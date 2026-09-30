# Swim-board import: frontend changes

Applies to the `read_swim_board` flow (user attaches a workout-board photo and asks to log it). Full reference: `docs/API-Reference.md` ("Sub-agents" and "Streaming response shape").

**No endpoints added or removed.** Everything still goes through `POST /api/ai/conversations/messages`, `POST /api/ai/tool-requests/{id}/answer`, `.../resolve`, `.../cancel` and `GET .../tool-requests/pending`. What changed is the questions event, one new field on the approval event, and the event order.

## New flow

1. `POST conversations/messages` (message + `imageIds`) streams: `sub_agent_started` -> `questions_asked` (**one event, all questions**).
2. User answers once. `POST tool-requests/{questionsRequestId}/answer` with `{ "answers": [...] }`.
3. That response stream continues: `turn_started` -> `usage` -> `sub_agent_completed` -> `text_delta` -> `permission_required` (`toolName: "save_session"`, with `preview`) -> `completed`. **No model reply sits between the answers and the save card.**
4. User taps Save -> `POST tool-requests/{saveRequestId}/resolve` `{ "approve": true }`. The stream runs the save (`tool_started`/`tool_completed`) and ends with a short assistant confirmation.
5. User wants changes -> `{ "approve": false, "denialReason": "the last set was kicks" }` (or just cancels the card). The assistant asks what to change and edits the draft; a new `save_session` card follows later as before.

## `questions_asked` (changed)

Sent once per conversion. There are no more follow-up rounds after a fork.

```json
{ "type": "questions_asked", "conversationId": "...", "toolExecutionRequestId": "...",
  "questions": [
    { "id": "q1", "text": "Which lane?", "kind": "Fork", "options": ["Lane 1", "Lane 2"], "showIf": null, "fieldType": null },
    { "id": "q2", "text": "Kick or swim?", "kind": "Detail", "options": ["Kick", "Swim"], "showIf": { "questionId": "q1", "option": "Lane 2" }, "fieldType": null },
    { "id": "changed", "text": "Did you do everything as written...?", "kind": "Detail", "options": ["Everything as written", "I changed something"], "showIf": null, "fieldType": null },
    { "id": "performedOn", "text": "What date did you swim?", "kind": "Field", "options": [], "showIf": null, "fieldType": "date" },
    { "id": "course", "text": "Which pool?", "kind": "Field", "options": ["scy", "scm", "lcm"], "showIf": null, "fieldType": "course" }
  ] }
```

- New fields: `id`, `showIf`, `fieldType`. `kind` gains `Field` (alongside `Detail`, `Fork`). Old clients that ignore them still work but will show every branch.
- **`showIf`**: show the question only if the question `showIf.questionId` is visible and its answer equals `showIf.option` (case-insensitive). Evaluate top to bottom, so a question under a hidden fork is hidden too. Re-evaluate live as the user changes a fork answer.
- **`Field` questions** are asked by the app, always last. `fieldType: "date"` -> date picker, send `yyyy-MM-dd`, default to the user's local today. `fieldType: "course"` -> pick one of `options` (`scy`/`scm`/`lcm`; label them however you like, send the raw value). Send both if you can: they are what turns on the one-tap save.
- Options are still suggestions: always allow free text on `Detail`/`Fork` questions.

## Answering

Body unchanged: `{ "answers": ["...", ...] }`, one entry per question **in the order received, including hidden ones**. Send `""` for a hidden or unanswered question. An answer can be the option's number (`"2"`) or its text/free text. The server applies the same `showIf` rules, so a stale answer on a hidden branch is ignored.

## `permission_required` (one new field)

```json
{ "type": "permission_required", "toolName": "save_session", "displayName": "Saving your session", "effect": "Creative",
  "inputJson": "{\"draftId\":\"...\",\"performedOn\":\"2026-09-21\",\"course\":\"scy\"}",
  "preview": "Group: Lane 2\nDistance: 1700\n[0] 4 x 100 free ...\nBreakdown:\n...", ... }
```

- `preview` (string or `null`) is the drafted session as multi-line plain text: group, total distance, numbered sets (`[0] ...`, nested `[1.0] ...`), then a breakdown. Render it in the card (monospace / pre-wrap) so the user can accept without reading JSON. Show `performedOn` and `course` from `inputJson` alongside it (nice to allow editing them).
- The same `preview` is on `GET .../tool-requests/pending` for each `PendingApproval` entry, so a reload restores the card.

## Behaviours to handle

- **Card blocks the chat.** While the save card is pending, sending a message returns an `error` event, same as any pending gated call. Disable the input or show the card prominently until resolved.
- **Fallback (no card).** If the date or course answer was missing or invalid, no card is raised: the assistant replies with a draft summary and asks for what is missing, as it did before. Handle both endings.
- **Failure.** If the conversion fails you get `sub_agent_completed` with `success: false` followed by an assistant message explaining. Cancel is unchanged (`POST .../cancel` with the id from `sub_agent_started`).
- **Ordering.** `permission_required` and `completed` follow `sub_agent_completed` directly; do not wait for assistant text before showing the card. `completed.assistantMessageId` points at the short "Here's your session..." message that is also emitted as a `text_delta`.
- **Usage.** `usage` events now arrive per model call (two for a normal conversion) rather than per turn.

## Checklist

- [ ] Parse `id`, `showIf`, `fieldType`; handle `kind: "Field"`.
- [ ] Render all questions in one form; hide/show branches with `showIf`.
- [ ] Date picker + course selector for the two `Field` questions; send them in `answers`.
- [ ] Send `""` for hidden questions; keep answer order.
- [ ] Stop expecting a second `questions_asked` after a fork.
- [ ] Render `preview` in the `save_session` approval card (live and from `/pending`).
- [ ] Support the no-card fallback and the failure ending.
