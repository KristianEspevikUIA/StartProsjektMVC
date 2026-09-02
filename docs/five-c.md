# The 5C questionnaire

Twenty-five statements in five categories, answered on a 1–5 scale by the player, their
guardian and their coach — and a coach view that shows where the three of them disagree.

All interface text is English, matching the StartCompass site and the wireframes.

---

## Where things are

| What | Where |
| --- | --- |
| **The questions** | `Data/Questions/five-c-questions.json` |
| Loading and validating them | `Services/FiveC/QuestionCatalog.cs` |
| The form | `Controllers/SurveyController.cs`, `Views/Survey/` |
| What is sent when a form is submitted | `Contracts/FiveC/SurveySubmission.cs` (+ `.ts` mirror) |
| Storage | `Services/FiveC/ISurveySubmissionStore.cs` and its two implementations |
| Player vs guardian vs coach | `Services/FiveC/FiveCAnalysisService.cs` |
| The difference scores | `Services/FiveC/FiveCDifference.cs` |
| Coach views | `CoachController.FiveCTeam` / `.FiveCPlayer`, `Views/Coach/` |
| Scale bounds, the follow-up rule and the difference bands | `Models/FiveC/FiveCRules.cs` |
| Styling | `wwwroot/css/startcompass.css` |

---

## Replacing the questions

Edit `Data/Questions/five-c-questions.json`. Nothing else.

No `.cshtml` file contains a category name or a statement — the form, the front page and
the coach overview all read the catalog. Adding a sixth category or dropping to four
questions in one of them needs no code change either; the app logs a warning when the shape
is not 5 × 5, but it runs.

Two rules when editing:

- **`key` is what answers are stored against.** Rewrite `text` as much as you like. Changing
  a `key` orphans every answer already submitted for it — that is a different question, not
  a reworded one.
- **`reversed: true`** marks a negatively worded statement. It is scored as `6 − value` when
  it is read, so a high score always means "good". Do not flip the scale in the form to
  compensate; that reverses it twice.

`textAboutPlayer` is optional and null everywhere today, so all three roles get an identical
form. Fill it in and coaches and guardians see that wording while the player keeps `text` —
that is the hook the wireframes describe, ready but not switched on.

The file is validated at startup. A duplicate key, a missing `text` or a scale that does not
line up stops the application with a message naming the problem. That is deliberate: the
alternative is discovering it as a half-empty form, mid-round, in front of a 14-year-old.

`version` is stored with every submission, so it is knowable afterwards which wording a set
of answers was given against.

---

## Sharing a form as a link

```
/Survey/Fill?roundId=2&playerId=14&role=Coach
```

`role` is optional. Left out, the most direct role the signed-in user holds for that player
is used.

**The link carries no authority.** It preselects the round, the player and the role, and
nothing else. Whoever follows it signs in as themselves, and the server re-runs both checks:

1. `CanViewPlayer` — the existing resource policy.
2. Whether that user actually holds the role in the link (`SurveyAssignmentService`). A coach
   cannot answer as the player by editing the query string.

Both run on GET and again on POST. The hidden fields in the form are input, not proof.

### Why not a token per submission

A one-time token in the URL is the other obvious option, and it is what "unique link per
submission" usually means. It was not chosen, because a token that works without signing in
is a bearer credential for a minor's record: it survives in message history, in forwarded
mail and in browser history, and it cannot be tied to a person afterwards. This repo is
built the other way round — self-registration is closed, the fallback policy denies by
default, and access is decided per player rather than per role.

If the club does want links that work without an account — realistically for guardians —
that is a deliberate decision with a real design behind it: single-use, short-lived, stored
hashed, scoped to one player and one round, and revocable. It is not a query parameter.

---

## Storing answers

> **This section is now half out of date, on purpose.** It was written when the app ran on
> local SQLite and Supabase was a separate thing to reach over HTTP. Since 26 August the app
> connects to the Supabase Postgres database directly, through EF Core and Npgsql — see the
> "Kom i gang" section in the README.
>
> That makes `SupabaseSurveySubmissionStore` redundant: it goes out over PostgREST to a
> database the process is already connected to, with a second credential, no shared
> transaction and no foreign keys to `Players` or `SurveyRounds`. The straightforward thing
> now is two EF entities and one migration, like everything else in the model. The store
> abstraction can stay — it is what keeps the in-memory fallback possible — but the live
> implementation should be an EF one.
>
> Not changed yet, because the 5C tables are Victor's to define and this is his call. The
> contract in `Contracts/FiveC/` is unaffected either way: it describes what the form hands
> over, not how it is written.
>
> **Do not use the anon/publishable key (`sb_publishable_…`) for this.** It is designed to be
> public and it is subject to row level security. Answers about minors behind a key that
> ships to browsers is the wrong shape regardless of what the policies say.

