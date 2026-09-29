# SwimRebase REST API Reference

_Last updated: 28/09/2026_

Base URL: `https://swimrebase-pro.azurewebsites.net` (production) / `https://swimrebase-dev.azurewebsites.net` (staging)

All endpoints require `Authorization: Bearer <supabase-jwt>`. Requests without a valid token return `401 Unauthorized`.

---

## AI Chat — `/api/ai`

### `POST /api/ai/conversations/messages`
Start a new conversation with a message.

**Body:**
```json
{ "message": "string", "imageIds": ["guid"] }
```
`imageIds` is optional - see [Image uploads](#image-uploads) below. Each id must be an image you've already uploaded and confirmed for your own account, or the send is rejected.

**Response:** `text/event-stream` (SSE) — see [Streaming response shape](#streaming-response-shape) below.

---

### `POST /api/ai/conversations/{conversationId}/messages`
Continue an existing conversation.

**Path params:** `conversationId` (guid)

**Body:**
```json
{ "message": "string", "imageIds": ["guid"] }
```
`imageIds` optional, same as above.

**Response:** `text/event-stream` (SSE).

---

### Image uploads

Images are uploaded directly to blob storage, not through this API - the flow is upload URL → PUT bytes → confirm → reference by id in a message.

#### `POST /api/ai/images/upload-url`
Request a presigned URL to upload one image directly to storage.

**Body:**
```json
{ "fileName": "string", "contentType": "image/jpeg" }
```
`contentType` must be one of `image/jpeg`, `image/png`, `image/gif`, `image/webp`.

**Response:**
```json
{ "imageId": "guid", "uploadUrl": "string", "expiresAt": "datetime" }
```
`uploadUrl` is a presigned PUT URL, valid for 15 minutes. `PUT` the raw image bytes to it directly (not through this API), with a `Content-Type` header that exactly matches the `contentType` you requested - the URL's signature is bound to that content type and the upload fails if it doesn't match.

#### `POST /api/ai/images/{imageId}/confirm`
Call this after the direct upload finishes, before referencing the image in a chat message. This is what actually verifies the bytes landed in storage - a presigned upload URL alone gives no such signal.

**Path params:** `imageId` (guid)

**Response:**
- `200 OK` `{ "imageId": "guid", "status": "Confirmed", "sizeBytes": 123 }`
- `404 Not Found` - no such image (or it isn't yours)
- `409 Conflict` - nothing has actually been uploaded to this image's URL yet
- `400 Bad Request` - the uploaded file exceeds the 5 MB limit (the object is deleted from storage automatically in this case)

Once confirmed, pass `imageId` in `imageIds` on a `POST .../messages` call to attach it to that message. Only the permanent image id is ever sent in a chat payload - never a raw storage key or a presigned URL, both of which the server resolves internally (a fresh, short-lived presigned URL is generated from the stored key every time the image is actually sent to the model, so it never depends on a URL staying valid for the life of the conversation).

#### `DELETE /api/ai/images/{imageId}`
Delete an uploaded image - removes both the blob from storage and the record of it. Only possible before the image has ever been sent in a message; once attached to a message its storage key is baked permanently into that message's content, so deleting it would break that message's image forever.

**Path params:** `imageId` (guid)

**Response:**
- `204 No Content` - deleted
- `404 Not Found` - no such image (or it isn't yours)
- `409 Conflict` - the image has already been sent in a message and can no longer be deleted

---

### `POST /api/ai/tool-requests/{toolExecutionRequestId}/resolve`
Approve or deny a gated tool call (a write/destructive action the AI wants to perform, e.g. creating a focus or timetable slot). Returns `400`-equivalent (`error` SSE event) if the request is actually an `ask_user_question` — answer those via the `/answer` endpoint below instead.

**Path params:** `toolExecutionRequestId` (guid)

**Body:**
```json
{ "approve": true, "denialReason": null }
```
`denialReason` is optional, used only when `approve` is `false`.

**Response:** `text/event-stream` (SSE).

---

### `POST /api/ai/tool-requests/{toolExecutionRequestId}/answer`
Answer a clarifying question the AI asked mid-turn (the `ask_user_question` tool - see the `question_asked` SSE event below). Returns an `error` SSE event if the request isn't actually a pending question.

**Path params:** `toolExecutionRequestId` (guid)

**Body:** one answer for a single question, or one per question (in the order asked) when a `questions_asked` event asked several at once - send exactly one of the two:
```json
{ "answer": "string" }
```
```json
{ "answers": ["string", "string"] }
```
Each answer is always free text. The `question_asked` event's `options` are AI-suggested quick replies only, never an enforced/closed set - clients should render them as tappable chips *and* always offer a free-text ("Other") input, and send whatever the user picked or typed as `answer` either way.

**Response:** `text/event-stream` (SSE).

---

### `POST /api/ai/tool-requests/{toolExecutionRequestId}/cancel`
Abandon a running sub-agent - a tool call the main chat is blocked on while a specialist talks to the user in its own conversation (see "Sub-agents" below). Whatever the sub-agent was waiting on is denied, the tool call is settled as declined, and the main chat resumes (the model is told the user cancelled). Returns an `error` SSE event if the request is not a running sub-agent (use the `toolExecutionRequestId` from the `sub_agent_started` event, or the `InProgress` entry from `/pending` after reconnecting).

**Path params:** `toolExecutionRequestId` (guid) - the *parent* request from `sub_agent_started`.

**Response:** `text/event-stream` (SSE).

---

### `GET /api/ai/conversations?sortBy={LastMessageAt|CreatedAt}&descending={bool}&includeArchived={bool}`
List the current user's conversations. All query params optional - default is `sortBy=LastMessageAt&descending=true&includeArchived=false` (most recently active first, archived chats excluded, which is also the shape of the `(UserId, IsArchived, LastMessageAt)` index). Pass `includeArchived=true` to also list archived chats.

`title` is auto-generated from the conversation's first message (truncated to ~60 chars at a word boundary) when it's created, and can be changed afterwards via the rename endpoint below.

**Response:**
```json
[
  { "id": "guid", "title": "string|null", "createdAt": "datetime", "lastMessageAt": "datetime", "isArchived": false }
]
```

---

### `PATCH /api/ai/conversations/{conversationId}`
Renames a conversation.

**Path params:** `conversationId` (guid)

**Body:** `{ "title": "string" }`

**Response:** `204 No Content`, or `404` if the conversation doesn't exist (or belongs to someone else), or `400` if `title` is empty/whitespace.

---

### `POST /api/ai/conversations/{conversationId}/archive`
Archives a conversation and its entire sub-agent tree (recursively, however deep), and auto-denies any tool request still awaiting approval anywhere in that tree (with `denialReason: "Conversation archived"`), so nothing is left silently blocked. Archived conversations are excluded from `GET /api/ai/conversations` by default, and can no longer be sent new messages (`POST .../messages` returns an `error` event) - existing history is still readable.

**Path params:** `conversationId` (guid)

**Response:** `204 No Content`, or `404` if the conversation doesn't exist (or belongs to someone else).

---

### `POST /api/ai/conversations/{conversationId}/unarchive`
Restores a single archived conversation (does not cascade to its sub-agent tree, and does not reverse any auto-denials from when it was archived).

**Path params:** `conversationId` (guid)

**Response:** `204 No Content`, or `404` if the conversation doesn't exist (or belongs to someone else).

---

### `GET /api/ai/conversations/{conversationId}/messages?before={int}&limit={int}`
Keyset-paginated message history for a conversation, newest-first. `before` (optional) is the `sequenceNumber` of the oldest message already loaded - omit for the first page, then pass `nextBefore` from the previous response to fetch older messages. `limit` optional, default 50, clamped to 1-100.

**Path params:** `conversationId` (guid)

**Response:**
```json
{
  "items": [
    { "id": "guid", "sequenceNumber": 12, "role": "User|Assistant|Tool", "content": "string",
      "images": [{ "imageId": "guid", "contentType": "image/jpeg", "url": "string" }],
      "createdAt": "datetime" }
  ],
  "hasMore": true,
  "nextBefore": 8
}
```
`nextBefore` is `null` when `hasMore` is `false`.

`images` lists the images attached to that message (empty when there are none). `url` is a presigned download URL generated fresh on every request, valid for 15 minutes - render it directly, and refetch the history if it has expired. `imageId` is `null` only for images attached before ids were stored on messages.

For a `Tool`-role message, `content` is itself a JSON-encoded array (still a string field - decode it client-side), one entry per tool call resolved in that turn:
```json
[
  {
    "toolUseId": "string",
    "toolName": "get_timetable",
    "displayName": "Checking your timetable",
    "effect": "Neutral",
    "success": true,
    "affectedCount": 6,
    "resultJson": "string"
  }
]
```
`effect` is `Neutral`/`Creative`/`Destructive` (see `tool_started`/`tool_completed` SSE events above). `affectedCount` is tool-defined - entries fetched for a read, rows created/deleted for a write. `resultJson` is the tool's full raw output (itself a JSON string) for a details/expand view. This is a UI-friendly projection only - it's derived from, and never fed back into, the exact provider-protocol content actually replayed to the model on the next turn.

---

### `GET /api/ai/conversations/{conversationId}/tool-requests/pending`
List still-open tool requests (`PendingApproval`, or `InProgress` for a running sub-agent) for a conversation, oldest first - including questions asked by any sub-agent the conversation started (their `conversationId` is the sub-agent's, not this one's). Use this to recover a conversation's state after reconnecting (e.g. after a dropped SSE connection or a server crash mid-turn) instead of relying solely on the `permission_required`/`question_asked` SSE events, which are only ever emitted once and are lost if the client wasn't connected to receive them.

**Path params:** `conversationId` (guid)

**Response:**
```json
[
  { "id": "guid", "conversationId": "guid", "status": "PendingApproval|InProgress", "assistantMessageId": "guid", "toolName": "string", "inputJson": "string", "createdAt": "datetime" }
]
```
`toolName` is `"ask_user_question"` for a pending question (parse `question`/`options` out of `inputJson`) - resolve those via `POST /api/ai/tool-requests/{toolExecutionRequestId}/answer`. A sub-agent's question has a different `toolName` (e.g. `"ask"` for the swim-board interview - parse `questions[]` out of `inputJson`, showing everything up to and including the first `kind: "fork"`); answer it the same way, with `answers`. A running sub-agent (e.g. `"read_swim_board"`) is listed with `status: "InProgress"` - it blocks the conversation until its question is answered or it is cancelled, and its `id` is what the `/cancel` endpoint takes. Only `PendingApproval` entries are answerable/resolvable. Everything else is a gated write/destructive call - resolve via `.../resolve`. An empty array means the conversation has nothing outstanding - if the frontend appears "stuck" waiting for approval, calling this endpoint is the way to tell whether there's really a request to resolve or whether the client's local state is just out of sync.

---

### `GET /api/ai/usage?from={datetime}&to={datetime}`
Token usage summary for the current user. Both query params optional.

**Response:** usage summary object (shape defined by `ITokenUsageRepository.GetSummaryForUserAsync`).

---

### Sub-agents (e.g. reading a swim workout board)

Some tool calls hand the conversation to a specialist that talks to the user directly, in its own conversation, while the main chat waits. Everything still streams over the same SSE connection, so the client shows it all in one chat. Today there is one: **`read_swim_board`**. The AI calls it when the user has attached a photo of a workout board (it is only offered when the conversation contains an image) and wants it logged.

1. The main assistant's turn ends with `sub_agent_started` (`toolUseId` is the tool call to nest the following events under; `toolExecutionRequestId` is what `/cancel` takes; `childConversationId` is the specialist's conversation).
2. The specialist asks about the swimmer's path through the board as `questions_asked` events (its `conversationId` is `childConversationId`). Answer each with `POST .../tool-requests/{toolExecutionRequestId}/answer` and `{ "answers": [...] }` - one entry per question in the event, in order. A question with `kind: "Fork"` is always last in its event (it decides which questions come next). Each answer is either the option's number (`"2"`) or free text. The answer's response stream continues the interview - the next `questions_asked` - or, when it is finished, `sub_agent_completed` followed by the main assistant's reply (a summary of the **draft** session, with numbered sets and computed totals).
3. While the interview runs, the main chat is blocked: sending a message returns an `error` event. To abandon it, `POST .../tool-requests/{toolExecutionRequestId}/cancel` with the id from `sub_agent_started` (or the `InProgress` entry of `/pending` after reconnecting).
4. The result is a **draft**: it is not in the user's log yet. To change it ("actually I skipped the kick set") the user just says so in the main chat; the AI edits the draft with its `edit_draft` tool.
5. Once the user is happy, the AI saves it with its `save_session` tool (it asks for the date swum and the pool course if the user has not said). That is a gated tool: a `permission_required` event with `toolName: "save_session"` and `inputJson` `{ draftId, performedOn, course, title? }` - approve it with `.../resolve`. The result carries the new `sessionId`; the saved session is then available under `/api/sessions`. Saving the same draft twice returns the same session.

Sub-agent conversations are an implementation detail: they never appear in `GET /api/ai/conversations`, and cannot be messaged directly.

---

### Streaming response shape

The two message-sending endpoints and the tool-resolve/answer endpoints all stream Server-Sent Events. Each event is JSON with a `type` discriminator:

| `type` | Fields | Meaning |
|---|---|---|
| `text_delta` | `text` | A chunk of the assistant's reply text |
| `turn_started` | `conversationId` | A new model call just started — show a "thinking" indicator until text or a tool event arrives |
| `tool_started` | `conversationId`, `toolUseId`, `toolName`, `displayName`, `effect` | A tool is about to run (auto-executed read tool, or a freshly-approved gated one) — `displayName` is the UI-friendly label, e.g. "Checking your timetable"; `effect` is `Neutral`/`Creative`/`Destructive` |
| `tool_completed` | `conversationId`, `toolUseId`, `toolName`, `displayName`, `effect`, `success`, `affectedCount` | The tool finished — `affectedCount` is tool-defined (e.g. sessions fetched, rows created/deleted) |
| `permission_required` | `conversationId`, `toolExecutionRequestId`, `toolName`, `displayName`, `effect`, `inputJson` | The AI wants to run a gated tool — needs a `resolve` call |
| `permission_resolved` | `conversationId`, `toolExecutionRequestId`, `approved` | Confirms a resolve was recorded (sibling gated calls from the same turn still pending) |
| `question_asked` | `conversationId`, `toolExecutionRequestId`, `question`, `options` | The AI is asking a clarifying question — needs an `answer` call. `options` are advisory quick-reply suggestions only; always also offer free-text entry |
| `questions_asked` | `conversationId`, `toolExecutionRequestId`, `questions` | A sub-agent is asking several questions at once - needs an `answer` call with one entry in `answers` per question, in order. Each question is `{ text, kind, options }`; `kind` is `Detail` or `Fork`. `options` are quick-reply suggestions only (a number or typed text both work); always offer free-text entry |
| `sub_agent_started` | `parentConversationId`, `toolExecutionRequestId`, `toolUseId`, `childConversationId`, `displayName` | A tool call started a sub-agent in its own conversation. Render everything until the matching `sub_agent_completed` as nested under that tool call (`toolUseId`) in the same chat. Its events carry `childConversationId` as their `conversationId` |
| `sub_agent_completed` | `parentConversationId`, `toolExecutionRequestId`, `childConversationId`, `displayName`, `success` | The sub-agent finished (or was cancelled); the main chat resumes right after |
| `usage` | `conversationId`, `model`, `inputTokens`, `outputTokens`, `cacheCreationInputTokens`, `cacheReadInputTokens` | Token usage for the turn that just completed |
| `completed` | `conversationId`, `assistantMessageId` | The turn finished normally |
| `error` | `message` | Something went wrong mid-turn |

Consume with `curl -N` (disables buffering) or an `EventSource`/SSE client.

---

## Focuses — `/api/focuses`

### `GET /api/focuses`
List the current user's active focuses.

**Response:** array of `FocusResponse` (see shape below).

---

### `POST /api/focuses`
Create a focus.

**Body:**
```json
{
  "tagLine": "string",
  "prompt": "string",
  "totalDays": 30,
  "priority": null,
  "positivePoints": [
    { "order": 1, "tagLine": "string", "description": "string" }
  ],
  "negativePoints": [
    { "order": 1, "tagLine": "string", "description": "string" }
  ]
}
```
`priority` is optional (nullable int).

**Response:** `FocusResponse`.

---

### `POST /api/focuses/{focusId}/check-ins?date={date}`
Log a check-in for a focus. `date` (yyyy-MM-dd) optional, defaults to today.

**Path params:** `focusId` (guid)

**Response:** `204 No Content` on success, `404 Not Found` if the focus doesn't exist / isn't active.

---

### `PUT /api/focuses/{focusId}/priority`
Move a focus to a new priority position.

**Path params:** `focusId` (guid)

**Body:**
```json
{ "priority": 2 }
```

**Response:** `204 No Content` / `404 Not Found`.

---

### `PUT /api/focuses/{focusId}`
Replace a focus's tagLine, prompt, totalDays, and comparison points. Full replacement, not a merge — include every point you want kept. Does not change priority (use the `priority` endpoint for that). Active-only — editing a completed or abandoned focus returns `404`.

**Path params:** `focusId` (guid)

**Body:**
```json
{
  "tagLine": "string",
  "prompt": "string",
  "totalDays": 30,
  "positivePoints": [
    { "tagLine": "string", "description": "string" }
  ],
  "negativePoints": [
    { "tagLine": "string", "description": "string" }
  ]
}
```

**Response:** `200 FocusResponse` / `404 Not Found`.

---

### `DELETE /api/focuses/{focusId}`
Permanently delete a focus — active, completed, or abandoned. This is a hard delete: the focus and its check-in history are removed for good, not archived. If the focus was active, the remaining active focuses are automatically re-ranked to close the priority gap.

**Path params:** `focusId` (guid)

**Response:** `204 No Content` / `404 Not Found`.

---

### `POST /api/focuses/{focusId}/abandon`
Abandon a focus.

**Path params:** `focusId` (guid)

**Response:** `204 No Content` / `404 Not Found`.

---

### `FocusResponse` shape
```json
{
  "id": "guid",
  "tagLine": "string",
  "prompt": "string",
  "totalDays": 30,
  "daysCompleted": 5,
  "priority": 1,
  "status": "Active | Completed | Abandoned",
  "origin": "Manual | Ai",
  "startedAt": "yyyy-MM-dd",
  "endedAt": "yyyy-MM-dd|null",
  "positivePoints": [{ "order": 1, "tagLine": "string", "description": "string" }],
  "negativePoints": [{ "order": 1, "tagLine": "string", "description": "string" }]
}
```

---

## Sessions — `/api/sessions`

Saved swims: a record of what was swum, logged after the fact. Today they are created by the AI's `save_session` tool (see Sub-agents above); these endpoints read them, correct them, and add them up. Distances are always computed from the session's structure, never entered.

A session's `content` is a tree. `sets` holds **items** (`{ kind: "item", reps, timing?, parts, note? }` - `parts` is what one rep is made of, each `{ dist, stroke, mode, equip, effort, intensity? }`) and **groups** (`{ kind: "group", repeat, items }`, nesting at most 3 deep). `stroke` is `free | back | breast | fly | im | choice`; an `im` part counts as a quarter of each stroke. `intensity` is `{ level, scale }` on one global 1-10 scale, mapped from `effort` (words like "easy"/"hard" or a percentage); it is absent when the effort could not be mapped.

### `GET /api/sessions?from={date}&to={date}&limit={int}&offset={int}`
Newest first. All parameters optional (`limit` 1-100, default 50).

**Response:** array of `{ id, performedOn, title, course, unit, source, totalDistance, createdAt }`. `course` is `Scy | Scm | Lcm`, `unit` is `Yards | Metres` (yards for `Scy`), `source` is `Ai | Logged`.

### `GET /api/sessions/{id}`
**Response:** the summary fields above plus `content` (the tree) and `outline` (numbered text). `404` if it is not the caller's.

### `POST /api/sessions/{id}/edits`
Changes a session with small path-addressed edits; the total and the per-part facts are rebuilt with it.

```json
{ "edits": [
  { "op": "insert", "path": "3", "node": { "kind": "item", "reps": 8, "parts": [{ "dist": 25, "stroke": "free", "mode": "kick", "effort": "fast" }] } },
  { "op": "remove", "path": "1.0" }
] }
```

`op` is `remove` (path) | `replace` (path, node) | `insert` (path = position to insert at, `node`) | `set_group` (`group`; empty clears it). `path` is dot-separated indexes into the outline: `"2"` is the third set, `"1.0"` the first item inside the second. Edits apply in order, each seeing the result of the ones before.

**Response:** the updated session (as for `GET`). `400` with `{ error }` (and `problems` for a session that would be invalid) if an edit is malformed or the result fails validation; nothing is changed.

### `GET /api/sessions/metrics`
Filter, group and measure what was swum. All parameters optional; repeat a parameter for several values (`strokes=fly&strokes=back`).

| Parameter | Meaning |
|---|---|
| `from`, `to` | Date range (yyyy-MM-dd), inclusive |
| `strokes` | `free | back | breast | fly | im | choice` (an IM part counts towards fly, back, breast and free) |
| `modes` | `swim | kick | pull | drill | scull` |
| `minIntensity`, `maxIntensity` | Bounds on the 1-10 scale. Setting either leaves out distance with no intensity |
| `groupBy` | Up to 2 of `stroke | mode | intensity | day | week | month | session` (weeks start Monday and are named by that date) |
| `measure` | `distance` (default), `sessions`, or `avgIntensity` (weighted by distance) |

Examples: `?strokes=fly&groupBy=month` (how much fly per month), `?minIntensity=8&groupBy=week` (distance at hard effort per week), `?measure=avgIntensity&groupBy=month`.

**Response:**
```json
{ "measure": "Distance", "groupBy": ["Stroke"], "totalDistance": 1600, "units": ["Metres"],
  "rows": [ { "keys": ["fly"], "value": 600 }, { "keys": ["free"], "value": 500 } ] }
```
`rows` reads in date/intensity order when grouped by time or intensity, otherwise biggest first. `units` lists every distance unit present; more than one means metres and yards are mixed - nothing is converted. `value` is `null` when the measure does not apply (an average with no intensity). `400` with `{ error }` for a question that cannot be answered (e.g. more than 2 `groupBy`).

---

## Timetable — `/api/timetable`

### `GET /api/timetable?asOf={date}`
Get the timetable as it stood on a given date. `asOf` (yyyy-MM-dd) optional, defaults to today.

**Response:** array of `TimetableEntryResponse` (see shape below).

---

### `PUT /api/timetable/slots/{slotId?}`
Create a new slot (omit `slotId`) or version a slot into a new state (provide `slotId`). The timetable is versioned — this never edits in place.

**Path params:** `slotId` (guid, optional)

**Body:**
```json
{
  "dayOfWeek": "Monday",
  "startTime": "06:00:00",
  "endTime": "07:30:00",
  "sessionType": "Swim",
  "label": "string|null",
  "effectiveFrom": "yyyy-MM-dd",
  "expectedSession": {
    "summary": "string",
    "totalDistanceMeters": 3000,
    "primaryEffortZone": "Aerobic"
  }
}
```
`label` and `expectedSession` are optional.

**Response:** `TimetableEntryResponse`.

---

### `DELETE /api/timetable/slots/{slotId}?effectiveTo={date}`
Close out a slot as of a date.

**Path params:** `slotId` (guid)
**Query params:** `effectiveTo` (yyyy-MM-dd) — required.

**Response:** `204 No Content` / `404 Not Found`.

---

### `TimetableEntryResponse` shape
```json
{
  "id": "guid",
  "slotId": "guid",
  "dayOfWeek": "Monday",
  "startTime": "06:00:00",
  "endTime": "07:30:00",
  "sessionType": "Swim",
  "label": "string|null",
  "effectiveFrom": "yyyy-MM-dd",
  "effectiveTo": "yyyy-MM-dd|null",
  "expectedSession": {
    "summary": "string",
    "totalDistanceMeters": 3000,
    "primaryEffortZone": "Aerobic"
  }
}
```
`expectedSession` is `null` if none was set.

---

## Notes

- `DayOfWeek`, `SessionType`, `EffortZone`, `FocusStatus`, `FocusOrigin` are enums — check the actual enum definitions in `src/Features/*/Domain/Models/` for their exact member names before relying on the examples above.
- A 500 response generally means an app-layer exception (e.g. not-found/access-denied cases) is leaking uncaught rather than being mapped to a proper status code — this is a known gap, not necessarily a client-side mistake.
