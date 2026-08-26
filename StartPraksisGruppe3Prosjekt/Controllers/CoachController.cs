using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Owner: Taavi.
///
/// [Authorize(Roles = ...)] only gets you into the controller. It says nothing about WHICH
/// players you are allowed to see. Every action that takes a player id has to run the
/// resource check as well -- see <see cref="PlayerDetail"/>, which is the pattern everyone
/// follows.
///
/// The 5C actions (<see cref="FiveCTeam"/>, <see cref="FiveCPlayer"/>) are the coach side of
/// the 5C questionnaire and were added with that feature. The gap views for the older
/// ten-statement form -- <see cref="Team"/>, <see cref="PlayerDetail"/>, <see cref="Search"/>
/// -- are still Taavi's TODOs and are left as they were.
/// </summary>
[Authorize(Roles = Roles.Coach + "," + Roles.Admin)]
public class CoachController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IScoringService _scoring;
    private readonly IConsentService _consent;
    private readonly IFiveCAnalysisService _fiveC;
    private readonly IQuestionCatalog _catalog;

    public CoachController(
        AppDbContext db,
        IAuthorizationService authz,
        IScoringService scoring,
        IConsentService consent,
        IFiveCAnalysisService fiveC,
        IQuestionCatalog catalog)
    {
        _db = db;
        _authz = authz;
        _scoring = scoring;
        _consent = consent;
        _fiveC = fiveC;
        _catalog = catalog;
    }

    /// <summary>
    /// The coach's teams, with how far each has got in the current round.
    ///
    /// Teams come from CoachTeam for the signed-in user, never from the role: a coach is
    /// not a coach for everyone.
    /// </summary>
    public async Task<IActionResult> Index(int? roundId, CancellationToken cancellationToken)
    {
        var round = await ResolveRoundAsync(roundId, cancellationToken);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Admins are not registered on a team, and would otherwise get an empty page on a
        // controller they are allowed into.
        var teams = User.IsInRole(Roles.Admin)
            ? await _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken)
            : await _db.CoachTeams
                .AsNoTracking()
                .Where(ct => ct.CoachUserId == userId)
                .Select(ct => ct.Team!)
                .OrderBy(t => t.Name)
                .ToListAsync(cancellationToken);

        var model = new CoachTeamsViewModel
        {
            RoundId = round?.Id,
            RoundName = round?.Name,
            RoundClosesAt = round?.ClosesAt,
            RoundIsOpen = round?.IsOpenAt(DateTimeOffset.UtcNow) == true
        };

        if (round is null || teams.Count == 0)
        {
            model.Teams = teams
                .Select(t => new CoachTeamsViewModel.TeamCard(t.Id, t.Name, 0, 0, 0, 0))
                .ToList();

            return View(model);
        }

        var cards = new List<CoachTeamsViewModel.TeamCard>();

        foreach (var team in teams)
        {
            var players = await _db.Players
                .AsNoTracking()
                .Where(p => p.TeamId == team.Id)
                .ToListAsync(cancellationToken);

            var comparisons = await _fiveC.GetForPlayersAsync(
                round.Id,
                players.ToDictionary(p => p.Id, p => p.Code),
                cancellationToken);

            var playersAnswered = 0;
            var coachAnswered = 0;
            var followUp = 0;

            foreach (var player in players)
            {
                var comparison = comparisons[player.Id];

                // Counting who answered is neutral -- it says nothing about any individual,
                // and so does not depend on consent.
                if (comparison.PlayerHasAnswered)
                {
                    playersAnswered++;
                }

                if (comparison.CoachHasAnswered)
                {
                    coachAnswered++;
                }

                // The follow-up count is about an individual, so it only includes players
                // this coach is actually allowed to see.
                if (comparison.NeedsFollowUp)
                {
                    var allowed = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
                    if (allowed.Succeeded)
                    {
                        followUp++;
                    }
                }
            }

            cards.Add(new CoachTeamsViewModel.TeamCard(
                team.Id,
                team.Name,
                players.Count,
                playersAnswered,
                coachAnswered,
                followUp));
        }

        model.Teams = cards;

        return View(model);
    }

    /// <summary>
    /// The 5C overview for one team: a row per player, and who needs following up.
    /// </summary>
    public async Task<IActionResult> FiveCTeam(int id, int? roundId, CancellationToken cancellationToken)
    {
        var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        // Same pattern as PlayerDetail: the role let you into the controller, the policy
        // decides whether THIS team is yours. Without it a coach could walk team ids and
        // learn which teams exist.
        var teamAllowed = await _authz.AuthorizeAsync(User, team, Policies.CanViewTeam);
        if (!teamAllowed.Succeeded)
        {
            return Forbid();
        }

        var round = await ResolveRoundAsync(roundId, cancellationToken);
        if (round is null)
        {
            return View(new FiveCTeamViewModel
            {
                TeamId = team.Id,
                TeamName = team.Name,
                Rounds = await RoundOptionsAsync(cancellationToken)
            });
        }

        var players = await _db.Players
            .AsNoTracking()
            .Where(p => p.TeamId == team.Id)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        var comparisons = await _fiveC.GetForPlayersAsync(
            round.Id,
            players.ToDictionary(p => p.Id, p => p.Code),
            cancellationToken);

        var consentLevels = await _consent.GetCurrentLevelsAsync(
            players.Select(p => p.Id),
            cancellationToken);

        var model = new FiveCTeamViewModel
        {
            TeamId = team.Id,
            TeamName = team.Name,
            RoundId = round.Id,
            RoundName = round.Name,
            RoundClosesAt = round.ClosesAt,
            RoundIsOpen = round.IsOpenAt(DateTimeOffset.UtcNow),
            Rounds = await RoundOptionsAsync(cancellationToken)
        };

        foreach (var player in players)
        {
            var comparison = comparisons[player.Id];

            var allowed = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
            var canView = allowed.Succeeded;

            model.Players.Add(new FiveCTeamViewModel.PlayerRow
            {
                PlayerId = player.Id,
                Code = player.Code,
                Position = player.Position,
                CanView = canView,
                Consent = consentLevels.TryGetValue(player.Id, out var level) ? level : ConsentLevel.None,

                // Progress is neutral and is shown for every row. What was answered is not.
                PlayerHasAnswered = comparison.PlayerHasAnswered,
                GuardianHasAnswered = comparison.GuardianHasAnswered,
                CoachHasAnswered = comparison.CoachHasAnswered,

                Comparison = canView ? comparison : null
            });
        }

        return View(model);
    }

    /// <summary>
    /// The 5C detail for one player: player, guardian and coach side by side, per category.
    /// </summary>
    public async Task<IActionResult> FiveCPlayer(int id, int? roundId, CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var round = await ResolveRoundAsync(roundId, cancellationToken);
        if (round is null)
        {
            return NotFound();
        }

        var comparison = await _fiveC.GetForPlayerAsync(
            round.Id,
            player.Id,
            player.Code,
            cancellationToken);

        var model = new FiveCPlayerViewModel
        {
            PlayerId = player.Id,
            Code = player.Code,
            TeamId = player.TeamId,
            TeamName = player.Team?.Name ?? string.Empty,
            Position = player.Position,
            Consent = await _consent.GetCurrentLevelAsync(player.Id, cancellationToken),
            RoundId = round.Id,
            RoundName = round.Name,
            RoundIsOpen = round.IsOpenAt(DateTimeOffset.UtcNow),
            Rounds = await RoundOptionsAsync(cancellationToken),
            Comparison = comparison,
            QuestionSetVersion = _catalog.Questions.Version,
            ShareLinks = BuildShareLinks(round.Id, player.Id, comparison)
        };

        return View(model);
    }

    /// <summary>
    /// Team overview for the older ten-statement form.
    /// TODO (Taavi): build TeamOverviewViewModel. The aggregate is only shown if
    /// CanViewTeamAggregate says yes -- and the policy requires the real number of
    /// responses, see <see cref="TeamAggregateResource"/>.
    /// </summary>
    public async Task<IActionResult> Team(int id, int? roundId)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id);
        if (team is null)
        {
            return NotFound();
        }

        var teamAllowed = await _authz.AuthorizeAsync(User, team, Policies.CanViewTeam);
        if (!teamAllowed.Succeeded)
        {
            return Forbid();
        }

        // TODO (Taavi): count the actual responses for the team in the round instead of 0.
        var responseCount = 0;

        var aggregateAllowed = await _authz.AuthorizeAsync(
            User,
            new TeamAggregateResource(team, responseCount),
            Policies.CanViewTeamAggregate);

        var model = new TeamOverviewViewModel
        {
            TeamId = team.Id,
            TeamName = team.Name
        };

        if (aggregateAllowed.Succeeded)
        {
            // TODO (Taavi): model.Aggregate = await _scoring.GetTeamAggregateAsync(...)
        }
        else
        {
            ViewData["AggregateMessage"] =
                "Too few responses to show a team average (at least " +
                $"{CanViewTeamAggregateRequirement.MinimumResponses} required).";
        }

        return View(model);
    }

    /// <summary>
    /// Player detail for the older ten-statement form.
    ///
    /// THIS IS THE PATTERN. Every action that takes a player id looks like this: fetch the
    /// player, ask the policy, return Forbid() on no. Do not check role or team by hand in
    /// the controller -- the rules live in CanViewPlayerHandler, in one place.
    /// </summary>
    public async Task<IActionResult> PlayerDetail(int id, int? roundId)
    {
        var player = await _db.Players
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (player is null)
        {
            return NotFound();
        }

        var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!authorized.Succeeded)
        {
            // Forbid, not NotFound: the user is signed in, they are just not allowed here.
            return Forbid();
        }

        var model = new PlayerDetailViewModel
        {
            PlayerId = player.Id,
            Code = player.Code,
            Position = player.Position,
            TeamName = player.Team?.Name ?? string.Empty,
            Consent = await _consent.GetCurrentLevelAsync(player.Id)
        };

        // TODO (Taavi): fetch the round, the statements and
        // model.Gap = await _scoring.GetPlayerGapAsync(roundId, player.Id);
        // The gap is calculated here and never stored.

        return View(model);
    }

    /// <summary>
    /// Search by player code.
    /// TODO (Taavi): hits have to be filtered through CanViewPlayer before they are shown --
    /// a search that confirms a player exists is also a disclosure.
    /// </summary>
    public IActionResult Search(string? q)
    {
        return View();
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The round to show: the one asked for, otherwise the open one, otherwise the most
    /// recent. Null only when no rounds exist at all.
    /// </summary>
    private async Task<SurveyRound?> ResolveRoundAsync(int? roundId, CancellationToken cancellationToken)
    {
        if (roundId is { } id)
        {
            return await _db.SurveyRounds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        var rounds = await _db.SurveyRounds
            .AsNoTracking()
            .OrderByDescending(r => r.ClosesAt)
            .ToListAsync(cancellationToken);

        return rounds.FirstOrDefault(r => r.IsOpenAt(now)) ?? rounds.FirstOrDefault();
    }

    private async Task<IReadOnlyList<FiveCTeamViewModel.RoundOption>> RoundOptionsAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return await _db.SurveyRounds
            .AsNoTracking()
            .OrderByDescending(r => r.ClosesAt)
            .Select(r => new FiveCTeamViewModel.RoundOption(
                r.Id,
                r.Name,
                r.OpensAt <= now && r.ClosesAt >= now))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Absolute links to the form, one per respondent role, for the coach to pass on.
    ///
    /// The link grants nothing. It preselects the round, the player and the role; whoever
    /// follows it signs in as themselves, and both CanViewPlayer and the role check run
    /// again on the server. That is what makes it safe to paste into a message -- and it is
    /// why there is no token in it. See docs/five-c.md.
    /// </summary>
    private IReadOnlyList<FiveCPlayerViewModel.ShareLink> BuildShareLinks(
        int roundId,
        int playerId,
        PlayerFiveCComparison comparison)
    {
        var roles = new (RespondentType Role, bool Answered)[]
        {
            (RespondentType.Player, comparison.PlayerHasAnswered),
            (RespondentType.Guardian, comparison.GuardianHasAnswered),
            (RespondentType.Coach, comparison.CoachHasAnswered)
        };

        return roles
            .Select(entry => new FiveCPlayerViewModel.ShareLink(
                entry.Role,
                Roles.DisplayName(entry.Role.ToString()),
                Url.Action(
                    "Fill",
                    "Survey",
                    new { roundId, playerId, role = entry.Role },
                    Request.Scheme) ?? string.Empty,
                entry.Answered))
            .ToList();
    }
}