`ISurveySubmissionStore` has three implementations, and configuration picks one:

- **`EfSurveySubmissionStore`** — **the default**, and what runs unless something is
  configured. Answers go in the application's own database, which since the move to Npgsql
  *is* Supabase: `FiveCSubmissions` and `FiveCAnswers`, with real foreign keys to `Players`
  and `SurveyRounds`. One credential, one connection, one transaction.
- **`SupabaseSurveySubmissionStore`** — used when `FiveC:Supabase:Url` and `:ApiKey` are both
  set, for a *genuinely separate* Supabase project. Talks to PostgREST directly; no client
  library.
- **`InMemorySurveySubmissionStore`** — only when `FiveC:Store` is set to `"InMemory"`.
  Answers live in memory and are gone when the process stops, which is what makes it useful
  for a demo and useless for anything else. In Development it seeds itself with made-up
  submissions so the coach overview has something to draw.

An empty `Url` or `ApiKey` therefore means the database, not memory. That was the other way
round while the app ran on local SQLite, and every answer disappeared on restart.

Which one is live is written to the log at startup, and shown to admins on `/Survey`.

All three implement `CountByRoundAsync`, which answers "how many submissions does each of
these periods hold" in one round trip. The admin period list is the only caller, and it used
to ask per period — reading every submission with all twenty-five of its answers just to call
`.Count` on the list.

### Configuration

`appsettings.json` carries the URL and the table names. The key does not:

```bash
dotnet user-secrets set "FiveC:Supabase:ApiKey" "..." --project StartPraksisGruppe3Prosjekt
```

Use the service role key. The respondent is signed in *here*, not in Supabase, so there is no
user JWT to pass on and row level security cannot tell who is answering. That also means the
key must never reach the browser — and it does not: every request to Supabase is made
server-side.

### The contract

`Contracts/FiveC/SurveySubmission.cs` is what the form hands over. `survey-submission.ts` is
the same thing in TypeScript, for the Supabase side; the C# file is what actually runs, and
if the two disagree the C# one wins.

```json
{
  "round_id": 2,
  "player_id": 14,
  "player_code": "TS-08-16",
  "respondent_role": "coach",
  "respondent_user_id": "9f0c...",
  "question_set_version": "placeholder-2026-08-26",
  "submitted_at": "2026-08-26T07:30:00+00:00",
  "answers": [
    { "question_key": "commitment-1", "category_key": "commitment", "value": 4 }
  ]
}
```

**The schema is Victor's.** The two things this side depends on:

1. **One submission per `(round_id, player_id, respondent_user_id)`.** Submitting again is a
   correction, not a second row. The store upserts on those three columns, so they need a
   unique constraint — otherwise a corrected form quietly becomes two opinions.
2. **`value` must be nullable.** Null means "not answered", and null is not 3. A `NOT NULL`
   column turns every blank into a middling opinion and there is no way to tell afterwards.

The expected landing place is two tables:

```
five_c_submissions (id, round_id, player_id, player_code, respondent_role,
                    respondent_user_id, question_set_version, submitted_at)
five_c_answers     (submission_id -> five_c_submissions, question_key, category_key, value)
```

Table and column names are configuration, not constants, so renaming one of them is an
`appsettings.json` change rather than a code change.

**Not yet verified against a real project.** The tables did not exist when this was written,
so the request shapes follow the PostgREST documentation rather than a green test. The two
POSTs in `SupabaseSurveySubmissionStore` are the first thing to check once the tables are up.

---

## The coach overview

`/Coach` → team card → `/Coach/FiveCTeam/{id}` → `/Coach/FiveCPlayer/{id}`.

**Per category, per player**, the player's, the guardian's and the coach's averages sit side
by side as bars. The bars are inline SVG because the CSP has no `unsafe-inline`: a bar cannot
take its width from a `style=""` attribute, but `width` on an SVG `rect` is markup and is
unaffected. A chart library would need a CDN, which the CSP also blocks.

### The difference scores

Three numbers at the top of `/Coach/FiveCPlayer/{id}`, and the same three per category
further down:

| Score | What it compares |
| --- | --- |
| **Coach vs player** | The coach's answers against the player's own. |
| **Guardian vs player** | The guardian's answers against the player's own. |
| **Between all three** | The mean of the three pairwise scores — coach/player, guardian/player and coach/guardian. |

Each score is a **mean absolute difference per statement**, on the same 1–5 scale the answers
use. It runs 0 to 4: 0 is the same answer every time, 4 would be opposite ends of the scale
on all twenty-five.

Three rules make the number mean what it says:

