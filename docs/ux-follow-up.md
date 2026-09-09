# UX follow-up

A brief for the next session on StartCompass. It came out of a structured UX review
(Laws of UX, Gestalt, Nielsen's heuristics, PACT) carried out on the running application on
**9 September 2026**, measured in the browser rather than judged from screenshots.

Paste this file — or the section you want — as the opening message next time. It is written
to stand on its own: nothing below depends on the conversation it came from.

---

## What has already been done — do not redo it

Four findings from that review were fixed the same day. Check them before touching anything
near them.

| Fixed | Where |
| --- | --- |
| **Unsaved answers are no longer lost.** A local draft is written 400 ms after the last keystroke, restored on return with a notice, and dropped on submit. A `beforeunload` guard covers private windows where storage is off. | `wwwroot/js/survey.js` (`initDraft`), `Views/Survey/Fill.cshtml`, `SurveyFormViewModel.DraftKey` / `.LastSavedAt`, `SurveyController.DraftKey` |
| **Amber score band met WCAG AA.** `--sc-warn` `#a06c14` → `#92620f`: it was 4.09:1 on `--sc-mid-wash`, and it colours the averages a coach reads, not just a label. | `wwwroot/css/startcompass.css` |
| **The progress count is announced.** A separate visually-hidden `role="status"` region, written 1.2 s after the last answer so it does not talk over the radio a screen reader has just read. | `Fill.cshtml`, `survey.js` (`initProgress`) |
| **Skip link** (WCAG 2.4.1, level A), first in the tab order. | `Views/Shared/_Layout.cshtml`, `startcompass.css` |

Two corrections to the review itself, so they are not chased again:

- **Panel headings were not a defect.** The review claimed the form has no heading structure
  for screen readers. Each `tabpanel` already carries `aria-labelledby` pointing at its tab,
  and hiding the `<h2>` is a documented decision in `survey.js`. Nothing to fix.
- **Nav links at 38 px pass WCAG 2.2.** Target Size (Minimum) is 24 px at level AA. 44 px is
  level AAA. Treat this as advisory, not a compliance failure.

---

## Open items

Each one was left deliberately: they are product or design decisions, not defects.

### 1. Norwegian interface — a decision to reopen, not a bug

**`docs/five-c.md` states: "All interface text is English, matching the StartCompass site and
the wireframes."** So this is settled, on purpose. What follows is the argument for revisiting
it, and IK Start owns the answer.

The users are Norwegian players aged roughly 13–19, their guardians, and their coaches. The
instrument asks things like *"I regain my composure quickly after making a mistake"* and
*"I respond constructively when I become frustrated or disappointed."* The distance between
*composure* and *calm*, or between *constructively* and *politely*, is exactly the nuance the
1–5 answer is supposed to capture.

A respondent who guesses at the wording still produces a number, and that number is
indistinguishable from a considered one. The whole system compares those numbers across
player, coach and guardian. Guardians are the most exposed: widest range of English, least
exposure to football-specific English, answering about their own child.

**If it is taken up:**

- There is no localisation infrastructure at all — no `IStringLocalizer`, no `Resources/`, no
  `RequestLocalization`. That is the first decision: resource files, or a second question-set
  file, or both.
- `Data/Questions/five-c-questions.json` is already close. Every question carries three
  wordings (`text`, `textAboutPlayer`, `textForGuardian`); a language axis is a fourth
  dimension on a structure that already has three.
- The 25 statements come from START IK's own questionnaire. **Translating them is a research
  decision, not a development one** — a translated psychometric item is a different item until
  someone qualified says otherwise. Get that in writing before changing any `text` field.
- The chrome (buttons, notices, headings) can be translated independently of the statements,
  and is the lower-risk place to start.
- `lang="en"` in `_Layout.cshtml` has to move with whatever is decided.

### 2. The tab strip is unusable on a phone

**Measured at 375 px: the strip holds 1274 px of tabs in a 347 px window.** Six panels —
five blocks of statements plus "End of period" — become a horizontal scroll that does not
announce itself.

Worse: `survey.js` restores the last-opened tab from `sessionStorage`, so a player coming
back lands mid-strip with a cut-off tab at the edge and no sign that four more sit to the
left.

Three shapes worth considering, in rough order of how much they change:

1. Shorter labels under a breakpoint (`1–5`, `6–10`, … `Slutt`).
2. Make Back/Next the primary navigation on narrow screens and reduce the strip to a
   `3 of 6` indicator.
3. Keep the strip but add edge affordances (fade, arrows) so the overflow is visible.

Relevant code: `initSectionTabs` and `initFormSteps` in `wwwroot/js/survey.js`, `.sc-tabs` in
`startcompass.css`. Whatever is chosen must keep working with JavaScript off, where the form
is one long column and there is no strip at all.

### 3. The coach team overview asks a lot at once

**Measured on one page: 13 tables, 85 rows, 33 decimal numbers, 2677 px tall, four separate
tab groups.**

The explanatory prose is a genuine strength — it actively stops a coach misreading data about
a child, and it should not be cut. But it sits between the coach and the numbers. The summary
that answers "who do I talk to first" already exists (*"highest in Communication (3.3) and
lowest in Concentration (3.2)"*) and is buried mid-page.

The suggestion is to lift a short answer — strongest C, weakest C, who has not answered — to
the top, and leave the method underneath it. This is a judgement call about who the page is
for on a Sunday evening, which is why it was not just done.

Relevant: `Views/Coach/FiveCTeam.cshtml`, `Views/Shared/_FiveCTeamOverview.cshtml`.

### 4. A scoped CSS rule overrides the design system

`Views/Shared/_Layout.cshtml.css` ships `a { color: #0077cc }`, which the build turns into
`a[b-f8hswsp4hl]` — two classes' worth of specificity, loaded **after** `startcompass.css`.
Any single-class `.sc-…` rule that sets a link colour loses to it.

This was hit for real while adding the skip link: it rendered blue on black at 4.06:1 until
the rule was raised to `a.sc-skip:link`. **That workaround is a symptom.** A cleanup should
look at every link in the `sc-` system, decide whether the scoped rule should exist at all,
and remove the specificity workaround if it does not.

### 5. Tab restoration helps corrections and hurts first runs

`survey.js` remembers the last opened panel per form in `sessionStorage`. That is right when
somebody returns to correct one answer, and wrong when somebody opens a form they have never
filled in and lands on block 4. Consider restoring only when the form is a correction
(`Model.IsCorrection`).

### 6. Advisory

- Navigation links are 38 px tall, Privacy is 21 px. Passes AA, misses AAA.
- On a 375 px screen the hero is 279 px and the first statement starts at 649 px of 812 px
  visible — nearly a full screen before the task begins.

---

## House rules any of this has to respect

These are not preferences; breaking them breaks something.

- **No user-visible text in a `.cshtml` file if it can come from the question catalog.**
  Replacing the whole question set must mean editing
  `Data/Questions/five-c-questions.json` and nothing else.
- **The CSP has no `unsafe-inline`.** No `style="…"` attributes and no inline `<script>`.
  Colours are class names from `QuestionColors`, never hex values from data.
- **The form must work with JavaScript off.** Tabs, step navigation and the draft are all
  enhancements over a page that is one long column and submits correctly without them.
- **Null is not 3.** An unanswered statement stays out of every mean rather than being pulled
  to the middle of the scale.
- **Comments explain why, not what.** The codebase is written this way throughout; match it.
- **Run the tests.** `dotnet test` — 197 passing as of this file. `StartCompassFactory` gives
  page-level tests a real rendered form, which is where most of this is best asserted.

---

## How the measurements were taken

Findings above with numbers came from a script run in the browser against the running app,
per page and per viewport: WCAG contrast over every text node with its resolved background,
bounding boxes of every interactive element, heading order, landmarks, and live regions.
Re-running something similar is the cheapest way to check a fix, and the only way to catch a
regression like the skip link one.
