using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;

namespace StartPraksisGruppe3Prosjekt.Services.Succession;

/// <summary>
/// Reads and writes succession planning: the coaches' assessments and the player facts beside
/// them. All the arithmetic is in <see cref="SuccessionMath"/>; this is the part that talks to
/// the database.
///
/// No authorisation here. The controller has decided who may see what before it asks.
/// </summary>
public interface ISuccessionPlanningService
{
    /// <summary>
    /// Every player in the pool, with what the coaches say about them in a cycle -- the
    /// workbook, pulled together.
    ///
    /// A player nobody has rated in <paramref name="cycle"/> yet shows their most recent
    /// earlier cycle instead, marked as such. Without that the board would be empty on the
    /// first day of every cycle; with it, it is never a mix of two cycles for one player.
    /// </summary>
    /// <param name="teamId">One team, or null for the whole club.</param>
    /// <param name="ratedAs">Only assessments made against this level, or null for all.</param>
    Task<SuccessionBoard> GetBoardAsync(
        SuccessionCycle cycle,
        int? teamId,
        string? ratedAs,
        CancellationToken cancellationToken = default);

    /// <summary>One player: each coach side by side in a cycle, and the history across cycles.</summary>
    Task<SuccessionPlayerDetail?> GetPlayerAsync(
        int playerId,
        SuccessionCycle cycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// This coach's assessment of this player in this cycle, and their latest one before it --
    /// the form starts from the second when there is no first, so a coach re-rating every eight
    /// weeks changes what has moved instead of typing the row out again.
    /// </summary>
    Task<(SuccessionAssessment? Current, SuccessionAssessment? Earlier)> GetOwnAsync(
        int playerId,
        string raterUserId,
        SuccessionCycle cycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves this coach's assessment for the cycle. A second save in the same cycle replaces
    /// the first -- all of it, ratings included -- rather than adding to it.
    /// </summary>
    /// <param name="assessment">
    /// The values to store. Player, rater and cycle are taken from the other arguments, not
    /// from this, so a form cannot write one coach's view under another's name.
    /// </param>
    Task SaveAsync(
        int playerId,
        string raterUserId,
        SuccessionCycle cycle,
        SuccessionAssessment assessment,
        CancellationToken cancellationToken = default);

    /// <summary>Saves contract and training group for a player.</summary>
    Task SaveProfileAsync(
        int playerId,
        string? contractType,
        DateOnly? contractEndsOn,
        string? trainingGroup,
        string updatedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The cycles worth offering in a picker: the one <paramref name="today"/> is in, and every
    /// earlier one somebody rated in. Newest first.
    /// </summary>
    Task<IReadOnlyList<SuccessionCycle>> GetCyclesAsync(
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What to call each coach on the page: the part of their sign-in address before the @.
    /// Coaches are colleagues, and "Coach 2" would make comparing them pointless. A coach whose
    /// account is gone is "Former coach", not an id.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> RaterNamesAsync(
        IEnumerable<string> raterUserIds,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISuccessionPlanningService" />
public sealed class SuccessionPlanningService : ISuccessionPlanningService
{
    private readonly AppDbContext _db;
    private readonly ISuccessionCatalog _catalog;

    public SuccessionPlanningService(AppDbContext db, ISuccessionCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    private SuccessionSettings Settings => _catalog.Settings;

    /// <inheritdoc />
    public async Task<SuccessionBoard> GetBoardAsync(
        SuccessionCycle cycle,
        int? teamId,
        string? ratedAs,
        CancellationToken cancellationToken = default)
    {
        var players = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .Where(p => teamId == null || p.TeamId == teamId)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        var playerIds = players.Select(p => p.Id).ToList();

        // Everything up to and including the cycle shown: the history is what "weeks until
        // ready" is worked out from, and a later cycle has no business in an earlier view.
        var assessments = await _db.SuccessionAssessments
            .AsNoTracking()
            .Include(a => a.Ratings)
            .Where(a => playerIds.Contains(a.PlayerId) && a.CycleStartsOn <= cycle.StartsOn)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(ratedAs))
        {
            assessments = assessments
                .Where(a => string.Equals(a.RatedAs, ratedAs, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var byPlayer = assessments.ToLookup(a => a.PlayerId);

        var profiles = await _db.PlayerSuccessionProfiles
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, cancellationToken);

        var rows = players
            .Select(player => BuildRow(
                player,
                profiles.GetValueOrDefault(player.Id),
                byPlayer[player.Id].ToList(),
                cycle))
            .ToList();

        return new SuccessionBoard(cycle, rows);
    }

    private BoardPlayer BuildRow(
        Player player,
        PlayerSuccessionProfile? profile,
        IReadOnlyList<SuccessionAssessment> assessments,
        SuccessionCycle cycle)
    {
        var byCycle = assessments
            .GroupBy(a => a.CycleStartsOn)
            .OrderBy(g => g.Key)
            .Select(g => (StartsOn: g.Key, Consensus: Consensus(g.ToList())))
            .ToList();

        var history = byCycle
            .Where(c => c.Consensus.Overall.HasValue)
            .Select(c => (c.StartsOn, c.Consensus.Overall!.Value))
            .ToList();

        var latest = byCycle.Count == 0 ? ((DateOnly, PlayerConsensus)?)null : byCycle[^1];

        return new BoardPlayer
        {
            Player = player,
            Profile = profile,
            Consensus = latest?.Item2,
            ConsensusCycle = latest is { } l ? _catalog.CycleOf(l.Item1) : null,
            RatersThisCycle = assessments
                .Where(a => a.CycleStartsOn == cycle.StartsOn)
                .Select(a => a.RaterUserId)
                .ToList(),
            Outlook = SuccessionMath.Outlook(history, Settings),
            Level = SuccessionMath.LevelOf(latest?.Item2.Overall, Settings)
        };
    }

    /// <inheritdoc />
    public async Task<SuccessionPlayerDetail?> GetPlayerAsync(
        int playerId,
        SuccessionCycle cycle,
        CancellationToken cancellationToken = default)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
        {
            return null;
        }

        var assessments = await _db.SuccessionAssessments
            .AsNoTracking()
            .Include(a => a.Ratings)
            .Where(a => a.PlayerId == playerId && a.CycleStartsOn <= cycle.StartsOn)
            .OrderBy(a => a.CycleStartsOn)
            .ThenBy(a => a.UpdatedAt)
            .ToListAsync(cancellationToken);

        var inCycle = assessments.Where(a => a.CycleStartsOn == cycle.StartsOn).ToList();

        var history = assessments
            .GroupBy(a => a.CycleStartsOn)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var consensus = Consensus(g.ToList());

                return new CycleHistoryPoint(
                    _catalog.CycleOf(g.Key),
                    consensus.Overall,
                    consensus.PersonalReadiness,
                    g.Count(),
                    SuccessionMath.LevelOf(consensus.Overall, Settings));
            })
            .ToList();

        var outlook = SuccessionMath.Outlook(
            history
                .Where(h => h.Overall.HasValue)
                .Select(h => (h.Cycle.StartsOn, h.Overall!.Value))
                .ToList(),
            Settings);

        var profile = await _db.PlayerSuccessionProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        return new SuccessionPlayerDetail
        {
            Player = player,
            Profile = profile,
            Cycle = cycle,
            Assessments = inCycle,
            Consensus = inCycle.Count == 0 ? null : Consensus(inCycle),
            History = history,
            Outlook = outlook
        };
    }

    /// <inheritdoc />
    public async Task<(SuccessionAssessment? Current, SuccessionAssessment? Earlier)> GetOwnAsync(
        int playerId,
        string raterUserId,
        SuccessionCycle cycle,
        CancellationToken cancellationToken = default)
    {
        var own = await _db.SuccessionAssessments
            .AsNoTracking()
            .Include(a => a.Ratings)
            .Where(a => a.PlayerId == playerId
                        && a.RaterUserId == raterUserId
                        && a.CycleStartsOn <= cycle.StartsOn)
            .OrderByDescending(a => a.CycleStartsOn)
            .Take(2)
            .ToListAsync(cancellationToken);

        var current = own.FirstOrDefault(a => a.CycleStartsOn == cycle.StartsOn);
        var earlier = own.FirstOrDefault(a => a.CycleStartsOn < cycle.StartsOn);

        return (current, earlier);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        int playerId,
        string raterUserId,
        SuccessionCycle cycle,
        SuccessionAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        var row = await FindOwnRowAsync(playerId, raterUserId, cycle, cancellationToken);

        if (row is null)
        {
            row = new SuccessionAssessment
            {
                PlayerId = playerId,
                RaterUserId = raterUserId,
                CycleStartsOn = cycle.StartsOn
            };

            _db.SuccessionAssessments.Add(row);
        }

        Apply(assessment, row);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The same coach saved the same player twice at once -- two tabs, or a double
            // click. The index held; the row that won is the one to correct. Same handling as
            // EfSurveySubmissionStore: what was staged for the insert is cleared first, or the
            // retry would insert it again into the same index.
            _db.ChangeTracker.Clear();

            var winner = await FindOwnRowAsync(playerId, raterUserId, cycle, cancellationToken);

            if (winner is null)
            {
                // No row to correct, so it was some other index refusing. Let it be seen.
                throw;
            }

            Apply(assessment, winner);

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private Task<SuccessionAssessment?> FindOwnRowAsync(
        int playerId,
        string raterUserId,
        SuccessionCycle cycle,
        CancellationToken cancellationToken) =>
        _db.SuccessionAssessments
            .Include(a => a.Ratings)
            .FirstOrDefaultAsync(
                a => a.PlayerId == playerId
                     && a.RaterUserId == raterUserId
                     && a.CycleStartsOn == cycle.StartsOn,
                cancellationToken);

    /// <summary>
    /// Writes the submitted values onto a row, new or existing. A wholesale replace, not a
    /// merge: a rating or a note the coach has cleared is cleared, rather than surviving from
    /// the version before.
    /// </summary>
    private void Apply(SuccessionAssessment source, SuccessionAssessment row)
    {
        _db.SuccessionRatings.RemoveRange(row.Ratings);
        row.Ratings.Clear();

        foreach (var rating in source.Ratings)
        {
            row.Ratings.Add(new SuccessionRating { RatingKey = rating.RatingKey, Value = rating.Value });
        }

        row.CatalogVersion = Settings.Version;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.RatedAs = source.RatedAs;
        row.AbilityCategory = source.AbilityCategory;
        row.FirstPosition = source.FirstPosition;
        row.SecondPosition = source.SecondPosition;
        row.ThirdPosition = source.ThirdPosition;
        row.PersonalReadiness = source.PersonalReadiness;
        row.Projection0To6Months = source.Projection0To6Months;
        row.Projection6To18Months = source.Projection6To18Months;
        row.Projection18To36Months = source.Projection18To36Months;
        row.PathwayBlocked = source.PathwayBlocked;
        row.WhatNow = source.WhatNow;
        row.SuccessionRisk = source.SuccessionRisk;
        row.ExternalNeeded = source.ExternalNeeded;
        row.KeyDevelopmentFocus = source.KeyDevelopmentFocus;
        row.SuperStrengths = source.SuperStrengths;
        row.Notes = source.Notes;
    }

    /// <inheritdoc />
    public async Task SaveProfileAsync(
        int playerId,
        string? contractType,
        DateOnly? contractEndsOn,
        string? trainingGroup,
        string updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.PlayerSuccessionProfiles
            .FirstOrDefaultAsync(p => p.PlayerId == playerId, cancellationToken);

        if (profile is null)
        {
            profile = new PlayerSuccessionProfile { PlayerId = playerId };
            _db.PlayerSuccessionProfiles.Add(profile);
        }

        profile.ContractType = contractType;
        profile.ContractEndsOn = contractEndsOn;
        profile.TrainingGroup = trainingGroup;
        profile.UpdatedByUserId = updatedByUserId;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SuccessionCycle>> GetCyclesAsync(
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var current = _catalog.CycleOf(today);

        var rated = await _db.SuccessionAssessments
            .AsNoTracking()
            .Where(a => a.CycleStartsOn <= current.StartsOn)
            .Select(a => a.CycleStartsOn)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rated
            .Select(_catalog.CycleOf)
            .Append(current)
            .DistinctBy(c => c.StartsOn)
            .OrderByDescending(c => c.StartsOn)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> RaterNamesAsync(
        IEnumerable<string> raterUserIds,
        CancellationToken cancellationToken = default)
    {
        var ids = raterUserIds.Distinct(StringComparer.Ordinal).ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.UserName })
            .ToListAsync(cancellationToken);

        var names = users.ToDictionary(
            u => u.Id,
            u => LocalPart(u.Email ?? u.UserName) ?? "Coach",
            StringComparer.Ordinal);

        foreach (var id in ids.Where(id => !names.ContainsKey(id)))
        {
            names[id] = "Former coach";
        }

        return names;
    }

    private static string? LocalPart(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var at = address.IndexOf('@');

        return at > 0 ? address[..at] : address;
    }

    private PlayerConsensus Consensus(IReadOnlyList<SuccessionAssessment> assessments) =>
        SuccessionMath.Consensus(assessments, Settings, _catalog.RiskSeverity);

    /// <summary>SQLITE_CONSTRAINT_UNIQUE. Same test as EfSurveySubmissionStore.</summary>
    private const int SqliteUniqueViolation = 2067;

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
            SqliteException sqlite => sqlite.SqliteExtendedErrorCode == SqliteUniqueViolation,
            _ => false
        };
}

// ---------------------------------------------------------------------------------------
// Results
// ---------------------------------------------------------------------------------------

/// <summary>The pool of players in one cycle.</summary>
public sealed record SuccessionBoard(SuccessionCycle Cycle, IReadOnlyList<BoardPlayer> Players);

/// <summary>One player on the board.</summary>
public sealed class BoardPlayer
{
    public required Player Player { get; init; }

    public PlayerSuccessionProfile? Profile { get; init; }

    /// <summary>What the coaches say, in <see cref="ConsensusCycle"/>. Null when never rated.</summary>
    public PlayerConsensus? Consensus { get; init; }

    /// <summary>The cycle <see cref="Consensus"/> is from: the one shown, or the latest before it.</summary>
    public SuccessionCycle? ConsensusCycle { get; init; }

    /// <summary>The coaches who have rated the player in the cycle shown. Empty means it is older.</summary>
    public IReadOnlyCollection<string> RatersThisCycle { get; init; } = Array.Empty<string>();

    public bool IsFromEarlierCycle => Consensus is not null && RatersThisCycle.Count == 0;

    public required ReadinessOutlook Outlook { get; init; }

    public ReadinessLevel Level { get; init; }
}

/// <summary>One player on their own page.</summary>
public sealed class SuccessionPlayerDetail
{
    public required Player Player { get; init; }

    public PlayerSuccessionProfile? Profile { get; init; }

    public required SuccessionCycle Cycle { get; init; }

    /// <summary>Each coach's assessment in the cycle, oldest first.</summary>
    public IReadOnlyList<SuccessionAssessment> Assessments { get; init; } = Array.Empty<SuccessionAssessment>();

    public PlayerConsensus? Consensus { get; init; }

    /// <summary>Every cycle up to this one that anybody rated the player in, oldest first.</summary>
    public IReadOnlyList<CycleHistoryPoint> History { get; init; } = Array.Empty<CycleHistoryPoint>();

    public required ReadinessOutlook Outlook { get; init; }
}

public sealed record CycleHistoryPoint(
    SuccessionCycle Cycle,
    double? Overall,
    double? PersonalReadiness,
    int Raters,
    ReadinessLevel Level);