- **Paired on the question key, not on the category average.** A 5 and a 1 average to the
  same 3 as two 3s do. A difference built from averages would call that agreement, so
  `RespondentGap.Between` pairs the two respondents statement by statement.
- **Only statements both of them answered.** There is no distance between an answer and a
  blank, so an unanswered statement is dropped rather than counted as anything.
- **Reversed statements are scored first.** The difference is measured on scores, not raw
  answers, so a negatively worded statement cannot flip the sign of a gap.

Alongside the unsigned score, `RespondentGap.SignedDifference` keeps the **direction** —
positive means the left respondent rated the player higher. The two say different things and
the page shows both: a small direction on top of a real distance is disagreement that cancels
out, not agreement, and the card says so in words.

`Between all three` degrades honestly. With only two respondents it is that single pair, and
the heading changes to "Between all who answered" so a two-way number never passes for a
three-way one. With one respondent there is no score at all — the card says which form is
missing rather than showing a zero.

The three bands are `FiveCRules.AgreementThreshold` (0.5) and
`FiveCRules.LargeDifferenceThreshold` (1.0), and `FiveCRules.LevelOf` rounds to one decimal
before banding — the band sits next to the number on screen, and that number is printed to
one decimal.

**The follow-up flag** (`FiveCRules.NeedsFollowUp`) fires when a player's own average for a
category is **below 2.0**, backed by **at least 3 answered statements** in it. Both numbers
are constants in `FiveCRules` — one place to change them.

Two is the "Disagree" point on the scale, so a player under it across a whole category is
disagreeing with the positive statements in all of it. The minimum answer count is what makes
it *consistent* rather than one bad day. It is based on the player's own answers, never on
what somebody else thinks about them.

Flagged players get a red badge on the team table, a red row, a red bar in the chart and a
banner on the detail page.

Nothing on these pages is stored. Every number is recalculated from the raw answers on each
request — the same rule the ten-statement gap follows, and for the same reason: a stored
judgement about a minor outlives the answers behind it, the consent that allowed it and the
round it belonged to.

### The team overview

Above the player rows on `/Coach/FiveCTeam/{id}` the squad is read as one, at the same three
levels a single player is read at: across all twenty-five statements, per category, and per
statement. Same bars, same partial (`_FiveCCategoryChart`), same 1–5 scale — which is the
point. A coach should not have to relearn the chart one level up.

**Every number is an average of players, not of answers.** At each level the squad value is
the mean of the per-player values at that level, so one player counts once whether they
answered five statements or twenty-five. Pooling every answer instead would let whoever
filled the form in most completely quietly weigh the most, and a team average is meant to
describe the average player.

Two things separate it from the rows below it:

- It is decided by **`CanViewTeamAggregate`**, once, against the real number of players
  behind the numbers — not by `CanViewPlayer` repeated for everyone. The count is part of
  the resource so that a controller cannot skip the threshold by forgetting to look at it.
- **A single role's line is withheld on the same threshold.** Fewer than
  `CanViewTeamAggregateRequirement.MinimumResponses` guardians answering makes the guardian
  average those guardians' answers with a team's name on it. `TeamRoleAverage.From` drops
  the number rather than passing it on, so no view has it to leak — and `Withheld` keeps
  "too few answered" apart from "nobody answered", which are different facts about a round.

The per-statement numbers here are **scored, not raw** — the opposite of the per-player
statement table. They sit beside category averages on a "higher is better" scale, and a raw
average on a reversed statement would be the one column in the section pointing the other
way. The statements are still marked `Reversed` so a reader can tell which ones were turned
round.

**Over time** is the same aggregate measured repeatedly: for each period and each C, the mean
of the players' own means. Only the players' answers, for the reason the individual line
gives — a squad line that moved when a coach changed their mind would be read as the squad
having developed. A period with too few players behind it is a gap rather than a drawn point,
and the page names it: an unexplained hole in a line reads as "nobody answered", and somebody
did.

### One component, four pages

Every long page in the 5C feature is built the same way, from the same code, and a coach or
a player who has learned one has learned all of them:

| Page | Panels |
| --- | --- |
| `Coach/FiveCTeam` | Overview, Per statement, Over time, Players |
| `Coach/FiveCPlayer` | Differences, The five C's, Statements, Over time, Sharing |
| `Shared/FiveCFeedback` | Status, Your answers, Who has looked |
| `Survey/Fill` | one panel per C, plus Back/Next and a live answered count |

A section opts in by carrying `data-tab-panel` and a `data-tab-label`; `survey.js` does the
rest. Two things are deliberately left OUT of the panels, because they are the reason the
page was opened and must never sit behind a tab: the "answer the form" call on the feedback
page, and **Save** on the form.

