# SwimRebase REST API Reference

_Last updated: 14/09/2026_

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

---

### `POST /api/ai/tool-requests/{toolExecutionRequestId}/resolve`
Approve or deny a gated tool call (a write/destructive action the AI wants to perform, e.g. creating a focus or timetable slot).

**Path params:** `toolExecutionRequestId` (guid)

**Body:**
```json
{ "approve": true, "denialReason": null }
```
`denialReason` is optional, used only when `approve` is `false`.

**Response:** `text/event-stream` (SSE).

---

### `GET /api/ai/conversations?sortBy={LastMessageAt|CreatedAt}&descending={bool}`
List the current user's conversations. Both query params optional - default is `sortBy=LastMessageAt&descending=true` (most recently active first, which is also the shape of the existing `(UserId, LastMessageAt)` index).

`title` is auto-generated from the conversation's first message (truncated to ~60 chars at a word boundary) when it's created.

**Response:**
```json
[
  { "id": "guid", "title": "string|null", "createdAt": "datetime", "lastMessageAt": "datetime" }
]
```

---

### `GET /api/ai/conversations/{conversationId}/messages?before={int}&limit={int}`
Keyset-paginated message history for a conversation, newest-first. `before` (optional) is the `sequenceNumber` of the oldest message already loaded - omit for the first page, then pass `nextBefore` from the previous response to fetch older messages. `limit` optional, default 50, clamped to 1-100.

**Path params:** `conversationId` (guid)

**Response:**
```json
{
  "items": [
    { "id": "guid", "sequenceNumber": 12, "role": "User|Assistant|Tool", "content": "string", "createdAt": "datetime" }
  ],
  "hasMore": true,
  "nextBefore": 8
}
```
`nextBefore` is `null` when `hasMore` is `false`.

---

### `GET /api/ai/conversations/{conversationId}/tool-requests/pending`
List still-unresolved (`PendingApproval`) gated tool requests for a conversation, oldest first. Use this to recover a conversation's state after reconnecting (e.g. after a dropped SSE connection or a server crash mid-turn) instead of relying solely on the `permission_required` SSE event, which is only ever emitted once and is lost if the client wasn't connected to receive it.

**Path params:** `conversationId` (guid)

**Response:**
```json
[
  { "id": "guid", "assistantMessageId": "guid", "toolName": "string", "inputJson": "string", "createdAt": "datetime" }
]
```
Resolve each one via `POST /api/ai/tool-requests/{toolExecutionRequestId}/resolve` above. An empty array means the conversation has nothing outstanding - if the frontend appears "stuck" waiting for approval, calling this endpoint is the way to tell whether there's really a request to resolve or whether the client's local state is just out of sync.

---

### `GET /api/ai/usage?from={datetime}&to={datetime}`
Token usage summary for the current user. Both query params optional.

**Response:** usage summary object (shape defined by `ITokenUsageRepository.GetSummaryForUserAsync`).

---

### Streaming response shape

The two message-sending endpoints and the tool-resolve endpoint all stream Server-Sent Events. Each event is JSON with a `type` discriminator:

| `type` | Fields | Meaning |
|---|---|---|
| `text_delta` | `text` | A chunk of the assistant's reply text |
| `turn_started` | `conversationId` | A new model call just started — show a "thinking" indicator until text or a tool event arrives |
| `tool_started` | `conversationId`, `toolUseId`, `toolName`, `displayName` | A tool is about to run (auto-executed read tool, or a freshly-approved gated one) — `displayName` is the UI-friendly label, e.g. "Checking your timetable" |
| `tool_completed` | `conversationId`, `toolUseId`, `toolName`, `displayName`, `success` | The tool finished |
| `permission_required` | `conversationId`, `toolExecutionRequestId`, `toolName`, `displayName`, `inputJson` | The AI wants to run a gated tool — needs a `resolve` call |
| `permission_resolved` | `conversationId`, `toolExecutionRequestId`, `approved` | Confirms a resolve was recorded (sibling gated calls from the same turn still pending) |
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
