# SwimRebase REST API Reference

_Last updated: 22/10/2026_

Base URL: `https://swimrebase-pro.azurewebsites.net` (production) / `https://swimrebase-dev.azurewebsites.net` (staging)

All endpoints require `Authorization: Bearer <supabase-jwt>` unless stated otherwise (the only exception is the RevenueCat webhook, see [Billing](#billing--apibilling)). Requests without a valid token return `401 Unauthorized`.

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
Approve or deny a gated tool call (a write/destructive action the AI wants to perform, e.g. saving a session). Returns `400`-equivalent (`error` SSE event) if the request is actually an `ask_user_question` — answer those via the `/answer` endpoint below instead.

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

`title` starts as the conversation's first message (truncated to ~60 chars at a word boundary) so the list is never blank. For a new chat, a cheap model then writes a short title (2-6 words) while the first reply streams, and the stream sends a `title_updated` event with it just before `completed` (see the SSE table). If that fails, times out or is turned off, the truncated title stays. A title the user set with the rename endpoint below is never overwritten, and a rename can be made at any time.

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
    "toolName": "get_session",
    "displayName": "Fetching your session",
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
  { "id": "guid", "conversationId": "guid", "status": "PendingApproval|InProgress", "assistantMessageId": "guid", "toolName": "string", "inputJson": "string", "preview": "string|null", "createdAt": "datetime" }
]
```
`preview` is a readable rendering of what approving would do, for tools that can show one (`save_session`: the drafted session's outline and total distance); `null` otherwise.
`toolName` is `"ask_user_question"` for a pending question (parse `question`/`options` out of `inputJson`) - resolve those via `POST /api/ai/tool-requests/{toolExecutionRequestId}/answer`. A sub-agent's question has a different `toolName` (e.g. `"ask"` for the swim-board questionnaire - parse `questions[]` out of `inputJson`; each has `id`, `q`, `kind` (`detail`, `fork` or `field`), `opts`, and optionally `showIf: { qid, opt }` and `fieldType`, exactly as in the `questions_asked` event below); answer it the same way, with `answers`. A running sub-agent (e.g. `"read_swim_board"`) is listed with `status: "InProgress"` - it blocks the conversation until its question is answered or it is cancelled, and its `id` is what the `/cancel` endpoint takes. Only `PendingApproval` entries are answerable/resolvable. Everything else is a gated write/destructive call - resolve via `.../resolve`. An empty array means the conversation has nothing outstanding - if the frontend appears "stuck" waiting for approval, calling this endpoint is the way to tell whether there's really a request to resolve or whether the client's local state is just out of sync.

---

### `GET /api/ai/usage?from={datetime}&to={datetime}`
Token usage summary for the current user. Both query params optional.

**Response:** usage summary object (shape defined by `ITokenUsageRepository.GetSummaryForUserAsync`).

---

### Sub-agents (e.g. reading a swim workout board)

Some tool calls hand the conversation to a specialist that talks to the user directly, in its own conversation, while the main chat waits. Everything still streams over the same SSE connection, so the client shows it all in one chat. Today there is one: **`read_swim_board`**. The AI calls it when the user has attached a photo of a workout board (it is only offered when the conversation contains an image) and wants it logged.

1. The main assistant's turn ends with `sub_agent_started` (`toolUseId` is the tool call to nest the following events under; `toolExecutionRequestId` is what `/cancel` takes; `childConversationId` is the specialist's conversation).
2. The specialist reads the board and asks **every** question about the swimmer's path at once, as one `questions_asked` event (its `conversationId` is `childConversationId`). Answer with `POST .../tool-requests/{toolExecutionRequestId}/answer` and `{ "answers": [...] }` - one entry per question in the event, in order, and an empty string `""` for a question that is hidden. Questions can be conditional: a question with `showIf: { questionId, option }` is shown only when the earlier question `questionId` was answered with `option` (compared case-insensitively; a fork's answer that matches no option shows none of its branches). A question hidden because its fork was answered otherwise - or because the fork it depends on is itself hidden - is left out and answered with `""`. `kind: "Field"` questions are asked by the app rather than the AI: `fieldType: "date"` (send `yyyy-MM-dd`; default to today) and `fieldType: "course"` (one of the `options`: `scy`, `scm`, `lcm`). Every other answer is either the option's number (`"2"`) or free text.
3. The answer's response stream runs the conversion: `sub_agent_completed`, then (when a valid date and course were answered) a `text_delta` and a `permission_required` for `save_session` whose `preview` is the drafted session (numbered sets and computed totals) and whose `inputJson` is `{ draftId, performedOn, course, title }` already filled in (`title` is the name the converter gave the workout, or `null`), then `completed`. No model turn happens between the answers and that card. If the date or course was missing or invalid the main assistant replies instead (a summary of the draft) and asks for what is missing.
4. While the conversion runs, the main chat is blocked: sending a message returns an `error` event. To abandon it, `POST .../tool-requests/{toolExecutionRequestId}/cancel` with the id from `sub_agent_started` (or the `InProgress` entry of `/pending` after reconnecting). The `save_session` card also blocks the chat until it is resolved.
5. The result is a **draft**: it is not in the user's log yet. Approve the card with `.../resolve` to save it - the result carries the new `sessionId`, the saved session is then available under `/api/sessions`, and the response stream continues with the main assistant's confirmation. Declining (send a `denialReason` such as "the last set was kicks") makes the assistant ask what to change and edit the draft with its `edit_draft` tool; it then saves with `save_session` again. Saving the same draft twice returns the same session.

Sub-agent conversations are an implementation detail: they never appear in `GET /api/ai/conversations`, and cannot be messaged directly.

---

### Streaming response shape

The two message-sending endpoints and the tool-resolve/answer endpoints all stream Server-Sent Events. Each event is JSON with a `type` discriminator:

| `type` | Fields | Meaning |
|---|---|---|
| `text_delta` | `text` | A chunk of the assistant's reply text |
| `turn_started` | `conversationId` | A new model call just started — show a "thinking" indicator until text or a tool event arrives |
| `tool_started` | `conversationId`, `toolUseId`, `toolName`, `displayName`, `effect` | A tool is about to run (auto-executed read tool, or a freshly-approved gated one) — `displayName` is the UI-friendly label, e.g. "Fetching your session"; `effect` is `Neutral`/`Creative`/`Destructive` |
| `tool_completed` | `conversationId`, `toolUseId`, `toolName`, `displayName`, `effect`, `success`, `affectedCount`, `resources` | The tool finished — `affectedCount` is tool-defined (e.g. sessions fetched, rows created/deleted). `resources` (nullable) lists what it created, changed or removed — see [Tool resources](#tool-resources) |
| `permission_required` | `conversationId`, `toolExecutionRequestId`, `toolName`, `displayName`, `effect`, `inputJson`, `preview` | The AI wants to run a gated tool — needs a `resolve` call. `preview` (nullable) is a readable rendering of what approving would do, e.g. the drafted session for `save_session` |
| `permission_resolved` | `conversationId`, `toolExecutionRequestId`, `approved` | Confirms a resolve was recorded (sibling gated calls from the same turn still pending) |
| `question_asked` | `conversationId`, `toolExecutionRequestId`, `question`, `options` | The AI is asking a clarifying question — needs an `answer` call. `options` are advisory quick-reply suggestions only; always also offer free-text entry |
| `questions_asked` | `conversationId`, `toolExecutionRequestId`, `questions` | A sub-agent is asking several questions at once - needs an `answer` call with one entry in `answers` per question, in order (`""` for a hidden one). Each question is `{ text, kind, options, id, showIf, fieldType }`; `kind` is `Detail`, `Fork` or `Field`. `showIf` (`{ questionId, option }`, nullable) hides the question unless that earlier question was answered with that option; `fieldType` (`date`, `course` or `effortMap`) says which input a `Field` question needs. `options` are quick-reply suggestions only (a number or typed text both work); always offer free-text entry |
| `sub_agent_started` | `parentConversationId`, `toolExecutionRequestId`, `toolUseId`, `childConversationId`, `displayName` | A tool call started a sub-agent in its own conversation. Render everything until the matching `sub_agent_completed` as nested under that tool call (`toolUseId`) in the same chat. Its events carry `childConversationId` as their `conversationId` |
| `sub_agent_completed` | `parentConversationId`, `toolExecutionRequestId`, `childConversationId`, `displayName`, `success` | The sub-agent finished (or was cancelled); the main chat resumes right after - or, for a finished swim-board conversion with a date and course, a `permission_required` for `save_session` follows instead |
| `quota_exceeded` | `conversationId`, `limit`, `used`, `resetsAt` | A `read_swim_board` call was refused because this week's conversions are used up. Show the upgrade prompt. No conversation was started and no model call was made; the assistant is told and will say so in its reply. `resetsAt` is when the count starts again |
| `title_updated` | `conversationId`, `title` | A new chat has been given a generated title - replace the one the list showed. Sent at most once, only for a chat's first message, before `completed` (or last, if the stream ends without it). Not sent if generation failed or the user renamed the chat meanwhile |
| `usage` | `conversationId`, `model`, `inputTokens`, `outputTokens`, `cacheCreationInputTokens`, `cacheReadInputTokens` | Token usage for the turn that just completed |
| `completed` | `conversationId`, `assistantMessageId` | The turn finished normally |
| `error` | `message` | Something went wrong mid-turn |

Consume with `curl -N` (disables buffering) or an `EventSource`/SSE client.

### Tool resources

`resources` on `tool_completed` (and on each tool-message entry in message history, next to `resultJson`) tells the client what a tool touched, so chat can render a tappable card and link to it without parsing the tool's own output. It is `null` when the tool touched nothing the user can open (reads, failed or declined calls).

```json
"resources": [ { "kind": "session", "id": "guid", "action": "created", "label": "Tuesday sprint set" } ]
```

| `kind` | `id` is | Open with |
|---|---|---|
| `session` | session id | `GET /api/sessions/{id}` |

`action` is `created`, `updated` or `deleted`. A `deleted` resource no longer exists, so render it as a non-tappable card. `label` is a short title for the card (absent for `deleted`).

---

## Billing — `/api/billing`

Subscriptions are sold through the stores and managed by RevenueCat; the server only learns about them from RevenueCat's webhook. A user's **plan** is `pro` (an active paid subscription), `trial` (inside the 21-day trial that starts when onboarding completes, see `proTrial` on the profile endpoints) or `free`. Paid beats trial beats free.

Swim-board conversions (`read_swim_board`) are limited per week, Monday 00:00 UTC to the next Monday: **2** on `free`, and a fair-use ceiling (50) on `trial` and `pro`. Everything else, including viewing history and metrics, is unaffected by plan. A conversion that fails without producing a draft does not count.

### `GET /api/billing/entitlement`
```json
{
  "plan": "free",
  "trialEndsAt": "2026-10-20T03:00:00Z",
  "subscription": { "productId": "pro_monthly", "store": "APP_STORE", "expiresAt": "2026-11-20T03:00:00Z", "willRenew": true },
  "conversions": { "limit": 2, "used": 1, "resetsAt": "2026-10-12T00:00:00Z" }
}
```
The response carries a strong `ETag` (a hash of the body) and `Cache-Control: private, no-cache`; send it back as `If-None-Match` and an unchanged entitlement answers `304 Not Modified` with no body. The body has no per-request values (`resetsAt` is the next Monday 00:00 UTC), so the tag changes only when the plan, subscription or quota really changes. The server still does the lookup, so this saves bytes, not requests; to save requests, do not refetch on foreground within a minute or so of the last fetch.

`trialEndsAt` is null until onboarding completes. `subscription` is present only while the paid subscription grants access (with `willRenew: false` after a cancellation, until the paid period ends), otherwise null. `conversions.limit` is this week's limit for the plan, `used` what has been counted, `resetsAt` when it starts again.

### `POST /api/billing/revenuecat`
RevenueCat's webhook, not called by the app. It needs no bearer token; it is authenticated by the value configured as `RevenueCat:WebhookSecret`, sent as the `Authorization` header (a `Bearer ` prefix is accepted). Wrong or missing secret returns `401`; anything else returns `200`, including events that are ignored, so RevenueCat does not retry them. Events are applied once by id and never over a newer one. The user is identified by `app_user_id`, which must be the user's id (set it with `Purchases.logIn`); anonymous RevenueCat ids are ignored.

---

## Sessions — `/api/sessions`

Saved swims: a record of what was swum, logged after the fact. Today they are created by the AI's `save_session` tool (see Sub-agents above); these endpoints read them, correct them, and add them up. A session's `title` is chosen when it is saved: the one passed to `save_session` if any, else the name the converter wrote for the workout (a few words about its focus or main set, written knowing the swimmer's recent titles so they vary), else one built from what was swum (`Lane 2 - Freestyle and kick - 2,400 m`). It is at most 80 characters and is not editable after saving yet. Distances are always computed from the session's structure, never entered. Every distance (`dist`, `totalDistance`, `distanceFromStart`, metric totals) is a decimal number with at most 2 decimal places, so odd pool lengths like `12.5` work.

A session's `content` is a tree. `sets` holds **items** (`{ kind: "item", reps, timing?, parts, note? }` - `parts` is what one rep is made of, each `{ dist, stroke, mode, equip, effort, intensity? }`) and **groups** (`{ kind: "group", repeat, items }`, nesting at most 3 deep). `stroke` is `free | back | breast | fly | im | choice`; an `im` part counts as a quarter of each stroke. `intensity` is `{ percent, scale, level }`, mapped from `effort`; it is `null` when the effort could not be mapped. `percent` (1-100) is the effort as a percent of maximum and is what is stored and filtered on; `scale` is where the text came from (`words` for "easy"/"sub max", `zones` for `A0`-`A3`/`TH`/`VO2`, `percent` for "80%"); `level` is the same effort on the 1-10 tens scale, written out for clients that still read it. The `effort` text is the coach's own wording and is kept as written; the percent is worked out from it, so it can be re-mapped later. See [Effort scales](#effort-scales).

In API responses enum values are PascalCase (`Free`, `Kick`, `SendOff`; the lowercase forms above are what the AI tools read and write) and missing values are `null` rather than left out (`timing`, `note`, `effort`, `intensity`). Decode enums case-insensitively to be safe.

### `GET /api/sessions?from={date}&to={date}&limit={int}&offset={int}&include=content`
Newest first. All parameters optional (`limit` 1-100, default 50).

**Response:** array of `{ id, performedOn, title, course, unit, source, totalDistance, createdAt }`. `course` is `Scy | Scm | Lcm`, `unit` is `Yards | Metres` (yards for `Scy`), `source` is `Ai | Logged`.

`include=content` adds each session's `content` (the tree, exactly as `GET /api/sessions/{id}` returns it) to every item, so a page of full sessions is one call instead of one per session. Items then look like `{ id, performedOn, title, course, unit, source, totalDistance, createdAt, content }`; there is no `outline` in the list. With `include=content`, `limit` is capped at 20 (a larger value is treated as 20). Any other `include` value is a `400`.

### `GET /api/sessions/{id}`
**Response:** the summary fields above (without `createdAt`) plus `content` (the tree) and `outline` (numbered text). `404` if it is not the caller's.

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
| `sessionId` | Only that saved session (including its linked timed swims). Combine with `groupBy=stroke`, `mode` or `intensity` for one session's breakdown; an unknown id returns empty rows |
| `from`, `to` | Date range (yyyy-MM-dd), inclusive |
| `strokes` | `free | back | breast | fly | im | choice` (an IM part counts towards fly, back, breast and free) |
| `modes` | `swim | kick | pull | drill | scull` |
| `minPercent`, `maxPercent` | Bounds on intensity, 1-100. Distance with no stated effort counts at the swimmer's default effort (see `applyDefault`); with `applyDefault=false` setting either leaves it out |
| `minIntensity`, `maxIntensity` | The earlier 1-10 bounds, kept for existing clients: a level covers the percents of its tens band (`minIntensity=8` is 75 and above). `minPercent`/`maxPercent` win when both are sent |
| `scale` | `tens`, `zones` or `custom`: how `groupBy=intensity` buckets intensity. Omit it to use the swimmer's saved scale (`tens` until they choose one) |
| `groupBy` | Up to 2 of `stroke | mode | intensity | day | week | month | session` (weeks start Monday and are named by that date) |
| `thenBy` | A second dimension, for a request with exactly one `groupBy`: `groupBy=day&thenBy=intensity` is the same as `groupBy=day&groupBy=intensity`. `400` with any other number of `groupBy` values |
| `applyDefault` | `true` (default): distance whose effort could not be mapped is counted at the swimmer's `defaultEffort`, so it falls in that effort's band, in `avgIntensity` and in `minPercent`/`maxPercent` ranges. `false`: it stays `unspecified` and is left out of intensity ranges and averages |
| `measure` | `distance` (default), `sessions`, or `avgIntensity` (average percent, weighted by distance) |

Examples: `?strokes=fly&groupBy=month` (how much fly per month), `?minPercent=80&groupBy=week` (distance at 80%+ effort per week), `?groupBy=intensity&scale=zones` (distance per zone), `?from=2026-10-12&to=2026-10-18&groupBy=day&thenBy=intensity&scale=zones` (every day's effort bands in one call; a day's total is the sum of its rows), `?measure=avgIntensity&groupBy=month`, `?sessionId={id}&groupBy=intensity&scale=zones` (one session's effort split).

**Response:**
```json
{ "measure": "Distance", "groupBy": ["Stroke"], "totalDistance": 1600, "units": ["Metres"],
  "scale": { "id": "zones", "name": "Zones", "bands": [ { "key": "A0", "label": "A0", "min": 1, "max": 55 }, ... ] },
  "defaultEffort": 50, "assumedDistance": 100,
  "rows": [ { "keys": ["fly"], "value": 600 }, { "keys": ["free"], "value": 500 } ] }
```
`rows` reads in date order when grouped by time, and in band order (with `unspecified` last) when grouped by intensity - the `keys` are the band keys of the chosen scale, e.g. `"8"` on `tens` or `"Sub Max"` on `zones`. Otherwise rows are biggest first. `scale` is the effort scale the intensity keys belong to (the one named by `scale`, else the swimmer's saved one), so a response can be rendered without a second call. `defaultEffort` is the percent unspecified distance was counted at (`null` with `applyDefault=false`) and `assumedDistance` is how much distance that was, for flagging it in the UI. `units` lists every distance unit present; more than one means metres and yards are mixed - nothing is converted. `value` is `null` when the measure does not apply (an average with no intensity). On a distance row, `assumed` is how much of that row's `value` had no stated effort and was counted at `defaultEffort`; it is left out when there was none, and its sum over all rows is `assumedDistance` (so a day's assumed distance is the sum over that day's rows). Example row: `{ "keys": ["2026-10-19", "A0"], "value": 1200, "assumed": 300 }`. `400` with `{ error }` for a question that cannot be answered (e.g. more than 2 `groupBy`, an unknown `scale`, or `scale=custom` with no custom scale saved).

### Effort scales
Intensity is always stored as a percent of maximum effort. An **effort scale** is a named set of ordered bands that together cover 1-100; it only decides how intensity is bucketed when grouped. Built in:
- `tens` - ten bands, keys `"1"` to `"10"` (a level is the percent rounded to the nearest ten). The default, and what clients read before percentages existed.
- `zones` - `A0` (1-55), `A1` (56-65), `A2` (66-72), `A3` (73-79), `TH` (80-85), `VO2` (86-89), `Sub Max` (90-97), `Max` (98-100).
- `custom` - the swimmer's own bands (see preferences).

How effort text becomes a percent is a fixed vocabulary shared with the conversion model: words (`easy` 20, `build` 50, `hard`/`fast` 80, `threshold` 83, `race pace` 88, `sub max` 90, `max`/`sprint` 100 ...), zone names (`A0` 40, `A1` 60, `A2` 70, `A3` 76, `TH` 83, `VO2` 88) and percentages (`80%` is 80). Text that is ambiguous ("easy/hard") or not in the vocabulary has no intensity.

### Custom metrics (home page)
Saved questions about training, answered by the same engine as `GET /api/sessions/metrics`. A swimmer keeps up to 12. Intensity bounds are percents of maximum effort, so a metric keeps its meaning if the swimmer changes their effort scale: for "TH and above" send the zone's lower edge (80 in the built-in `zones` scale) as `minPercent`. Distance with no stated effort counts at the swimmer's default effort, so an unstated set is left out of a high threshold. An IM part counts towards fly, back, breast and free.

**Definition:** `{ "name": "Fly at TH+ this week", "strokes": ["fly"], "modes": [], "minPercent": 80, "maxPercent": null, "measure": "distance" | "sessions" | "avgIntensity", "window": "last7Days" | "last30Days" | "thisWeek" | "thisMonth" | "allTime", "groupBy": "day" | "week" | "month" | null }`. `name` is required (1-60 characters); empty `strokes` or `modes` mean all.

- `GET /api/metrics/custom?today=yyyy-MM-dd` - every metric with its current value, in the order added. `today` is the swimmer's own date, so "this week" (Monday start) follows their time zone. **Response:** `[ { "id", "name", "measure", "window", "value", "series": [ { "keys": ["2026-10-12"], "value": 200 } ] | null, "units": ["Yards"], "assumedDistance": 0 } ]`. `series` is present only when the definition has a `groupBy`; periods with nothing swum are absent, so zero-fill on the client. `value` is `0` for a total over nothing and `null` for an average over nothing. Distances in metres and yards are never converted; more than one entry in `units` means they are mixed.
- `GET /api/metrics/custom/{id}?today=YYYY-MM-DD[&window=...][&groupBy=...][&thenBy=intensity[&scale=tens|zones|custom]][&include=previous]` - one metric over a whole period, for stepping back and forward in time. `window` and `groupBy` override the saved ones for this request only; nothing is written. **Response:** an item of the list above plus `"range": { "from": "2026-10-12", "to": "2026-10-18" }`, the inclusive dates the window covered, and `window` echoing the one used. `thisWeek` is Monday to Sunday and `thisMonth` the calendar month, so `to` can be after `today`; `last7Days` and `last30Days` end on `today`; `allTime` runs from the first session (or `today` if there is none) to `today`, and counts only what was swum by then. Step back by sending an earlier `today`. `series` is `null` unless the saved definition or `groupBy` gives a grouping (`day`, `week` or `month`), and each bar's `avgIntensity` is that bar's own distance-weighted average. `thenBy=intensity` splits every bar into effort bands: each `series` row then has two keys, `[period, bandKey]`, in period then scale order, and the response adds `scale` (`{ id, name, bands: [ { key, label, min, max } ] }`, the whole scale, not just the bands present) and `defaultEffort` (the percent distance with no stated effort was counted at, which is also what `assumedDistance` measures). `scale` picks the bands, else the swimmer's saved scale. The metric's own strokes, modes and intensity bounds apply to every row. `thenBy` is ignored (one key, no `scale`) when the metric has no series or its measure is not `distance`. The response also carries `firstSessionDate` (`yyyy-MM-dd`, or `null` with no sessions): the date of the swimmer's earliest session whatever the period, for limiting how far back to step. `include=previous` adds `previous`: `{ "range", "value", "series", "units", "assumedDistance" }` for the period before (the anchor moves back 7 days for `thisWeek` and `last7Days`, 30 for `last30Days`, one calendar month for `thisMonth`), banded the same way and sharing the top-level `scale` and `defaultEffort`. `previous` is `null` without `include` and for `allTime`; any other `include` value is a `400`. `404` for a metric that is not the caller's. `400` with `{ error }` for a missing or unreadable `today` (before 2000, or more than a day ahead of the server's UTC date), an unknown `window` or `groupBy`, a `thenBy` other than `intensity`, an unknown `scale` (or `custom` with none saved), or a series that would have more than 366 bars (e.g. `allTime` by `day` over more than a year).
- `GET /api/metrics/custom/definitions` - `[ { "id", "position", "definition": { ... } } ]`, for editing.
- `POST /api/metrics/custom` - body: a definition. **Response:** `{ "id", "position", "definition" }`.
- `PUT /api/metrics/custom/{id}` - body: a definition. `404` for a metric that is not the caller's.
- `DELETE /api/metrics/custom/{id}` - `204`, or `404`.

`GET /api/metrics/custom` also returns `400` for a missing or unreadable `today`. `400` with `{ error }` on create and edit is for a missing name, the 12-metric limit, a `groupBy` other than day, week or month, an intensity bound outside 1-100, or a lowest intensity above the highest. The chat can also add one through the gated `add_custom_metric` tool.

### `GET /api/sessions/effort-scales`
**Response:** `{ "scales": [ { "id": "tens", "name": "1-10", "bands": [ { "key": "1", "label": "1", "min": 1, "max": 14 }, ... ] }, { "id": "zones", ... } ], "custom": { "id": "custom", "name": "Custom", "bands": [...] } | null, "selected": "tens", "defaultEffort": 50 }`. `selected` is the swimmer's saved scale.

### `GET /api/sessions/effort-scale`
The scale metrics use when a request names none, already resolved, plus the default effort. **Response:** `{ "selected": "zones", "scale": { "id": "zones", "name": "Zones", "bands": [ ... ] }, "custom": { "id": "custom", "name": "Custom", "bands": [...] } | null, "defaultEffort": 50 }`. `scale` is what a metrics response without a `scale` parameter reports; `custom` is the swimmer's own scale whether or not it is selected.

### `PUT /api/sessions/effort-scale`
**Body:** `{ "scale": "tens" | "zones" | "custom", "customBands"?: [ { "key", "label"?, "min", "max" } ] }`. `scale` is required; `customBands` saves the swimmer's own bands (the same rules as `customEffortScale` below) and is required the first time `scale` is `custom`. **Response:** as `GET`. `400` with `{ error }` for a missing or unknown `scale`, `custom` with no bands saved, or bands that are not a usable scale. `defaultEffort` is changed through `PUT /api/sessions/preferences`.

### `GET /api/sessions/preferences`
**Response:** `{ "defaultEffort": 50, "effortScale": "tens", "customEffortScale": [ { "key": "easy", "label": "Easy", "min": 1, "max": 60 }, ... ] | null, "displayUnit": "Metres" }`. `defaultEffort` is the swimmer's default effort as a percent (1-100); 50 until they choose one. Metrics count distance with no stated effort at this percent (see `applyDefault` on `GET /api/sessions/metrics`). `effortScale` is the scale metrics group intensity by when a request names none. `displayUnit` (`"Metres"` or `"Yards"`, `"Metres"` until chosen) is the unit the app shows distances in. It is a display preference only: it does not change the unit a session is stored or logged in, is not applied to new sessions, and nothing is converted by the API - the client converts for display.

### `PUT /api/sessions/preferences`
**Body:** any of `{ "defaultEffort": 60, "effortScale": "zones" | "tens" | "custom", "customEffortScale": [ { "key", "label"?, "min", "max" } ], "displayUnit": "Metres" | "Yards" }`. Only the fields sent change; `customEffortScale: []` removes the saved custom scale (and falls back to `tens` if it was selected). A custom scale needs 2-12 bands with unique keys (1-16 characters, not `unspecified`), listed in order, each starting one above the previous band's `max`, from 1 to 100. **Response:** the saved preferences. `400` with `{ error }` for a `defaultEffort` outside 1-100, an unknown `effortScale` or `displayUnit`, `effortScale: "custom"` with no custom scale, or bands that are not a usable scale.

---

## Timed swims — `/api/timed-swims`

How a swim went, as opposed to how much was swum: a time for a distance and stroke (the "event" is just `dist` + `stroke`, e.g. 50 free). Logged separately from sessions, usually during a practice (`sessionId` set) and sometimes standalone, such as a race result. Timed swims are never edited or deleted through the API.

Enums are accepted in any case on input and returned PascalCase (`Free`, `Scm`, `Push`).

### `POST /api/timed-swims`
**Body:**
```json
{
  "sessionId": "guid|null",
  "performedOn": "yyyy-MM-dd",
  "course": "Scy | Scm | Lcm",
  "dist": 50,
  "stroke": "free | back | breast | fly | im | choice",
  "mode": "swim | kick | pull | drill | scull",
  "seconds": 27.84,
  "effort": "string|null",
  "startType": "Push | Dive | null",
  "finish": "Hand | Head | Feet | null",
  "resistance": { "type": "string", "load": "string|null" },
  "splits": [
    { "distanceFromStart": 25, "seconds": 13.1, "strokeRateCyclesPerMinute": 52 }
  ],
  "includeInMetrics": false
}
```
`sessionId`, `effort`, `startType`, `finish`, `resistance`, `splits` and `includeInMetrics` are optional.

- `seconds` is the whole swim's time and is always required; splits are extra detail on top of it.
- `effort` is free text ("easy", "80%", "race pace") and is mapped to the same percent `intensity` as a session part. It is `null` when the text can't be mapped.
- A split is a checkpoint before the finish, so `distanceFromStart` must be below `dist`. The finish time is `seconds`, not a split.
- `strokeRateCyclesPerMinute` counts full stroke cycles, not individual arm strokes.
- `includeInMetrics` defaults to `false`; see below.

**Validation (400):** `dist >= 1` with at most 2 decimal places, `seconds > 0`, a `resistance` must have a `type`, and splits must be strictly increasing in both distance and seconds, finish before `dist` and `seconds`, with a rate above 0.

**Response:** `TimedSwimResponse`. `400` with `{ error, problems }` listing everything to fix; nothing is saved.

### `GET /api/timed-swims`
Fastest first. All parameters optional; repeat a parameter for several values (`strokes=free&strokes=fly`).

| Parameter | Meaning |
|---|---|
| `sessionId` | Only swims logged against this session |
| `from`, `to` | Date range (yyyy-MM-dd), inclusive |
| `distances` | Exact distances, e.g. `distances=50&distances=12.5` |
| `strokes` | `free | back | breast | fly | im | choice` |
| `minPercent`, `maxPercent` | Bounds on intensity, 1-100. Swims with no intensity are left out when either is set |
| `minIntensity`, `maxIntensity` | The earlier 1-10 bounds (a level covers the percents of its tens band); `minPercent`/`maxPercent` win when both are sent |
| `startTypes` | `Push | Dive` |
| `resistanceTypes` | Resistance type names to match |
| `hasResistance` | `true` for swims with resistance, `false` for without |

Examples: `?strokes=free&distances=50` (50 free times, best first), `?hasResistance=false&startTypes=Dive` (unresisted dive starts).

The list is not paginated.

**Response:** array of `TimedSwimResponse`.

### `GET /api/timed-swims/{id}`
**Response:** `TimedSwimResponse`. `404` if it is not the caller's.

### `TimedSwimResponse` shape
```json
{
  "id": "guid",
  "sessionId": "guid|null",
  "performedOn": "yyyy-MM-dd",
  "course": "Scy | Scm | Lcm",
  "unit": "Yards | Metres",
  "dist": 50,
  "stroke": "Free",
  "mode": "Swim",
  "seconds": 27.84,
  "effort": "string|null",
  "intensity": 90,
  "startType": "Push | Dive | null",
  "finish": "Hand | Head | Feet | null",
  "resistance": { "type": "string", "load": "string|null" },
  "splits": [{ "distanceFromStart": 25, "seconds": 13.1, "strokeRateCyclesPerMinute": 52 }],
  "strokeRateCyclesPerMinute": 52.0,
  "includeInMetrics": false,
  "createdAt": "ISO-8601 timestamp"
}
```
`unit` is derived from `course` (yards for `Scy`). `resistance` is `null` when none was set. The top-level `strokeRateCyclesPerMinute` is the average of the splits' rates, to one decimal place, and `null` when no split has one.

### Interaction with session metrics
A timed swim counts towards `GET /api/sessions/metrics` as training volume (its `dist`, `stroke`, `mode` and `intensity`, on the swim's own `performedOn` date, grouped under its session) only when **both** `includeInMetrics` is `true` and `sessionId` is set. A standalone swim never counts, whatever the flag, because there is no session for it to be volume of. Because the swim is added to the metrics at query time, editing the linked session does not remove it.

---

## Profile & onboarding — `/api/profile`

Onboarding progress is tracked server-side so a user who closes the app can resume. On launch call `GET /api/profile/onboarding`: if `status` is `Completed`, go straight into the app, otherwise resume at `resumeStep`. A profile is created on first call (existing users before launch are already `Completed`).

Steps, in order: `Questionnaire` → `FirstSession` → `ReviewWorkout` → `ProTrial` → `Completed`. Only `ProTrial` can be skipped. Splash and OTP sign-in are not steps.

### `GET /api/profile`
**Response:** `{ "onboardingCompleted": bool, "answers": { "level": "Regional", "goal": ["DataInsights", "GeneralFitness"] }, "remindersEnabled": bool, "reminderLeadMinutes": 60, "proTrial": { "endsAt": "datetime", "daysRemaining": 21 } | null }`

### `GET /api/profile/onboarding`
**Response:**
```json
{
  "status": "InProgress | Completed",
  "currentStep": "FirstSession",
  "resumeStep": "FirstSession",
  "completedSteps": ["Questionnaire"],
  "firstSessionPath": "Upload | Sample | null",
  "proTrial": { "endsAt": "datetime", "daysRemaining": 21 }
}
```
`proTrial` is null until onboarding completes; the 21-day Pro trial starts then.

### `GET /api/profile/onboarding/questionnaire`
The questions and their options, plus the user's saved answers - render the questionnaire from this so new questions need no app change.

**Response:** `{ "questions": [{ "key": "level", "prompt": "string", "multiple": false, "options": [{ "value": "Regional", "label": "Regional", "exclusive": false }], "answer": "string | null", "answers": ["string"] }], "complete": bool }`

`multiple` says whether several options can be chosen (checkboxes rather than radio buttons). `exclusive` on an option means choosing it excludes the others: clear the other selections when it is picked. For a single-answer question read `answer` (null until answered); for a multiple-answer question `answer` is always null and `answers` lists every chosen value (for single-answer questions `answers` holds that one value too).

Current questions: `level` (one answer: `Regional`, `StateQualified`, `NationalQualified`, `OlympicTrialQualified`) and `goal` (any number of: `CompetitiveImprovement`, `DataInsights`, `GeneralFitness`, `SocialCommunityAndGamification`; or `Other` on its own, which is exclusive).

### `PUT /api/profile/onboarding/questionnaire`
**Body:** `{ "answers": { "level": "Regional", "goal": ["DataInsights", "GeneralFitness"] } }`

Each answer is the chosen value, or an array of values; a plain string is a one-item selection, so `"goal": "DataInsights"` still works. Saving a question replaces everything previously saved for it (send the whole selection, not a delta), and a question left out of the body keeps its answers. Partial saves are fine; once every question has at least one answer the user advances to `FirstSession`. `400` for an unknown question or value, an empty selection, several answers to a single-answer question, or an exclusive option (`Other`) sent together with others. **Response:** same shape as `GET /api/profile/onboarding`.

### `PUT /api/profile/onboarding/first-session`
Records which branch the user picked for the first session: `{ "path": "Upload" | "Sample" }`. Returns `409` before the questionnaire is done. **Response:** onboarding status.

### `POST /api/profile/onboarding/steps/{step}/complete`
Marks the current step done and advances. Completing a step the user is already past is a no-op; completing one ahead of the current step returns `409`. `Questionnaire` needs all answers and `FirstSession` needs a chosen path, otherwise `400`. Completing `ProTrial` finishes onboarding. **Response:** onboarding status.

### `POST /api/profile/onboarding/steps/{step}/skip`
Only valid for `ProTrial` (`400` otherwise). **Response:** onboarding status.

### `PUT /api/profile/notification-preferences`
**Body:** `{ "remindersEnabled": true, "reminderLeadMinutes": 60 }` - lead time must be 5-1440. **Response:** profile (as `GET /api/profile`).

---

## Account — `/api/account`

Deleting an account is two calls, so a stray request can never delete anything. Deletion is permanent: it removes the user's sessions, chats and uploaded images, profile, subscription record, analytics events and their sign-in. It does **not** cancel an App Store or Google Play subscription - tell the user to cancel it in their store settings.

### `POST /api/account/deletion`
Starts a deletion; nothing is deleted yet. The user must have signed in (phone OTP) within the last 5 minutes.
**Response:** `{ "confirmationToken": "...", "expiresAt": "..." }` - the token is valid for 10 minutes and only for this user.
- `403 { "error": "reauthentication_required" }` - the sign-in is too old. Run phone OTP again, then retry.

### `POST /api/account/deletion/confirm`
**Body:** `{ "confirmationToken": "..." }`. Deletes the account.
- `204 No Content` - deleted. The app must now discard its session and call `Purchases.logOut()`; the old access token is refused (`401`) from here on.
- `400 Bad Request` - the token is invalid, expired or was issued to someone else. Start again from the first call.
- A `5xx` means it stopped part way. Nothing is lost by retrying with the same token (until it expires) - every step is safe to repeat.

---

## Not planned: launch bootstrap

A single `GET /api/bootstrap` (onboarding, custom metrics, conversations, entitlement, effort scale) was assessed and held back. It would save about 4 requests but couples five features' response shapes and release cadence to one endpoint. Revisit after profiling the current launch; first confirm the host serves HTTP/2 and consider enabling response compression for month views.

---

## Notes

- `DayOfWeek`, `SessionType`, `EffortZone` are enums — check the actual enum definitions in `src/Features/*/Domain/Models/` for their exact member names before relying on the examples above.
- A 500 response generally means an app-layer exception (e.g. not-found/access-denied cases) is leaking uncaught rather than being mapped to a proper status code — this is a known gap, not necessarily a client-side mistake.
