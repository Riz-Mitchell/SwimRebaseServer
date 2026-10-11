# Custom effort terms: frontend handoff

Branch: `feature/custom-effort-terms` (backend done, not yet reviewed or merged). Applies to the swim-board import flow (`read_swim_board`). Background: `docs/Swim-Board-Frontend-Changes.md`.

## What this is

A club's boards often use its own effort wording ("Wolf pace", "Zone 4"). The server cannot score what it does not understand, so that distance was counted at the swimmer's default effort. Now, when a board uses wording the server does not know, the questionnaire includes one extra question per wording asking how hard it is. The answer is saved for that user, so the same wording is understood in every later session and is never asked about again.

**No endpoints added or removed, no new events.** The only change is a new kind of question inside the existing `questions_asked` event.

## The new question

Same shape as every other question. It is a `Field` question with `fieldType: "effortMap"`.

```json
{ "id": "effort:Wolf pace",
  "text": "How hard is \"Wolf pace\"? Pick what it matches, or give a percent.",
  "kind": "Field",
  "options": ["very easy", "easy", "smooth", "steady", "a0", "moderate", "a1", "strong", "a2", "a3", "hard", "threshold", "th", "race pace", "vo2 max", "sub max", "max"],
  "showIf": null,
  "fieldType": "effortMap" }
```

- The wording the question is about is the `id` after `effort:`. Show it prominently; `text` is a fine fallback.
- `options` are effort words and zones, easiest first (the list above is indicative and may change). They are lower case: show zones as `A2`, `TH` and words as sentence case if you like, but see the answer rules below.
- At most **4** of these per questionnaire. They come **after** the date and pool questions, in the same list.
- They are never hidden (`showIf` is always `null`).
- Most boards will not have any. Do not assume the question exists.

## UI to build

One control with two ways to answer:

1. **"Same as..." chips** from `options`, single select. This is for "our Wolf pace is the same as A2".
2. **A 1-100 slider** (with a number readout) for "it's about 85%".

Picking a chip clears the slider and the other way round. The question may be left unanswered (skipping is allowed).

## Answering

The answer format is unchanged: one string per question in `answers`, in the order received.

| User did | Send |
|---|---|
| Picked a chip | The option's text exactly as given, e.g. `"a2"` (case does not matter) |
| Used the slider | The number **with a percent sign**, e.g. `"85"` is wrong, send `"85%"` |
| Left it alone | `""` |

**The percent sign matters.** An answer that is a bare number is read as an option number ("2" means the second option), as for every other question. Always send `"<n>%"` for the slider.

Do not send the option's number for the chips; send its text.

## What the user sees afterwards

Nothing extra. The draft and the save card already reflect the mapping: the part's intensity comes back as `{ "percent": 85, "scale": "personal", "level": 9 }`. `scale: "personal"` means it came from the user's own term. If you display the intensity scale or source anywhere, treat `personal` as a valid value alongside `words`, `zones` and `percent`.

## Behaviours to handle

- **Older clients are safe.** A client that does not know `effortMap` shows it as a plain `Field` question with free text. Typing "A2" or "85%" still works.
- **Skipped means asked again.** If the user skips, nothing is saved and the same wording is asked again on the next board that uses it.
- **Changing a meaning later is not supported yet.** Re-answering only happens if the wording is asked again, which it will not be once saved. There is no screen to view or edit saved terms yet (see open questions).
- **Existing sessions are not changed.** Only sessions created after the term is saved use it.
- **Stale questionnaires.** A questionnaire already open from before this deploy has no effort questions, which is fine.

## Open questions for product / design

1. **A settings screen** listing the user's saved terms (wording and percent) with edit and delete. The backend table and repository exist; the endpoints do not. Say if you want them and I will add `GET/PUT/DELETE /api/sessions/effort-terms`.
2. **Percent on each chip.** Options are currently plain text, so the slider cannot jump to a chip's percent. If you want the slider to follow the chip, the server can return the percent alongside each option; that would be an additive change.
3. **Scale display.** Should a saved term appear on the user's custom effort scale (the bands in Settings)? The server stores a percent only, so it falls into whichever band covers it.

## Testing it

1. Attach a board whose effort column uses a made-up term, e.g. `4 x 100 @ Wolf pace`.
2. Expect the `questions_asked` event to end with an `effortMap` question for `Wolf pace`.
3. Answer it with `"85%"`, finish and save. The saved session's part should have `intensity.percent` 85 and `scale` `personal`.
4. Attach another board using `Wolf pace`. It should not be asked again, and should already be scored at 85.

## Checklist

- [ ] Handle `fieldType: "effortMap"` on a `Field` question; ignore unknown field types gracefully.
- [ ] Chips (single select) plus a 1-100 slider; selecting one clears the other.
- [ ] Send the option text, `"<n>%"` for the slider, or `""` to skip.
- [ ] Parse the wording from the `id` (after `effort:`) for the label.
- [ ] Accept `scale: "personal"` on intensity.
- [ ] Decide on the open questions above.
