using Ai.Application.Models;
using Ai.Application.Services;
using Ai.Domain.Models;
using Ai.Infrastructure;
using Microsoft.EntityFrameworkCore;

// One-off repair tool for conversations bricked by a gated tool call whose tool_result ended up not
// immediately following its tool_use in SequenceNumber order. Anthropic rejects any request whose
// history has that shape:
//   "messages.N: `tool_use` ids were found without `tool_result` blocks immediately after: ..."
//
// Two distinct ways this happens, both handled here:
//   1. MISSING - the tool_result message was never written at all (GatedToolApprovalResolver crashed
//      between marking the request resolved and persisting the result message - fixed by wrapping
//      both writes in a transaction, see GatedToolApprovalResolver.cs).
//   2. MISPLACED - the tool_result exists, but the user sent new messages while the request was still
//      PendingApproval (before the SendUserMessageAsync guard existed), each one persisted immediately
//      and pushed "next available sequence number" forward. When the request was finally resolved, its
//      result message landed after those stray messages instead of right after the tool_use.
//
// Nothing is ever re-executed - a Denied/Executed/Failed request's stored outcome/ResultJson is
// trusted as-is. This only fixes message existence/position, never tool behavior.
//
// Usage:
//   dotnet run --project tools/ToolRequestRepair -- <conversationId> [--connection "<conn-string>"] [--list] [--apply]
//
// --list dumps the live message sequence (as Anthropic would see it) and the tool_execution_requests
// for the conversation, for manual inspection - no repair logic runs.
//
// Without --apply, repair mode is a dry run: reports what it would change without writing. The
// connection string can also come from the CONVERSATION_REPAIR_CONNECTION environment variable
// instead of --connection, so it never has to be typed on the command line / land in shell history.

if (args.Length == 0 || args[0].StartsWith('-'))
{
    Console.WriteLine("Usage: dotnet run --project tools/ToolRequestRepair -- <conversationId> [--connection \"<conn-string>\"] [--list] [--apply]");
    return 1;
}

if (!Guid.TryParse(args[0], out var conversationId))
{
    Console.WriteLine($"'{args[0]}' is not a valid conversation id (guid).");
    return 1;
}

var apply = args.Contains("--apply");

string? connectionString = null;
var connectionFlagIndex = Array.IndexOf(args, "--connection");
if (connectionFlagIndex >= 0 && connectionFlagIndex + 1 < args.Length)
{
    connectionString = args[connectionFlagIndex + 1];
}
connectionString ??= Environment.GetEnvironmentVariable("CONVERSATION_REPAIR_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("No connection string. Pass --connection \"<conn-string>\" or set CONVERSATION_REPAIR_CONNECTION.");
    return 1;
}

var optionsBuilder = new DbContextOptionsBuilder<AiDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention();

await using var db = new AiDbContext(optionsBuilder.Options);

var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId);
if (conversation is null)
{
    Console.WriteLine($"No conversation found with id {conversationId}.");
    return 1;
}

Console.WriteLine($"Conversation {conversationId} (user {conversation.UserId}, last message at {conversation.LastMessageAt:u}).");

if (args.Contains("--list"))
{
    await ListAsync(db, conversationId);
    return 0;
}

// Admin escape hatch: directly set one message's sequence number. For manually cleaning up after a
// bad run (e.g. one interrupted mid-renumber) - the normal repair logic below doesn't know how to
// reason about arbitrary out-of-range values, only about tool_use/tool_result placement.
var setSeqFlagIndex = Array.IndexOf(args, "--set-sequence");
if (setSeqFlagIndex >= 0)
{
    var messageId = Guid.Parse(args[setSeqFlagIndex + 1]);
    var newSeq = int.Parse(args[setSeqFlagIndex + 2]);
    var message = await db.ConversationMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId);
    if (message is null)
    {
        Console.WriteLine($"No message {messageId} found in this conversation.");
        return 1;
    }

    Console.WriteLine($"Message {messageId}: sequenceNumber {message.SequenceNumber} -> {newSeq}");
    if (apply)
    {
        message.SequenceNumber = newSeq;
        await db.SaveChangesAsync();
        Console.WriteLine("Committed.");
    }
    else
    {
        Console.WriteLine("Dry run - pass --apply to commit.");
    }

    return 0;
}

Console.WriteLine(apply ? "Mode: APPLY (writes will be committed)" : "Mode: DRY RUN (pass --apply to actually write)");
Console.WriteLine();

// Const large enough that it can never collide with a real sequence number, used to vacate slots
// before reassigning them - see MoveMessagesAsync.
const int TempOffset = 1_000_000_000;

var repairedAny = false;

