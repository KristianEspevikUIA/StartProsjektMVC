using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// The 5C questionnaire: 25 statements in five categories, answered on a 1-5 scale, and
/// the short reflection that closes the period -- the strongest C, the one to work on next,
/// and what would help. Both come from the question set file; see
/// <see cref="Models.FiveC.ReflectionSection"/>.
///
/// The same form is used by all three respondent types. What differs is the header -- who
/// is answering, and about whom -- not the statements. The statements themselves come from
/// <see cref="IQuestionCatalog"/> and are never written in a view.
///
/// Three things have to hold on every request here:
///
///   1. The round has to be open. A closed round gets <see cref="Closed"/>, not a 400.
///   2. The user has to be allowed to see this player (CanViewPlayer) AND allowed to answer
///      in the requested role (<see cref="ISurveyAssignmentService"/>). Both, on GET and
///      again on POST. The hidden fields in the form are input, not proof.
///   3. Answers are stored raw, 1-5. Reversal is a reading rule, not a writing one.
///
/// The statements are shown SHUFFLED, in blocks of five rather than in the five C's -- see
/// <see cref="IQuestionOrder"/>. The order depends only on the player and the period, so it
/// is the same on a redisplay and the same when the respondent comes back to correct an
/// answer. What is STORED is unaffected: the submission below is built from the catalog, in
/// catalog order, whatever order the form was in.
///
/// Sharing a form is a query string: /Survey/Fill?roundId=2&amp;playerId=14&amp;role=Coach.
/// The link only preselects who is answering about whom -- it grants nothing. Anyone
/// following it still signs in, and both checks above still run. See docs/five-c.md for
/// why that was chosen over a token in the URL.
/// </summary>
[Authorize]
public class SurveyController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IQuestionCatalog _catalog;
    private readonly IQuestionOrder _order;
    private readonly ISurveySubmissionStore _store;
    private readonly ISurveyAssignmentService _assignments;
    private readonly ILogger<SurveyController> _logger;
    private readonly IPeriodService _periods;
    private readonly IPeriodSelection _selection;

    public SurveyController(
        AppDbContext db,
        IAuthorizationService authz,
        IQuestionCatalog catalog,
        IQuestionOrder order,
        ISurveySubmissionStore store,
        ISurveyAssignmentService assignments,
        ILogger<SurveyController> logger,
        IPeriodService periods,
        IPeriodSelection selection)
    {
        _db = db;
        _authz = authz;
        _catalog = catalog;
        _order = order;
        _store = store;
        _assignments = assignments;
        _logger = logger;
        _periods = periods;
        _selection = selection;
    }

    /// <summary>
    /// The open round and the forms this user is expected to fill in.
    /// </summary>
    public async Task<IActionResult> Index(
        SurveyIndexViewModel.FilterInput filter,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // One trip for the periods. The picker needs all of them and the selection is made
        // from the same list -- ResolveAsync would fetch it a second time.
        var rounds = await _periods.GetAllAsync(cancellationToken);

        // The period asked for, otherwise the one remembered from last time, otherwise the
        // current one. Picking a period here and then opening the team overview keeps the
        // choice -- see IPeriodSelection.
        var selected = _selection.Select(rounds, filter.RoundId);

        var model = new SurveyIndexViewModel
        {
            Filter = filter,
            Rounds = rounds
                .Select(r => new SurveyIndexViewModel.RoundSummary(
                    r.Id, r.Name, r.OpensAt, r.ClosesAt, r.IsOpenAt(now)))
                .ToList(),
            StoreDescription = User.IsInRole(Roles.Admin) ? _store.Description : null
        };

        if (selected is null)
        {
            return View(model);
        }

        model.SelectedRound = new SurveyIndexViewModel.RoundSummary(
            selected.Id, selected.Name, selected.OpensAt, selected.ClosesAt, selected.IsOpenAt(now));

        model.SelectedRoundIsOpen = selected.IsOpenAt(now);
        model.Filter.RoundId = selected.Id;

        var all = await _assignments.GetAssignmentsAsync(User, selected.Id, cancellationToken);

        // Totals are counted before filtering. A progress figure that moves when you narrow
        // the list is telling you about the filter, not about the work left.
        model.TotalCount = all.Count;
        model.TotalAnswered = all.Count(a => a.HasAnswered);

        model.Teams = all
            .Select(a => a.TeamName)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        model.Roles = all
            .Select(a => a.Role)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        model.Assignments = Filtered(all, filter).ToList();

        return View(model);
    }

    /// <summary>
    /// Applies the filters in memory. The list is already loaded -- it is one round's worth
    /// of forms for one user -- so a second trip to the database would buy nothing.
    /// </summary>
    private static IEnumerable<SurveyAssignment> Filtered(
        IReadOnlyList<SurveyAssignment> assignments,
        SurveyIndexViewModel.FilterInput filter)
    {
        IEnumerable<SurveyAssignment> result = assignments;

        if (!string.IsNullOrWhiteSpace(filter.Team))
        {
            result = result.Where(a =>
                string.Equals(a.TeamName, filter.Team, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Role is { } role)
        {
            result = result.Where(a => a.Role == role);
        }

        result = filter.Status switch
        {
            FormStatus.Pending => result.Where(a => !a.HasAnswered),
            FormStatus.Completed => result.Where(a => a.HasAnswered),
            _ => result
        };

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var query = filter.Query.Trim();

            // Player code only. Names are not in this system, and searching one would be a
            // different feature with a different privacy question attached.
            result = result.Where(a =>
                a.PlayerCode.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    /// <summary>
    /// The form.
    /// </summary>
    /// <param name="roundId">The round being answered.</param>
    /// <param name="playerId">The player the answers are about.</param>
    /// <param name="role">
    /// Who is answering. Optional: left out, the most direct role this user has for this
    /// player is used. Supplied, it still has to be a role they actually hold -- a coach
    /// cannot answer as the player by editing the link.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> Fill(
        int roundId,
        int playerId,
        RespondentType? role,
        CancellationToken cancellationToken)
    {
        var context = await ResolveAsync(roundId, playerId, role, cancellationToken);
        if (context.Failure is not null)
        {
            return context.Failure;
        }

        var (round, player, respondent) = context;

        if (!round.IsOpenAt(DateTimeOffset.UtcNow))
        {
            return await ClosedViewAsync(round, player, respondent, cancellationToken);
        }

        // Pre-fill from what this person sent last time, so a correction starts from their
        // own answers rather than from a blank form.
        var existing = await _store.FindAsync(round.Id, player.Id, UserId, cancellationToken);

        var model = BuildForm(round, player, respondent, existing);

        return View(model);
    }

    /// <summary>
    /// Saving.
    ///
    /// Everything is checked again from the database. The round, the player, the role and
    /// the question keys all come back from the browser, and none of them are trusted.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> Fill(SurveyFormViewModel model, CancellationToken cancellationToken)
    {
        var context = await ResolveAsync(
            model.RoundId,
            model.PlayerId,
            model.Respondent,
            cancellationToken);

        if (context.Failure is not null)
        {
            return context.Failure;
        }

        var (round, player, respondent) = context;

        // Applies to POST too: somebody who had the form open when the round closed meets
        // the same page as somebody following an old link. Not an error, not a silent accept.
        if (!round.IsOpenAt(DateTimeOffset.UtcNow))
        {
            return await ClosedViewAsync(round, player, respondent, cancellationToken);
        }

        var answers = ReadAnswers(model);
        var reflection = ReadReflection(model);

        if (!ModelState.IsValid)
        {
            // Rebuild everything that is not bound -- questions, scale, headings -- from the
            // catalog. Only the values the respondent chose survive a failed post.
            var existing = await _store.FindAsync(round.Id, player.Id, UserId, cancellationToken);
            var redisplay = BuildForm(round, player, respondent, existing);

            foreach (var input in redisplay.Answers)
            {
                if (answers.TryGetValue(input.QuestionKey, out var value))
                {
                    input.Value = value;
                }
            }

            // The same for the reflection: a rejected save must not throw away the paragraph
            // somebody just wrote because a statement further up was left blank.
            foreach (var input in redisplay.ReflectionAnswers)
            {
                if (reflection.TryGetValue(input.QuestionKey, out var written))
                {
                    input.Value = written;
                }
            }

            return View(redisplay);
        }

        var submission = new SurveySubmission
        {
            RoundId = round.Id,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = SurveySubmission.Roles.From(respondent),
            RespondentUserId = UserId,
            QuestionSetVersion = _catalog.Questions.Version,
            SubmittedAt = DateTimeOffset.UtcNow,
            Answers = _catalog.Questions.Categories
                .SelectMany(category => category.Questions.Select(question => new SurveyAnswer
                {
                    QuestionKey = question.Key,
                    CategoryKey = category.Key,
                    // Raw, unreversed, exactly as it was answered.
                    Value = answers.TryGetValue(question.Key, out var value) ? value : null
                }))
                .ToList(),

            // Driven by the catalog for the same reason the answers are: the browser decides
            // what it posts, the question set decides what is stored. Blanks are carried as
            // null and dropped by the store -- a question left alone leaves no row.
            Reflection = _catalog.Questions.ReflectionQuestions
                .Select(question => new ReflectionAnswer
                {
                    QuestionKey = question.Key,
                    Value = reflection.TryGetValue(question.Key, out var written) ? written : null
                })
                .ToList()
        };

        await _store.SaveAsync(submission, cancellationToken);

        _logger.LogInformation(
            "5C form submitted: round {RoundId}, player {PlayerId}, role {Role}.",
            round.Id,
            player.Id,
            submission.RespondentRole);

        TempData["SurveyMessage"] = $"Your answers for {player.Code} are saved. Thank you!";

        return RedirectToAction(nameof(Index));
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// The three lookups and the two authorisation checks that every request here needs.
    /// Returns a failure result instead of the tuple when any of them says no, so the
    /// actions above stay readable.
    /// </summary>
    private async Task<ResolvedForm> ResolveAsync(
        int roundId,
        int playerId,
        RespondentType? requestedRole,
        CancellationToken cancellationToken)
    {
        var round = await _db.SurveyRounds
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roundId, cancellationToken);

        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (round is null || player is null)
        {
            return ResolvedForm.Failed(NotFound());
        }

        // The pattern from CoachController.PlayerDetail. The role let you into the
        // controller; the policy decides whether THIS player is yours.
        var canView = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!canView.Succeeded)
        {
            // Forbid rather than NotFound: the user is signed in, they just are not allowed
            // here. Consent is no longer part of that decision -- a coach may open any player
            // in the club, and what stands in for the old consent check is the audit log in
            // IPlayerAccessLog. The rule itself lives in CanViewPlayerHandler; if answering
            // should ever get a rule of its own, it belongs in Authorization/ too, not here.
            return ResolvedForm.Failed(Forbid());
        }

        var allowedRoles = await _assignments.GetAllowedRolesAsync(User, player, cancellationToken);
        if (allowedRoles.Count == 0)
        {
            return ResolvedForm.Failed(Forbid());
        }

        // A link may name a role. It may not invent one.
        var respondent = requestedRole ?? allowedRoles[0];
        if (!allowedRoles.Contains(respondent))
        {
            return ResolvedForm.Failed(Forbid());
        }

        return new ResolvedForm(round, player, respondent, null);
    }

    /// <summary>
    /// Builds the form from the question catalog. Called on GET and again whenever a POST
    /// has to be redisplayed, so there is exactly one place that decides what is on the page.
    /// </summary>
    private SurveyFormViewModel BuildForm(
        SurveyRound round,
        Player player,
        RespondentType respondent,
        SurveySubmission? existing)
    {
        var previous = existing?.Answers.ToDictionary(a => a.QuestionKey, a => a.Value)
                       ?? new Dictionary<string, int?>();

        var previousReflection = existing?.Reflection
                                     .ToDictionary(a => a.QuestionKey, a => a.Value, StringComparer.OrdinalIgnoreCase)
                                 ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var model = new SurveyFormViewModel
        {
            RoundId = round.Id,
            RoundName = round.Name,
            RoundClosesAt = round.ClosesAt,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            TeamName = player.Team?.Name ?? string.Empty,
            Respondent = respondent,
            QuestionSetVersion = _catalog.Questions.Version,
            IsCorrection = existing is not null,
            Scale = _catalog.Questions.Scale
        };

        // The order this player and period get, which is not the catalog order -- see
        // IQuestionOrder. Derived from the two ids, so a redisplay after a failed POST
        // rebuilds exactly the form the respondent was looking at.
        var ordered = _order.For(player.Id, round.Id);

        var sections = new List<SurveyFormViewModel.Section>();

        // Blocks of five, cut out of the shuffled sequence. They are positions in the form
        // and not the five C's: a block normally holds statements from four or five
        // different categories, which is the point.
        foreach (var block in ordered.Chunk(QuestionOrder.BlockSize))
        {
            var questions = new List<SurveyFormViewModel.SectionQuestion>(block.Length);

            foreach (var item in block)
            {
                var index = model.Answers.Count;

                model.Answers.Add(new SurveyFormViewModel.QuestionInput
                {
                    QuestionKey = item.Question.Key,
                    Value = previous.TryGetValue(item.Question.Key, out var value) ? value : null
                });

                questions.Add(new SurveyFormViewModel.SectionQuestion(
                    Index: index,
                    Number: item.Number,
                    Text: item.Question.TextFor(respondent),
                    Reversed: item.Question.Reversed,
                    CategoryName: item.Category.Name,
                    Color: item.Color));
            }

            var first = block[0].Number;
            var last = block[^1].Number;

            sections.Add(new SurveyFormViewModel.Section(
                Key: $"block-{sections.Count + 1}",
                // An en dash, and the same range the tab strip shows, so the heading and
                // the tab are the same words. A single-statement block says "25", not
                // "25-25", which is what a last block of one would otherwise read as.
                Name: first == last ? $"Statement {first}" : $"Statements {first}\u2013{last}",
                Description: string.Empty,
                Questions: questions));
        }

        model.Sections = sections;
        model.Reflection = BuildReflection(model, respondent, previousReflection);

        return model;
    }

    /// <summary>
    /// The reflection block, or null when the question set does not have one.
    ///
    /// The choices are the catalog's own categories, so the five C's a respondent picks
    /// between are by construction the five they have just answered about -- adding a sixth
    /// C to the file adds it here too, with no code change.
    /// </summary>
    private SurveyFormViewModel.ReflectionBlock? BuildReflection(
        SurveyFormViewModel model,
        RespondentType respondent,
        IReadOnlyDictionary<string, string?> previous)
    {
        var reflection = _catalog.Questions.Reflection;

        if (reflection is null || reflection.Questions.Count == 0)
        {
            return null;
        }

        var choices = _catalog.Questions.Categories
            .Select(c => new SurveyFormViewModel.ReflectionChoice(c.Key, c.Name))
            .ToList();

        var fields = new List<SurveyFormViewModel.ReflectionField>();
        var number = 0;

        foreach (var question in reflection.Questions)
        {
            var index = model.ReflectionAnswers.Count;

            model.ReflectionAnswers.Add(new SurveyFormViewModel.ReflectionInput
            {
                QuestionKey = question.Key,
                Value = previous.TryGetValue(question.Key, out var written) ? written : null
            });

            fields.Add(new SurveyFormViewModel.ReflectionField(
                Index: index,
                Number: ++number,
                Text: question.TextFor(respondent),
                IsCategoryChoice: question.IsCategoryChoice,
                Required: question.Required,
                MaxLength: question.MaxLength,
                Placeholder: question.Placeholder,
                Choices: question.IsCategoryChoice
                    ? choices
                    : Array.Empty<SurveyFormViewModel.ReflectionChoice>()));
        }

        return new SurveyFormViewModel.ReflectionBlock(
            reflection.Title,
            reflection.Description,
            fields);
    }

    /// <summary>
    /// Reads the posted answers against the catalog and records anything wrong in ModelState.
    ///
    /// The browser decides which keys it sends. This is where that stops mattering: an
    /// unknown key is dropped, and a missing one is reported. What ends up stored is driven
    /// by the catalog, in <see cref="Fill(SurveyFormViewModel, CancellationToken)"/>.
    /// </summary>
    private Dictionary<string, int?> ReadAnswers(SurveyFormViewModel model)
    {
        var scale = _catalog.Questions.Scale;
        var answers = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < model.Answers.Count; i++)
        {
            var input = model.Answers[i];

            if (_catalog.FindQuestion(input.QuestionKey) is null)
            {
                // Either the form is stale because the question set changed under the
                // respondent, or someone is editing field names. Same handling either way.
                _logger.LogWarning(
                    "Discarded an answer for the unknown question key '{QuestionKey}'.",
                    input.QuestionKey);
                continue;
            }

            if (input.Value is { } value && (value < scale.Min || value > scale.Max))
            {
                ModelState.AddModelError(
                    $"Answers[{i}].Value",
                    $"Choose a value between {scale.Min} and {scale.Max}.");
                continue;
            }

            answers[input.QuestionKey] = input.Value;
        }

        // Unanswered questions. Allowed only when the question set opens for it, in which
        // case a blank is stored as null -- never as the middle of the scale.
        if (!scale.AllowDontKnow)
        {
            var catalogIndex = 0;

            foreach (var question in _catalog.Questions.AllQuestions)
            {
                var answered = answers.TryGetValue(question.Key, out var value) && value.HasValue;

                if (!answered)
                {
                    // Attach the message to the field the browser actually posted, so the
                    // validation summary lines up with the statement on screen even if the
                    // form is out of step with the catalog.
                    var postedIndex = model.Answers.FindIndex(
                        a => string.Equals(a.QuestionKey, question.Key, StringComparison.OrdinalIgnoreCase));

                    ModelState.AddModelError(
                        $"Answers[{(postedIndex >= 0 ? postedIndex : catalogIndex)}].Value",
                        "This statement has not been answered.");
                }

                catalogIndex++;
            }
        }

        return answers;
    }

    /// <summary>
    /// Reads the posted reflection against the catalog, the same way <see cref="ReadAnswers"/>
    /// reads the statements: an unknown key is dropped, and anything wrong is recorded in
    /// ModelState rather than stored.
    ///
    /// Three things are checked here and cannot be checked in the view model, because they
    /// depend on which question this is: that a chosen C is one of the C's in the file, that
    /// a written answer is within the length that question allows, and that a question the
    /// file marks as required was actually answered.
    ///
    /// What comes back is keyed by question and is what gets stored -- blanks included, as
    /// null. Values that failed a check are in there too, so a rejected save redisplays what
    /// the respondent wrote instead of clearing it; nothing is stored unless ModelState is
    /// valid.
    /// </summary>
    private Dictionary<string, string?> ReadReflection(SurveyFormViewModel model)
    {
        var written = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < model.ReflectionAnswers.Count; i++)
        {
            var input = model.ReflectionAnswers[i];
            var question = _catalog.FindReflectionQuestion(input.QuestionKey);

            if (question is null)
            {
                // Either the form is stale because the question set changed under the
                // respondent, or someone is editing field names. Same handling as for a
                // statement: dropped, and said out loud in the log.
                _logger.LogWarning(
                    "Discarded an answer for the unknown reflection question '{QuestionKey}'.",
                    input.QuestionKey);
                continue;
            }

            var value = input.Value?.Trim();

            if (string.IsNullOrEmpty(value))
            {
                // A blank is not an empty string. It is stored as null, or not at all.
                written[question.Key] = null;
                continue;
            }

            written[question.Key] = value;

            if (question.IsCategoryChoice)
            {
                var category = _catalog.FindCategory(value);

                if (category is null)
                {
                    ModelState.AddModelError(
                        $"ReflectionAnswers[{i}].Value",
                        "Choose one of the five C's.");
                    continue;
                }

                // The catalog's own spelling, not the browser's. What is stored has to match
                // the category keys everything else groups on.
                written[question.Key] = category.Key;
            }
            else if (value.Length > question.MaxLength)
            {
                ModelState.AddModelError(
                    $"ReflectionAnswers[{i}].Value",
                    $"Keep this to {question.MaxLength} characters or fewer.");
            }
        }

        // Questions the file marks as required. Off by default -- see ReflectionQuestion.Required.
        foreach (var question in _catalog.Questions.ReflectionQuestions.Where(q => q.Required))
        {
            var answered = written.TryGetValue(question.Key, out var value)
                           && !string.IsNullOrWhiteSpace(value);

            if (answered)
            {
                continue;
            }

            var postedIndex = model.ReflectionAnswers.FindIndex(
                a => string.Equals(a.QuestionKey, question.Key, StringComparison.OrdinalIgnoreCase));

            ModelState.AddModelError(
                $"ReflectionAnswers[{(postedIndex >= 0 ? postedIndex : 0)}].Value",
                "This question has not been answered.");
        }

        return written;
    }

    /// <summary>Builds the closed-round page, including whether this user already answered.</summary>
    private async Task<IActionResult> ClosedViewAsync(
        SurveyRound round,
        Player player,
        RespondentType respondent,
        CancellationToken cancellationToken)
    {
        var existing = await _store.FindAsync(round.Id, player.Id, UserId, cancellationToken);

        return View("Closed", new SurveyClosedViewModel
        {
            RoundName = round.Name,
            OpensAt = round.OpensAt,
            ClosesAt = round.ClosesAt,
            NotOpenYet = DateTimeOffset.UtcNow < round.OpensAt,
            HasAnswered = existing is not null,
            PlayerCode = player.Code
        });
    }

    /// <summary>Round, player and role once all the checks have passed -- or the result to return instead.</summary>
    private readonly record struct ResolvedForm(
        SurveyRound Round,
        Player Player,
        RespondentType Respondent,
        IActionResult? Failure)
    {
        public static ResolvedForm Failed(IActionResult result) =>
            new(null!, null!, default, result);

        public void Deconstruct(out SurveyRound round, out Player player, out RespondentType respondent)
        {
            round = Round;
            player = Player;
            respondent = Respondent;
        }
    }
}
