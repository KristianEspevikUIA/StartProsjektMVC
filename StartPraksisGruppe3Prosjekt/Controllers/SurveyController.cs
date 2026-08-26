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
/// The 5C questionnaire: 25 statements in five categories, answered on a 1-5 scale.
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
    private readonly ISurveySubmissionStore _store;
    private readonly ISurveyAssignmentService _assignments;
    private readonly ILogger<SurveyController> _logger;
    private readonly IPeriodSelection _selection;

    public SurveyController(
        AppDbContext db,
        IAuthorizationService authz,
        IQuestionCatalog catalog,
        ISurveySubmissionStore store,
        ISurveyAssignmentService assignments,
        ILogger<SurveyController> logger,
        IPeriodSelection selection)
    {
        _db = db;
        _authz = authz;
        _catalog = catalog;
        _store = store;
        _assignments = assignments;
        _logger = logger;
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

        var rounds = await _db.SurveyRounds
            .AsNoTracking()
            .OrderByDescending(r => r.ClosesAt)
            .ToListAsync(cancellationToken);

        // The period asked for, otherwise the one remembered from last time, otherwise the
        // current one. Picking a period here and then opening the team overview keeps the
        // choice -- see IPeriodSelection.
        var selected = await _selection.ResolveAsync(filter.RoundId, cancellationToken);

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
            // here. Note that for a coach this also fails when consent is not Full -- which
            // means a coach cannot record their expectation about a player who has not
            // consented. That follows from the policy rather than from a decision made here;
            // if answering should have its own rule, it belongs in Authorization/.
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

        var sections = new List<SurveyFormViewModel.Section>();
        var number = 0;

        foreach (var category in _catalog.Questions.Categories)
        {
            var questions = new List<SurveyFormViewModel.SectionQuestion>();

            foreach (var question in category.Questions)
            {
                var index = model.Answers.Count;

                model.Answers.Add(new SurveyFormViewModel.QuestionInput
                {
                    QuestionKey = question.Key,
                    Value = previous.TryGetValue(question.Key, out var value) ? value : null
                });

                questions.Add(new SurveyFormViewModel.SectionQuestion(
                    Index: index,
                    Number: ++number,
                    Text: question.TextFor(respondent),
                    Reversed: question.Reversed));
            }

            sections.Add(new SurveyFormViewModel.Section(
                category.Key,
                category.Name,
                category.Description,
                questions));
        }

        model.Sections = sections;

        return model;
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