// Re-fetch fresh each iteration: an earlier repair in this loop can renumber messages a later
// group's calculation depends on, so we don't trust an initial in-memory snapshot across repairs.
while (true)
{
    var allRequests = await db.ToolExecutionRequests.Where(t => t.ConversationId == conversationId).ToListAsync();
    var allMessages = await db.ConversationMessages
        .Where(m => m.ConversationId == conversationId)
        .OrderBy(m => m.SequenceNumber)
        .ToListAsync();
    var messagesById = allMessages.ToDictionary(m => m.Id);

    // Map every resolved tool_use id to the (single) Tool-role message that already carries its
    // tool_result, if one exists anywhere in history.
    var toolResultMessageByToolUseId = new Dictionary<string, ConversationMessage>();
    foreach (var m in allMessages.Where(m => m.Role == MessageRole.Tool))
    {
        foreach (var block in ChatContentJson.Deserialize(m.RawContentJson).OfType<ToolResultContentBlock>())
        {
            toolResultMessageByToolUseId[block.ToolUseId] = m;
        }
    }

    var fixedThisPass = false;

    foreach (var group in allRequests.GroupBy(t => t.AssistantMessageId))
    {
        var siblings = group.ToList();

        if (siblings.Any(s => s.Status == ToolExecutionStatus.PendingApproval))
        {
            continue; // Genuinely still pending - not our concern.
        }

        if (!messagesById.TryGetValue(group.Key, out var assistantMessage))
        {
            Console.WriteLine($"Turn {group.Key}: no assistant message found for this id - skipping, needs manual review.");
            continue;
        }

        var targetSeq = assistantMessage.SequenceNumber + 1;

        var resultMessages = siblings
            .Select(s => toolResultMessageByToolUseId.GetValueOrDefault(s.AnthropicToolUseId))
            .ToList();

        ConversationMessage? existing;
        if (resultMessages.All(m => m is null))
        {
            existing = null;
        }
        else if (resultMessages.All(m => m is not null && m.Id == resultMessages[0]!.Id))
        {
            existing = resultMessages[0];
        }
        else
        {
            Console.WriteLine($"Turn {group.Key}: sibling calls have inconsistent tool_result coverage - skipping, needs manual review.");
            continue;
        }

        if (existing is not null && existing.SequenceNumber == targetSeq)
        {
            continue; // Already correctly placed.
        }

        if (existing is null)
        {
            Console.WriteLine($"Turn {group.Key}: tool_result message missing entirely. Reconstructing at sequenceNumber {targetSeq}:");
            var blocks = siblings.Select(BuildResultBlock).ToList();
            foreach (var (sibling, block) in siblings.Zip(blocks))
            {
                Console.WriteLine($"  - {sibling.ToolName} ({sibling.AnthropicToolUseId}): {sibling.Status} -> \"{Truncate(block.ResultJson, 80)}\" (isError={block.IsError})");
            }

            var newMessage = ConversationMessageFactory.CreateToolResultMessage(conversationId, targetSeq, blocks);

            // Vacate [targetSeq, max] by shifting each of those existing rows up by one (final value
            // computed from each message's CURRENT SequenceNumber, captured before any mutation),
            // then drop the new row into the freed targetSeq slot.
            var plan = allMessages
                .Where(m => m.SequenceNumber >= targetSeq)
                .Select(m => (message: m, finalSeq: m.SequenceNumber + 1))
                .ToList();

            if (apply)
            {
                await RenumberAsync(db, plan, TempOffset);
                db.ConversationMessages.Add(newMessage);
                await db.SaveChangesAsync();
            }
            else
            {
                Console.WriteLine($"  => would insert message {newMessage.Id} at {targetSeq}, shifting {plan.Count} later message(s) up by 1.");
            }
        }
        else if (existing.SequenceNumber > targetSeq)
        {
            var interlopers = allMessages
                .Where(m => m.SequenceNumber >= targetSeq && m.SequenceNumber < existing.SequenceNumber)
                .OrderBy(m => m.SequenceNumber)
                .ToList();

            Console.WriteLine($"Turn {group.Key}: tool_result exists (message {existing.Id}) but at sequenceNumber {existing.SequenceNumber}, " +
                               $"not immediately after the tool_use at {assistantMessage.SequenceNumber}. " +
                               $"{interlopers.Count} message(s) were sent while it was still pending and now sit in between:");
            foreach (var m in interlopers)
            {
                Console.WriteLine($"  - seq={m.SequenceNumber} role={m.Role} id={m.Id}");
            }
            Console.WriteLine($"  => would move tool_result to {targetSeq} and shift those {interlopers.Count} message(s) up by 1 (their relative order is preserved).");

            if (apply)
            {
                // Rotate: tool_result moves down to targetSeq, interlopers each move up by 1 - a
                // closed range, so nothing outside [targetSeq, existing.SequenceNumber] changes.
                // Final values are computed from CURRENT SequenceNumber before any mutation happens.
                var plan = new List<(ConversationMessage message, int finalSeq)> { (existing, targetSeq) };
                plan.AddRange(interlopers.Select(m => (message: m, finalSeq: m.SequenceNumber + 1)));

                await RenumberAsync(db, plan, TempOffset);
                await db.SaveChangesAsync();
            }
        }
        else
        {
            Console.WriteLine($"Turn {group.Key}: tool_result at {existing.SequenceNumber} is BEFORE the tool_use at {assistantMessage.SequenceNumber} - " +
                               "unexpected shape, skipping, needs manual review.");
            continue;
        }

        Console.WriteLine();
        repairedAny = true;
        fixedThisPass = true;

        if (apply)
        {
            break; // Re-fetch fresh state before evaluating the next group.
        }
    }

    if (!apply || !fixedThisPass)
    {
        break;
    }
}

