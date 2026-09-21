namespace SessionConversion.Application.Interview;

/// <summary>
/// Names and schemas of the tools around the swim-board conversion. Every byte of the interviewer's tools is re-sent (from cache) on
/// each turn, so they are deliberately terse. Transport-neutral: the Ai feature wraps these in its own tool types.
/// </summary>
public static class InterviewTools
{
    /// <summary>The main chat's tool that starts the interview.</summary>
    public const string ReadSwimBoard = "read_swim_board";

    /// <summary>The main chat's tool for changing a draft afterwards.</summary>
    public const string EditDraft = "edit_draft";

    /// <summary>Interviewer-only: ask the swimmer questions.</summary>
    public const string Ask = "ask";

    /// <summary>Interviewer-only, and its terminal tool: write the draft session.</summary>
    public const string DraftSession = "draft_session";

    /// <summary>The <c>ConversationKind</c> of the interviewer's own conversation, and the scope of its tools.</summary>
    public const string AgentKind = "swim_board_interview";

    public const int MaxQuestionTurns = 12;

    public const string AskDescription =
        "Ask the swimmer questions about the path they swam. Questions in board order. kind=fork: a branch to completely different sets " +
        "(group/lane, A/B/C, sprint vs distance); a fork must be the LAST question and ends your turn. kind=detail: a choice inside one set. " +
        "opts: 0-5 short answers (the swimmer may type anything).";

    public const string AskSchema = """
        {"type":"object","properties":{"questions":{"type":"array","minItems":1,"items":{"type":"object","properties":{"q":{"type":"string"},"kind":{"type":"string","enum":["fork","detail"]},"opts":{"type":"array","items":{"type":"string"}}},"required":["q","kind","opts"]}}},"required":["questions"]}
        """;

    public const string DraftSessionDescription =
        "Write the session the swimmer actually swam (skipped sets omitted, added sets included). Call once nothing is left to ask.";

    public static readonly string DraftSessionSchema = WithDefs("""
        {"type":"object","properties":{"group":{"type":"string"},"sets":{"type":"array","items":{"$ref":"#/$defs/node"}}},"required":["sets"],"$defs":{__DEFS__}}
        """);

    public const string ReadSwimBoardDescription =
        "Reads a photo of a swim workout board attached to this conversation and works out what the user actually swam by interviewing them " +
        "(which lane/group, which options they chose, anything skipped or changed). The interview happens directly with the user; you do not " +
        "relay it. Returns a numbered DRAFT of the session with its distances, plus what the user said. Use it whenever the user attaches a " +
        "workout board and wants it logged or converted. `context` = anything the user already told you that answers the interview's questions (their lane, what they skipped).";

    public const string ReadSwimBoardSchema = """
        {"type":"object","properties":{"context":{"type":"string","description":"What the user already said that helps read the board (e.g. their lane). Omit if nothing."}}}
        """;

    public const string EditDraftDescription =
        "Changes a draft made by read_swim_board without rewriting it. Address sets by the [index] shown in the draft outline: \"2\" is the third set, " +
        "\"1.0\" the first item inside the second set. Edits apply in order and each sees the result of the ones before. " +
        "op=remove (path) | replace (path, node) | insert (path = position to insert at, node) | set_group (group; empty clears it). Returns the updated outline.";

    public static readonly string EditDraftSchema = WithDefs("""
        {"type":"object","properties":{"draftId":{"type":"string"},"edits":{"type":"array","minItems":1,"items":{"type":"object","properties":{"op":{"type":"string","enum":["remove","replace","insert","set_group"]},"path":{"type":"string"},"node":{"$ref":"#/$defs/node"},"group":{"type":"string"}},"required":["op"]}}},"required":["draftId","edits"],"$defs":{__DEFS__}}
        """);

    private const string NodeDefs = """
        "node":{"type":"object","properties":{"kind":{"type":"string","enum":["item","group"]},"reps":{"type":"integer"},"dist":{"type":"integer","description":"per rep"},"strokes":{"type":"array","items":{"type":"string","enum":["free","back","breast","fly","im","choice"]}},"mode":{"type":"string","enum":["swim","kick","pull","drill","scull"]},"equip":{"type":"array","items":{"type":"string"}},"effort":{"type":"string"},"rest":{"type":"integer","description":"seconds"},"parts":{"type":"array","items":{"$ref":"#/$defs/part"}},"note":{"type":"string"},"repeat":{"type":"integer"},"items":{"type":"array","items":{"$ref":"#/$defs/node"}}},"required":["kind"]},
        "part":{"type":"object","properties":{"dist":{"type":"integer"},"stroke":{"type":"string","enum":["free","back","breast","fly","im","choice"]},"mode":{"type":"string","enum":["swim","kick","pull","drill","scull"]},"equip":{"type":"array","items":{"type":"string"}},"effort":{"type":"string"}},"required":["dist"]}
        """;

    private static string WithDefs(string schema) => schema.Replace("__DEFS__", NodeDefs.Trim());
}