The team page holds four sections — the squad average, the same thing statement by
statement, the squad over time, and the players — and stacked in one column that is about
five screens of scrolling before a coach reaches the player they opened the page for.

`survey.js` turns them into tabs. The strip is **built from the panels that are actually
there**, not written in the view: a round with no aggregate has nothing to break down and
gets no "Per statement" tab, and a team with one period gets no trend panel to tab to.
Nothing in the markup has to be kept in step with what the controller decided.

It is an enhancement, not the structure. With JavaScript off there is no strip and no
panel is hidden — the page is the column of sections it always was, in the same order. That
is the same rule the search box follows, and the reason the tabs are not rendered server
side: a strip that switched nothing would be worse than a long page.

The selected tab is remembered per page in `sessionStorage`, so opening a player and coming
back returns to the section that was open. A `#sc-panel-N` link wins over the remembered
one, and `data-tab-open` from the server wins over both -- which is how the form opens the
first C still holding an unanswered statement after a rejected save, rather than leaving the
error message behind a tab nobody was told to press.

**The form is the one that does more than switch.** It is still tabs, and still looks like
the others, but a form needs forward motion: `initFormSteps` appends Back and Next to each
panel, writes the answered count onto every tab (`3/5`), and marks the tabs with something
still missing. Hidden panels post exactly as they would have in the long column -- the tabs
change what is on screen and nothing at all about what is saved.

### Finding a player in a squad

The player table filters live, on player code and position, over the squad already on the
page. Nothing is fetched and no code leaves the browser — every row is in the document, and
the filter only decides which are shown. The control is `hidden` in the markup and revealed
by `survey.js`, so with JavaScript off the table is complete and no dead search box appears.

Codes and positions, because those are the only things there are: this system holds no names.

### What the coach does and does not see

- **Nothing is withheld from a coach any more.** A coach sees every player and every number.
  The code that renders a row without figures, and the "no consent for individual views"
  wording next to it, are left over from when consent gated a coach — they are now
  unreachable. Removing them is on the list.
- "Has not answered" is still its own state, and still says so in words rather than showing
  a dash that could be read as a zero.
- Counts of *who answered* say nothing about any individual. They were shown for every row
  even when the numbers were not, which is why they were separated in the first place.
- No free-text field and no notes. A coach's written note about a minor is a new category of
  personal data and is not in the data model.

---

## Known limits

- **Nothing limits which players a coach can reach.** A coach is a coach: every coach sees
  every team, gets a form for every player in the club, and `CanViewTeam` /
  `CanViewTeamAggregate` no longer look at `CoachTeam`. Consent used to be the last
  remaining limit; the club asked for that to go too, and it did.

  What stands in its place is `PlayerAccessEvent` — an append-only record of who opened
  which player, from which page, when. It prevents nothing; it makes every lookup
  accountable afterwards. **If it stops being written, the rule in `CanViewPlayerHandler`
  has no counterweight at all**, so any new page that shows one player's answers has to call
  `IPlayerAccessLog.RecordAsync`. `AccessControlTests` holds that down for the two pages
  that exist.
- **`CoachTeam` is still in the model, but no longer grants or limits anything.** The table,
  the entity and the seeded rows are untouched — dropping them is a schema migration on a
  shared database and nobody has asked for it. The only remaining reader is the development
  demo-data seeder, which uses it to pick a plausible coach. If it is not going to come back,
  it should be removed deliberately, in its own change.
- **`/Survey` lists every player in the club for a coach**, which is why that page has
  filters: period, team, role, status and player code. If it becomes unusable again at a few
  hundred players, the answer is a better filter — not a quiet return to team-scoped access,
  which is an authorisation decision and belongs in `Authorization/`.
- **Consent no longer decides anything a coach does.** It is still recorded, still
  append-only, and still shown on the coach's player page — but nothing in the code branches
  on it any more. It has to be described that way in the Sikt filing, and the club has to
  confirm that is what they want. See `docs/sikt-melding.md`.
- **`ConsentService.GetCurrentLevelsAsync` was implemented here** (it was one of Brage's
  TODOs) because the team overview lists a whole squad and would otherwise do one query per
  player. Same rule as the single-player version. The rest of that service is untouched.
- **The ten-statement form still has no team aggregate.** `CoachController.Team` remains a
  TODO. The 5C side now goes through `CanViewTeamAggregate` and its 3-response threshold, so
  the older form has a worked example to follow rather than a policy nobody calls.
- **A team average cannot be compared against another team's.** Each squad is read on its
  own, which is deliberate for now: a league table of squads of minors is a different
  product decision and nobody has asked for it.
- **`Coach/Index` was implemented** to make the 5C pages reachable. The richer version, with
  gap figures for the ten-statement form, is still Taavi's.
