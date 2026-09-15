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
{ "message": "string" }
```

**Response:** `text/event-stream` (SSE) — see [Streaming response shape](#streaming-response-shape) below.

---

### `POST /api/ai/conversations/{conversationId}/messages`
Continue an existing conversation.

**Path params:** `conversationId` (guid)

**Body:**
```json
{ "message": "string" }
```

**Response:** `text/event-stream` (SSE).

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
| `permission_required` | `conversationId`, `toolExecutionRequestId`, `toolName`, `inputJson` | The AI wants to run a gated tool — needs a `resolve` call |
| `permission_resolved` | `conversationId`, `toolExecutionRequestId`, `approved` | Confirms a resolve was recorded (sibling gated calls from the same turn still pending) |
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
