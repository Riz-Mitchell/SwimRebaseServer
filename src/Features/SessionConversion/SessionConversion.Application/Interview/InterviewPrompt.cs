using Sessions.Domain.Calculations;

namespace SessionConversion.Application.Interview;

/// <summary>The system prompts of the interview's two model calls. Each one is answered as JSON of a fixed schema (<see cref="ConversionSchemas"/>).</summary>
public static class InterviewPrompt
{
    public const string Questionnaire = """
        You find out what THIS swimmer actually swam on a workout board, so their own path through it can be recorded. You get a photo of the board (whiteboard, print, handwriting or typed) and sometimes notes from the swimmer's own chat message (e.g. their lane): treat anything those notes already answer as answered.

        Read the whole board. Note groups/lanes, either/or sets and sets with options. Then write every question the swimmer needs to answer, in board order, all at once. Answer with the JSON only.
        - kind=fork: a branch to completely different sets: which group/lane, "a / b / c", sprint OR distance, "choose one set". Give 2-5 short opts.
        - kind=detail: a choice inside one set that changes what was swum: stroke choice ("choice"), kick vs swim, which equipment, a rep count they chose.
        - id: a short unique id (q1, q2, ...).
        - showIf: a question that only matters on one branch of a fork sets showIf to {qid: that fork's id, opt: the exact opts text of that branch}. A question that applies whichever branch they took has showIf null.
        - Do not ask what the board already states. Do not ask hypothetical or speculative questions. Give 2-5 short opts when the board suggests them, otherwise an empty opts array; the swimmer can always type anything.
        - Do not ask for the date, the pool, or whether they changed anything: those are asked separately. A board with no choices at all gets an empty questions array.
        - efforts: every effort wording the board uses (zone names, pace names, intensity words like "A2", "threshold", "Wolf pace"), each once, copied exactly as written. Not instructions such as "descend 1-4" or "odds/evens". An empty array when the board has none. Never turn these into questions.
        """;

    public static readonly string Conversion = $$"""
        You turn a photo of a swim workout board into a record of what THIS swimmer actually swam. You get the board and the swimmer's answers to questions about the path they took, and sometimes notes from their chat message. Use what the swimmer told you over what the board says. Answer with the JSON only.

        LEDGER first: one entry per line or set on the board, in board order. boardLine is its text as written. outcome is swum (as written), modified (swum, but changed, shortened or a different option), skipped, other_group (another group's or lane's set, or an option they did not choose) or added (extra work the swimmer said they did, placed where it happened).
        SETS next: exactly the ledger entries that are swum, modified or added, in board order.

        WRITING THE SESSION
        - Only what was swum, in board order. Skipped sets are omitted; extra sets the swimmer added are included where they happened.
        - Sets for other groups/lanes are omitted. `group` = the swimmer's group name as written, or null if the board is for everyone.
        - `title`: 2-6 words naming what makes this workout distinctive: its focus or signature set ("Fly kick ladder", "Threshold 200s", "Sprint starts and turns"). Sentence case. Never the date, the total distance, or just "swim". If the swimmer's recent titles are listed, give a clearly different one. null only when the board gives nothing to go on.
        - kind=item: `reps` and `parts`. `parts` is what ONE rep is made of and always has at least one part, each with `dist` (per rep, decimals allowed for odd pool lengths like 12.5, at most 2 places) and `stroke`.
          "4x100 free" -> reps 4, one part {dist 100, stroke free}. "1 x 400 IM" -> reps 1, one part {dist 400, stroke im}. "8 x 50 kick" -> reps 8, one part {dist 50, stroke free, mode kick}.
          A rep that mixes strokes/modes/equipment/effort ("25 fly + 75 free", "50 kick + 50 swim") is several parts in order. Ladders/pyramids (100-200-300): one item per rung.
        - kind=group: `repeat` + `items`, for "3 rounds of..." / a bracket followed by "x3", or a rep pattern that repeats ("12x50 as 3 easy, 1 as 25 fast + 25 easy" -> repeat 3 of [3 x 50 easy, 1 x (25 fast + 25 easy)]). Groups may nest, but no more than 3 deep.
        - Alternating strokes ("8 x 50 alt fly/back", "fly on evens, back on odds"): a group with repeat = reps / 2 holding one item per stroke, e.g. repeat 4 of [1 x 50 fly, 1 x 50 back]. An odd count is that group plus one extra item.
        - stroke (every part needs one): free, back, breast, fly, im (a quarter of each in order fly, back, breast, free; its dist must divide by 4), or choice. "choice"/"any" -> the stroke they said, else "choice".
        - mode: kick, pull, drill (name it in note), scull, else swim. equip: fins, paddles, buoy, snorkel, board, band, parachute; an empty array when there is none.
        - effort (on the part): the board's own effort wording, copied exactly as written and never reworded, shortened or swapped for a similar term ("sub max" stays "sub max", "A2" stays "A2", "80%" stays "80%"). {{EffortVocabulary.Default.PromptText()}} Effort wording not in that list is still copied as written; null when the board gives none. Anything that is not an effort, e.g. "descend 1-4" or "odds/evens", goes in the item's note; note and timing are null when there is nothing to say.
        - timing: {kind: rest, seconds} for "15 sec rest"; {kind: sendOff, seconds} for "@1:30" / "on 1:30" (seconds = 90); null when the board gives none.
        - Never total anything; distances are computed for you.
        """;
}
