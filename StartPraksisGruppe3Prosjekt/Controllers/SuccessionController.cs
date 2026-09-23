using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.Succession;
using StartPraksisGruppe3Prosjekt.ViewModels.Succession;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Owner: Kristian.
///
/// Succession planning: the coaches' workbook, as pages. Each coach rates the players they know
/// every eight weeks; this pulls the ratings together, puts the coaches side by side, picks the
/// best eleven in a formation and says how far off -- and how many weeks off -- each player is.
///
/// Coaches and administrators only, by role, and never a player or a guardian: these are the
/// staff's working judgements, not feedback. Only a COACH rates. An administrator sees
/// everything and can keep the contract details up to date, but a view on a player's ability
/// is a coach's to give, and an admin account that is also a coach has the coach role for it.
///
/// Every action that shows a player's ratings runs CanViewPlayer for them and writes the audit
/// log, the same as the 5C pages -- see CoachController.PlayerDetail for the pattern.
/// </summary>
[Authorize(Roles = Roles.Coach + "," + Roles.Admin)]
public class SuccessionController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly ISuccessionCatalog _catalog;
    private readonly ISuccessionPlanningService _planning;
    private readonly IPlayerAccessLog _accessLog;
    private readonly IPlayerWelcomeService _welcome;

    public SuccessionController(
        AppDbContext db,
        IAuthorizationService authz,
        ISuccessionCatalog catalog,
        ISuccessionPlanningService planning,
        IPlayerAccessLog accessLog,
        IPlayerWelcomeService welcome)
    {
        _db = db;
        _authz = authz;
        _catalog = catalog;
        _planning = planning;
        _accessLog = accessLog;
        _welcome = welcome;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private bool IsCoach => User.IsInRole(Roles.Coach);

    /// <summary>
    /// The board: every player in the pool, with what the coaches say about them in a cycle.
    /// </summary>
    /// <param name="cycle">"2026-08-17". Left out, the cycle today is in.</param>
    /// <param name="team">One team's id, or left out for the whole club.</param>
    /// <param name="sort">"code" (default), "readiness" or "team".</param>
    public async Task<IActionResult> Index(
        string? cycle,
        int? team,
        string? sort,
        CancellationToken cancellationToken)
    {
        var filter = await BuildFilterAsync(cycle, team, ratedAs: null, cancellationToken);
        if (filter is null)
        {
            return NotFound();
        }

        if (!await CanViewTeamAsync(filter.TeamId, cancellationToken))
        {
            return Forbid();
        }

        var board = await _planning.GetBoardAsync(filter.Cycle, filter.TeamId, null, cancellationToken);
        var rows = await VisibleAsync(board.Players);

        var sortKey = sort?.Trim().ToLowerInvariant() switch
        {
            "readiness" => "readiness",
            "team" => "team",
            _ => "code"
        };

        var ordered = sortKey switch
        {
            // Readiness is a ranking of minors, so it is a choice made on the page and never
            // the default -- the same call the 5C team page makes about sorting by difference.
            "readiness" => rows
                .OrderByDescending(r => r.Consensus?.Overall ?? double.MinValue)
                .ThenBy(r => r.Player.Code, StringComparer.Ordinal)
                .ToList(),
            "team" => rows
                .OrderBy(r => r.Player.Team?.Name, StringComparer.Ordinal)
                .ThenBy(r => r.Player.Code, StringComparer.Ordinal)
                .ToList(),
            _ => rows.OrderBy(r => r.Player.Code, StringComparer.Ordinal).ToList()
        };

        // A row with nothing but a code on it says nothing about the player; a row with ratings
        // or a contract does, and those are the ones logged.
        await _accessLog.RecordManyAsync(
            User,
            ordered.Where(r => r.Consensus is not null || r.Profile is not null).Select(r => r.Player.Id).ToList(),
            "Succession/Overview",
            cancellationToken);

        var today = Today;
        var currentRows = ordered.Where(r => !r.IsFromEarlierCycle && r.Consensus is not null).ToList();

        // One lookup for every coach on the board, so each row can say who is behind it.
        var raterNames = (await _planning.RaterNamesAsync(
                ordered.SelectMany(r => r.Raters).Select(r => r.RaterUserId),
                cancellationToken))
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Key == UserId ? "You" : entry.Value,
                StringComparer.Ordinal);

        var model = new SuccessionOverviewViewModel
        {
            Filter = filter,
            Sort = sortKey,
            Rows = ordered,
            Today = today,
            CanRate = IsCoach && filter.IsCurrentCycle,
            RaterNames = raterNames,
            RatedThisCycle = ordered.Count(r => r.RatersThisCycle.Count > 0),
            RatedByYou = ordered.Count(r => r.RatersThisCycle.Contains(UserId)),
            ReadyNow = ordered.Count(r => r.Level == ReadinessLevel.Ready),
            Developing = ordered.Count(r => r.Level == ReadinessLevel.Developing),
            Disagreements = currentRows.Where(r => r.Consensus!.CoachesDisagree).ToList(),
            ContractsEnding = ordered
                .Where(r => r.Profile?.ContractEndsOn is { } ends
                            && ends >= today
                            && ends.DayNumber - today.DayNumber <= SuccessionOverviewViewModel.ContractWarningDays)
                .OrderBy(r => r.Profile!.ContractEndsOn)
                .ToList()
        };

        return View(model);
    }

    /// <summary>
    /// The best eleven in a formation, from the players the coaches have rated, and who is next
    /// in line behind each of them.
    /// </summary>
    /// <param name="formation">"3-5-2" (or "1-3-5-2"). Left out, the first formation in the file.</param>
    /// <param name="ratedAs">Only assessments made against this level, e.g. "first-team".</param>
    public async Task<IActionResult> Formation(
        string? formation,
        string? cycle,
        int? team,
        string? ratedAs,
        CancellationToken cancellationToken)
    {
        var chosen = formation is null ? _catalog.DefaultFormation : _catalog.FindFormation(formation);
        if (chosen is null)
        {
            return NotFound();
        }

        var filter = await BuildFilterAsync(cycle, team, ratedAs, cancellationToken);
        if (filter is null)
        {
            return NotFound();
        }

        if (!await CanViewTeamAsync(filter.TeamId, cancellationToken))
        {
            return Forbid();
        }

        var board = await _planning.GetBoardAsync(filter.Cycle, filter.TeamId, filter.RatedAs, cancellationToken);
        var rows = (await VisibleAsync(board.Players))
            .Where(r => r.Consensus?.Overall is not null)
            .ToDictionary(r => r.Player.Id);

        var names = await DisplayNamesAsync(rows.Values, cancellationToken);

        var candidates = rows.Values
            .Select(r => new ElevenCandidate(r.Player.Id, r.Player.Code, r.Consensus!.Overall!.Value, r.Consensus.Positions))
            .ToList();

        var slots = SuccessionMath.PickEleven(chosen, candidates, _catalog.Settings);

        SlotPlayer ToSlotPlayer(SlotPick pick, string? startsAt)
        {
            var row = rows[pick.Candidate.PlayerId];

            return new SlotPlayer
            {
                PlayerId = row.Player.Id,
                Code = row.Player.Code,
                Name = names[row.Player.Id].Name,
                NameTag = names[row.Player.Id].Tag,
                TeamName = row.Player.Team?.Name,
                Overall = pick.Candidate.Overall,
                Fit = pick.Fit,
                Rank = pick.Rank,
                Level = row.Level,
                Outlook = row.Outlook,
                FromEarlierCycle = row.IsFromEarlierCycle,
                StartsAt = startsAt,
                RatedAs = row.Consensus!.RatedAs.Winner is { } level ? _catalog.Level(level)?.Name ?? level : null
            };
        }

        var views = slots
            .Select(slot => new SlotView
            {
                Position = slot.Position,
                PositionName = _catalog.Position(slot.Position)?.Name ?? slot.Position,
                Starter = slot.Starter is { } starter ? ToSlotPlayer(starter, null) : null,
                NextInLine = slot.NextInLine.Select(n => ToSlotPlayer(n.Pick, n.StartsAt)).ToList()
            })
            .ToList();

        // Back into the rows the file draws, in the same order the slots came out, and which
        // part of the team each slot is in.
        var lines = new List<IReadOnlyList<SlotView>>();
        var units = new List<TeamUnit>();
        var index = 0;
        foreach (var line in chosen.Lines)
        {
            lines.Add(views.Skip(index).Take(line.Count).ToList());
            units.AddRange(Enumerable.Repeat(SuccessionMath.UnitOf(lines.Count - 1, chosen.Lines.Count), line.Count));
            index += line.Count;
        }

        var starters = views.Where(v => v.Starter is not null).Select(v => v.Starter!).ToList();
        var starting = starters.Select(s => s.PlayerId).ToHashSet();

        // Everybody there is to choose from, not only the eleven: the substitutes beside the
        // pitch are where a coach brings players on from. Strongest first, as a squad list reads.
        var squad = rows.Values
            .OrderByDescending(r => r.Consensus!.Overall)
            .ThenBy(r => r.Player.Code, StringComparer.Ordinal)
            .Select(r => new SquadPlayer
            {
                PlayerId = r.Player.Id,
                Code = r.Player.Code,
                Name = names[r.Player.Id].Name,
                NameTag = names[r.Player.Id].Tag,
                TeamName = r.Player.Team?.Name,
                Overall = r.Consensus!.Overall!.Value,
                Level = r.Level,
                Positions = r.Consensus.Positions,
                FromEarlierCycle = r.IsFromEarlierCycle,
                Starts = starting.Contains(r.Player.Id)
            })
            .ToList();

        // The substitutes show every one of them with a number, so every one of them is logged --
        // not only the eleven and the next in line.
        await _accessLog.RecordManyAsync(
            User,
            squad.Select(p => p.PlayerId).ToList(),
            "Succession/Formation",
            cancellationToken);

        var fits = views.Select(v => v.Starter?.Fit).ToList();

        return View(new SuccessionFormationViewModel
        {
            Filter = filter,
            Formation = chosen,
            Formations = _catalog.Settings.Formations,
            Lines = lines,
            FilledCount = starters.Count,
            ReadyCount = starters.Count(s => s.Level == ReadinessLevel.Ready),
            TeamRating = Average(fits),
            Units = Enum.GetValues<TeamUnit>()
                .Where(units.Contains)
                .Select(unit => new UnitRating(unit, Average(fits.Where((_, slot) => units[slot] == unit))))
                .ToList(),
            CandidateCount = candidates.Count,
            Squad = squad,
            Editor = new LineupEditorData(
                views.Select((v, slot) => new LineupSlot(slot, v.Position, v.PositionName, units[slot].ToString())).ToList(),
                chosen.Lines.Select(line => line.Count).ToList(),
                squad.Select(p => new LineupPlayer(
                        p.PlayerId,
                        p.Code,
                        p.Name,
                        p.NameTag,
                        p.TeamName,
                        p.Overall,
                        SuccessionFormat.LevelName(p.Level),
                        // The file's own spelling of each key, so the script can match a slot
                        // by plain equality.
                        p.Positions.ToDictionary(pos => _catalog.Position(pos.Key)?.Key ?? pos.Key, pos => pos.BestRank),
                        p.FromEarlierCycle,
                        Url.Action(nameof(Player), new { id = p.PlayerId, cycle = filter.Cycle.Key }) ?? string.Empty))
                    .ToList(),
                views.Select(v => v.Starter?.PlayerId).ToList(),
                _catalog.Settings.PositionRankPenalty,
                _catalog.Settings.OutOfPositionPenalty,
                _catalog.Settings.ReadyAt,
                _catalog.Settings.DevelopingAt)
        });

        static double? Average(IEnumerable<double?> values)
        {
            var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();
            return present.Count == 0 ? null : present.Average();
        }
    }

    /// <summary>One player: each coach side by side, the history, the notes, the contract.</summary>
    public async Task<IActionResult> Player(int id, string? cycle, CancellationToken cancellationToken)
    {
        var (found, refusal) = await FindViewablePlayerAsync(id, cancellationToken);
        if (found is null)
        {
            return refusal!;
        }

        var filter = await BuildFilterAsync(cycle, team: null, ratedAs: null, cancellationToken);
        if (filter is null)
        {
            return NotFound();
        }

        var detail = await _planning.GetPlayerAsync(id, filter.Cycle, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        await _accessLog.RecordAsync(User, id, "Succession/Player", cancellationToken: cancellationToken);

        var names = await _planning.RaterNamesAsync(detail.Assessments.Select(a => a.RaterUserId), cancellationToken);

        // The signed-in coach first, so "you" is always the first column; the rest in the order
        // they rated.
        var raters = detail.Assessments
            .OrderBy(a => a.RaterUserId == UserId ? 0 : 1)
            .Select(a => new SuccessionPlayerViewModel.RaterColumn(
                a.RaterUserId == UserId ? "You" : names.GetValueOrDefault(a.RaterUserId, "Coach"),
                a.RaterUserId == UserId,
                a,
                SuccessionMath.OverallOf(a, _catalog.Settings)))
            .ToList();

        return View(new SuccessionPlayerViewModel
        {
            Detail = detail,
            Filter = filter,
            Raters = raters,
            CanRate = IsCoach && filter.IsCurrentCycle,
            HasOwn = raters.Any(r => r.IsYou),
            Profile = new SuccessionProfileForm
            {
                ContractType = detail.Profile?.ContractType,
                ContractEndsOn = detail.Profile?.ContractEndsOn,
                TrainingGroup = detail.Profile?.TrainingGroup
            }
        });
    }

    /// <summary>
    /// The rating form, for the current cycle. Starts from this cycle's assessment if there is
    /// one, otherwise from the coach's latest earlier one, otherwise blank.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = Roles.Coach)]
    public async Task<IActionResult> Rate(int id, CancellationToken cancellationToken)
    {
        var (found, refusal) = await FindViewablePlayerAsync(id, cancellationToken);
        if (found is null)
        {
            return refusal!;
        }

        var cycle = _catalog.CycleOf(Today);

        await _accessLog.RecordAsync(User, id, "Succession/Rate", cancellationToken: cancellationToken);

        var (current, earlier) = await _planning.GetOwnAsync(id, UserId, cycle, cancellationToken);

        var model = new SuccessionRateViewModel();

        if (current is not null)
        {
            model.CopyFrom(current);
            model.IsCorrection = true;
        }
        else if (earlier is not null)
        {
            model.CopyFrom(earlier);
            model.PrefilledFrom = _catalog.CycleOf(earlier.CycleStartsOn).Label;
        }

        return View(Describe(model, found, cycle));
    }

    /// <summary>
    /// Saves the coach's assessment for the current cycle. The cycle is today's, not a field in
    /// the form: a closed cycle is history, and a form cannot reopen it.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Coach)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> Rate(
        int id,
        SuccessionRateViewModel form,
        CancellationToken cancellationToken)
    {
        var (found, refusal) = await FindViewablePlayerAsync(id, cancellationToken);
        if (found is null)
        {
            return refusal!;
        }

        var cycle = _catalog.CycleOf(Today);

        ValidateRating(form);

        if (!ModelState.IsValid)
        {
            var (current, _) = await _planning.GetOwnAsync(id, UserId, cycle, cancellationToken);
            form.IsCorrection = current is not null;

            return View(Describe(form, found, cycle));
        }

        await _planning.SaveAsync(id, UserId, cycle, form.ToAssessment(_catalog.Settings), cancellationToken);

        TempData["SuccessionMessage"] = $"Your assessment of {found.Code} for {cycle.Label} is saved.";

        return RedirectToAction(nameof(Player), new { id });
    }

    /// <summary>Contract type, contract end and training group. Coaches and administrators.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> Profile(
        int id,
        [Bind(Prefix = nameof(SuccessionPlayerViewModel.Profile))] SuccessionProfileForm form,
        CancellationToken cancellationToken)
    {
        var (found, refusal) = await FindViewablePlayerAsync(id, cancellationToken);
        if (found is null)
        {
            return refusal!;
        }

        var contract = Blank(form.ContractType);
        var group = Blank(form.TrainingGroup);

        if (contract is not null && _catalog.ContractType(contract) is null)
        {
            return BadRequest();
        }

        if (group is not null && _catalog.Level(group) is null)
        {
            return BadRequest();
        }

        await _planning.SaveProfileAsync(
            id,
            _catalog.ContractType(contract)?.Key,
            form.ContractEndsOn,
            _catalog.Level(group)?.Key,
            UserId,
            cancellationToken);

        TempData["SuccessionMessage"] = $"Club details for {found.Code} are saved.";

        return RedirectToAction(nameof(Player), new { id });
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Everything a posted rating has to satisfy beyond its attributes. Every key has to be one
    /// the file knows -- the form offers nothing else, so anything else did not come from it.
    /// </summary>
    private void ValidateRating(SuccessionRateViewModel form)
    {
        var settings = _catalog.Settings;

        foreach (var rating in settings.Ratings)
        {
            var field = $"{nameof(SuccessionRateViewModel.Ratings)}[{rating.Key}]";

            if (!form.Ratings.TryGetValue(rating.Key, out var value) || value is null)
            {
                // All six, as the workbook's /6 assumes. A blank would make the overall an
                // average of fewer ratings than the next coach's.
                ModelState.AddModelError(field, $"Give {rating.Name} a rating from {settings.Scale.Min} to {settings.Scale.Max}.");
            }
            else if (value < settings.Scale.Min || value > settings.Scale.Max)
            {
                ModelState.AddModelError(field, $"{rating.Name} has to be from {settings.Scale.Min} to {settings.Scale.Max}.");
            }
        }

        var unknownRatings = form.Ratings.Keys
            .Where(key => settings.Ratings.All(r => !string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in unknownRatings)
        {
            ModelState.AddModelError(nameof(SuccessionRateViewModel.Ratings), $"'{key}' is not a rating.");
        }

        if (form.PersonalReadiness is { } personal && (personal < settings.Scale.Min || personal > settings.Scale.Max))
        {
            ModelState.AddModelError(nameof(form.PersonalReadiness), $"Has to be from {settings.Scale.Min} to {settings.Scale.Max}.");
        }

        CheckKey(form.RatedAs, _catalog.Level, nameof(form.RatedAs));
        CheckKey(form.AbilityCategory, _catalog.AbilityCategory, nameof(form.AbilityCategory));
        CheckKey(form.SuccessionRisk, _catalog.Risk, nameof(form.SuccessionRisk));
        CheckKey(form.FirstPosition, _catalog.Position, nameof(form.FirstPosition));
        CheckKey(form.SecondPosition, _catalog.Position, nameof(form.SecondPosition));
        CheckKey(form.ThirdPosition, _catalog.Position, nameof(form.ThirdPosition));

        var positions = new[] { form.FirstPosition, form.SecondPosition, form.ThirdPosition }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (positions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != positions.Count)
        {
            ModelState.AddModelError(nameof(form.SecondPosition), "Name each position once.");
        }

        if (string.IsNullOrWhiteSpace(form.FirstPosition) && positions.Count > 0)
        {
            ModelState.AddModelError(nameof(form.FirstPosition), "Start with the 1st position.");
        }
    }

    private void CheckKey(string? value, Func<string?, SuccessionOption?> lookup, string field)
    {
        if (!string.IsNullOrWhiteSpace(value) && lookup(value) is null)
        {
            ModelState.AddModelError(field, $"'{value}' is not in the list.");
        }
    }

    private static SuccessionRateViewModel Describe(
        SuccessionRateViewModel model,
        Models.Player player,
        SuccessionCycle cycle)
    {
        model.PlayerId = player.Id;
        model.Code = player.Code;
        model.TeamName = player.Team?.Name;
        model.CycleLabel = cycle.Label;
        return model;
    }

    /// <summary>
    /// What each player is called on the best eleven: the first name the club entered for the
    /// welcome, or the code where there is none. The only staff page that shows a name -- the
    /// coaches asked to see the team as a team, and a pitch of codes is not one.
    ///
    /// Two players with the same first name get their code as a tag under it, so a shirt can
    /// never be mistaken for the other one.
    /// </summary>
    private async Task<Dictionary<int, ShirtName>> DisplayNamesAsync(
        IEnumerable<BoardPlayer> rows,
        CancellationToken cancellationToken)
    {
        var players = rows.Select(r => r.Player).ToList();
        var firstNames = await _welcome.FirstNamesAsync(players.Select(p => p.Id).ToList(), cancellationToken);

        var shared = firstNames.Values
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return players.ToDictionary(
            p => p.Id,
            p => firstNames.TryGetValue(p.Id, out var name)
                ? new ShirtName(name, shared.Contains(name) ? p.Code : null)
                : new ShirtName(p.Code, null));
    }

    /// <summary>
    /// The player, or the response to give instead: 404 when there is no such player, 403 when
    /// CanViewPlayer says no. The pattern from CoachController.PlayerDetail, in one place.
    /// </summary>
    private async Task<(Models.Player? Player, IActionResult? Refusal)> FindViewablePlayerAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return (null, NotFound());
        }

        var allowed = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);

        return allowed.Succeeded ? (player, null) : (null, Forbid());
    }

    /// <summary>The rows this user may see, one CanViewPlayer check each -- as FiveCTeam does.</summary>
    private async Task<List<BoardPlayer>> VisibleAsync(IEnumerable<BoardPlayer> rows)
    {
        var visible = new List<BoardPlayer>();

        foreach (var row in rows)
        {
            if ((await _authz.AuthorizeAsync(User, row.Player, Policies.CanViewPlayer)).Succeeded)
            {
                visible.Add(row);
            }
        }

        return visible;
    }

    private async Task<bool> CanViewTeamAsync(int? teamId, CancellationToken cancellationToken)
    {
        if (teamId is null)
        {
            return true;
        }

        var team = await _db.Teams.AsNoTracking().FirstAsync(t => t.Id == teamId, cancellationToken);

        return (await _authz.AuthorizeAsync(User, team, Policies.CanViewTeam)).Succeeded;
    }

    /// <summary>
    /// The cycle, team and level to show. Null when the URL names a team that does not exist --
    /// a 404 rather than quietly showing the whole club under a heading that says otherwise. A
    /// cycle in the future is today's: there is nothing to show for it yet.
    /// </summary>
    private async Task<SuccessionFilterViewModel?> BuildFilterAsync(
        string? cycle,
        int? team,
        string? ratedAs,
        CancellationToken cancellationToken)
    {
        var current = _catalog.CycleOf(Today);
        var chosen = SuccessionCycle.TryParse(cycle, _catalog.Settings.Cycle) ?? current;

        if (chosen.StartsOn > current.StartsOn)
        {
            chosen = current;
        }

        var teams = await _db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new SuccessionFilterViewModel.TeamOption(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        if (team is { } teamId && teams.All(t => t.Id != teamId))
        {
            return null;
        }

        return new SuccessionFilterViewModel
        {
            Cycle = chosen,
            CurrentCycle = current,
            Cycles = await _planning.GetCyclesAsync(Today, cancellationToken),
            TeamId = team,
            Teams = teams,
            RatedAs = _catalog.Level(ratedAs)?.Key
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