if (!repairedAny)
{
    Console.WriteLine(apply ? "Nothing needed repair." : "Nothing to repair (or re-run with --apply to write the changes shown above).");
    return 0;
}

if (apply)
{
    conversation.LastMessageAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Console.WriteLine("Repair committed.");
}
else
{
    Console.WriteLine("Dry run only - re-run with --apply to write the changes shown above.");
}

return 0;

static ToolResultContentBlock BuildResultBlock(ToolExecutionRequest sibling) => sibling.Status switch
{
    ToolExecutionStatus.Denied => new ToolResultContentBlock(sibling.AnthropicToolUseId, "The user declined this action.", true),
    ToolExecutionStatus.Executed => new ToolResultContentBlock(sibling.AnthropicToolUseId, sibling.ResultJson ?? string.Empty, false),
    ToolExecutionStatus.Failed => new ToolResultContentBlock(sibling.AnthropicToolUseId, sibling.ResultJson ?? "Tool execution failed.", true),
    _ => throw new InvalidOperationException($"Unexpected status '{sibling.Status}' for an already-resolved request."),
};

// Moves every message in `plan` to its finalSeq, safely against the unique (ConversationId,
// SequenceNumber) index: each finalSeq was computed from that message's CURRENT SequenceNumber
// before this call. Phase 1 pushes every one of them out to current+offset (a huge constant, so none
// of them can collide with each other or with any untouched row) and commits that. Phase 2 then sets
// each to its real finalSeq in memory only - the caller commits phase 2, optionally combined with
// other changes (e.g. inserting a brand new row into a slot phase 1 just vacated).
static async Task RenumberAsync(AiDbContext db, List<(ConversationMessage message, int finalSeq)> plan, int offset)
{
    foreach (var (message, _) in plan)
    {
        message.SequenceNumber += offset;
    }
    await db.SaveChangesAsync();

    foreach (var (message, finalSeq) in plan)
    {
        message.SequenceNumber = finalSeq;
    }
}

static async Task ListAsync(AiDbContext db, Guid conversationId)
{
    var allRequests = await db.ToolExecutionRequests.Where(t => t.ConversationId == conversationId).ToListAsync();
    var allMessages = await db.ConversationMessages
        .Where(m => m.ConversationId == conversationId)
        .OrderBy(m => m.SequenceNumber)
        .ToListAsync();

    // What Anthropic actually sees: ChatContextMapper folds User/Tool rows onto ChatRole.User and
    // Assistant onto ChatRole.Assistant, in SequenceNumber order - a tool_use is only valid if the
    // very next entry in THIS list carries the matching tool_result, not just "somewhere in history".
    Console.WriteLine("--- live message sequence (as sent to Anthropic) ---");
    foreach (var m in allMessages.Where(m => !m.IsCompacted))
    {
        var blocks = ChatContentJson.Deserialize(m.RawContentJson);
        var blockSummary = string.Join(", ", blocks.Select(b => b switch
        {
            ToolUseContentBlock u => $"tool_use({u.Id}, {u.ToolName})",
            ToolResultContentBlock r => $"tool_result({r.ToolUseId}, isError={r.IsError})",
            TextContentBlock t => $"text(\"{Truncate(t.Text, 40)}\")",
            _ => b.GetType().Name,
        }));
        Console.WriteLine($"  seq={m.SequenceNumber,-4} role={m.Role,-9} id={m.Id} : {blockSummary}");
    }

    Console.WriteLine();
    Console.WriteLine("--- tool_execution_requests ---");
    foreach (var t in allRequests.OrderBy(t => t.CreatedAt))
    {
        Console.WriteLine($"  toolUseId={t.AnthropicToolUseId} assistantMessageId={t.AssistantMessageId} status={t.Status} createdAt={t.CreatedAt:u}");
    }
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
