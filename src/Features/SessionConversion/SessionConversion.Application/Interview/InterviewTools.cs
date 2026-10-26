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

    /// <summary>The main chat's tool for saving a draft as a session.</summary>
    public const string SaveSession = "save_session";

    /// <summary>Records the questionnaire the swimmer is asked and, once answered, their answers. Never offered to a model: the interview's own driver raises it.</summary>
    public const string Ask = "ask";

    /// <summary>The <c>ConversationKind</c> of the interviewer's own conversation, and the scope of its tools.</summary>
    public const string AgentKind = "swim_board_interview";

    /// <summary>
    /// Thinking effort for the interview's two model calls. Reading a board is mostly mechanical, and thinking tokens are billed as
    /// output, so this stays below the model's default. Raise it if draft quality drops.
    /// </summary>
    public const string InterviewEffort = "medium";

    /// <summary>Room for the whole session in one reply: a big board is a few thousand tokens of JSON.</summary>
    public const int ConversionMaxTokens = 16000;

    public const string AskDescription = "The swimmer's questionnaire about the path they swam, and their answers.";

    public static readonly string AskSchema = ConversionSchemas.Questionnaire;

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

    public const string SaveSessionDescription =
        "Saves a draft made by read_swim_board as the user's session, once they are happy with it. Needs the date they swam and the pool course; " +
        "ask if they have not said. The title is optional: leave it out to use the draft's own, or give a short one if the user asks for a different name. " +
        "Saving the same draft twice is safe. The user is asked to confirm before it is saved.";

    public const string SaveSessionSchema = """
        {"type":"object","properties":{"draftId":{"type":"string"},"performedOn":{"type":"string","description":"date swum, yyyy-MM-dd"},"course":{"type":"string","enum":["scy","scm","lcm"]},"title":{"type":"string","description":"2-6 words; omit to use the draft's"}},"required":["draftId","performedOn","course"]}
        """;

    private const string NodeDefs = """
        "node":{"type":"object","properties":{"kind":{"type":"string","enum":["item","group"]},"reps":{"type":"integer"},"timing":{"type":"object","properties":{"kind":{"type":"string","enum":["rest","sendOff"]},"seconds":{"type":"integer"}},"required":["kind","seconds"]},"parts":{"type":"array","minItems":1,"items":{"$ref":"#/$defs/part"},"description":"one rep"},"progression":{"$ref":"#/$defs/progression"},"note":{"type":"string"},"repeat":{"type":"integer"},"items":{"type":"array","items":{"$ref":"#/$defs/node"}}},"required":["kind"]},
        "progression":{"type":"object","description":"a descend: effort rising across the reps of an item","properties":{"span":{"type":"integer","description":"reps per ramp (the n in 'dec 1-n'); omit to ramp the whole set"},"from":{"type":"string","description":"the board's wording for the easy end; omit if it gives none"},"to":{"type":"string","description":"the board's wording for the hard end; omit if it gives none"}}},
        "part":{"type":"object","properties":{"dist":{"type":"number"},"stroke":{"type":"string","enum":["free","back","breast","fly","im","choice"]},"mode":{"type":"string","enum":["swim","kick","pull","drill","scull"]},"equip":{"type":"array","items":{"type":"string"}},"effort":{"type":"string"}},"required":["dist","stroke"]}
        """;

    private static string WithDefs(string schema) => schema.Replace("__DEFS__", NodeDefs.Trim());
}
