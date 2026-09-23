using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;

namespace StartPraksisGruppe3Prosjekt.Services.Succession;

/// <summary>
/// Every number on the succession pages, worked out here and nowhere else: overall readiness,
/// what the coaches say together, where they disagree, how far off a player is and how long
/// that looks like taking, and the best eleven in a formation.
///
/// No database, no clock, no request. Everything it needs is passed in, so each rule can be
/// tested on its own -- and so the controller and the views have nothing left to calculate.
/// </summary>
public static class SuccessionMath
{
    // -----------------------------------------------------------------------------------
    // One coach
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Overall readiness for one coach's assessment: the average of the ratings the file lists.
    /// This is the workbook's =SUM(N:S)/6.
    ///
    /// With one difference, on purpose. SUM/6 counts a blank rating as 0, so a coach who had not
    /// got round to Availability yet made the player look a sixth worse than they are. Here a
    /// rating that is not given is left out rather than counted as zero -- and the form asks for
    /// all of them, so in practice the two agree. Null when there are no ratings at all.
    /// </summary>
    public static double? OverallOf(SuccessionAssessment assessment, SuccessionSettings settings)
    {
        var keys = settings.Ratings.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var values = assessment.Ratings
            .Where(r => keys.Contains(r.RatingKey))
            .Select(r => (double)r.Value)
            .ToList();

        return values.Count == 0 ? null : values.Average();
    }

    /// <summary>The three lights of the workbook's icon set, as a level.</summary>
    public static ReadinessLevel LevelOf(double? readiness, SuccessionSettings settings) => readiness switch
    {
        null => ReadinessLevel.None,
        var r when r >= settings.ReadyAt => ReadinessLevel.Ready,
        var r when r >= settings.DevelopingAt => ReadinessLevel.Developing,
        _ => ReadinessLevel.NotYet
    };

    // -----------------------------------------------------------------------------------
    // The coaches together
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// What the coaches who rated a player in one cycle say together.
    ///
    /// EVERY COACH COUNTS ONCE. Overall readiness is the average of each coach's own overall,
    /// not of every rating anybody gave -- the same rule as the 5C team average, which is an
    /// average of players and not of answers. Each rating is averaged the same way.
    ///
    /// Categories are a vote. Where no single answer has the most votes, there is no consensus
    /// and the page says the coaches are split instead of picking one.
    /// </summary>
    public static PlayerConsensus Consensus(
        IReadOnlyList<SuccessionAssessment> assessments,
        SuccessionSettings settings,
        Func<string?, int> riskSeverity)
    {
        var ratings = settings.Ratings
            .Select(rating =>
            {
                var values = assessments
                    .SelectMany(a => a.Ratings)
                    .Where(r => string.Equals(r.RatingKey, rating.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Value)
                    .ToList();

                return values.Count == 0
                    ? new RatingSummary(rating.Key, rating.Name, null, null, null, 0)
                    : new RatingSummary(rating.Key, rating.Name, values.Average(), values.Min(), values.Max(), values.Count);
            })
            .ToList();

        var overalls = assessments
            .Select(a => OverallOf(a, settings))
            .Where(o => o.HasValue)
            .Select(o => o!.Value)
            .ToList();

        var personal = assessments
            .Where(a => a.PersonalReadiness.HasValue)
            .Select(a => a.PersonalReadiness!.Value)
            .ToList();

        var risks = assessments
            .Select(a => a.SuccessionRisk)
            .Where(r => riskSeverity(r) >= 0)
            .ToList();

        var worstSeverity = risks.Count == 0 ? -1 : risks.Max(riskSeverity);
        var worstRisk = risks.FirstOrDefault(r => riskSeverity(r) == worstSeverity);

        return new PlayerConsensus
        {
            RaterCount = assessments.Count,
            Ratings = ratings,
            Overall = overalls.Count == 0 ? null : overalls.Average(),
            OverallSpread = overalls.Count < 2 ? null : overalls.Max() - overalls.Min(),
            PersonalReadiness = personal.Count == 0 ? null : personal.Average(),
            PersonalReadinessSpread = personal.Count < 2 ? null : personal.Max() - personal.Min(),
            AbilityCategory = VoteOn(assessments.Select(a => a.AbilityCategory)),
            RatedAs = VoteOn(assessments.Select(a => a.RatedAs)),
            Positions = PositionsOf(assessments),
            WorstRisk = worstRisk,
            WorstRiskCount = worstRisk is null
                ? 0
                : risks.Count(r => string.Equals(r, worstRisk, StringComparison.OrdinalIgnoreCase)),
            PathwayBlocked = YesCount(assessments.Select(a => a.PathwayBlocked)),
            ExternalNeeded = YesCount(assessments.Select(a => a.ExternalNeeded)),
            DisagreementAt = settings.DisagreementAt
        };
    }

    /// <summary>
    /// The positions the coaches named for a player, best first.
    ///
    /// A position counts at the BEST rank any coach gave it: if one coach has a player first at
    /// RB and another has them second, RB is a 1st position -- and the row says one of the two
    /// put it there. Ordered by that rank, then by how many coaches named it at all, then by how
    /// many put it first, then by key, so the same assessments always give the same order.
    /// </summary>
    public static IReadOnlyList<PositionPreference> PositionsOf(IEnumerable<SuccessionAssessment> assessments)
    {
        var named = new Dictionary<string, (int BestRank, int ListedBy, int FirstBy)>(StringComparer.OrdinalIgnoreCase);

        foreach (var assessment in assessments)
        {
            var positions = new[] { assessment.FirstPosition, assessment.SecondPosition, assessment.ThirdPosition };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < positions.Length; index++)
            {
                var key = positions[index]?.Trim();

                // The same position twice on one coach's row counts once, at its better rank.
                if (string.IsNullOrEmpty(key) || !seen.Add(key))
                {
                    continue;
                }

                var rank = index + 1;

                named[key] = named.TryGetValue(key, out var existing)
                    ? (Math.Min(existing.BestRank, rank), existing.ListedBy + 1, existing.FirstBy + (rank == 1 ? 1 : 0))
                    : (rank, 1, rank == 1 ? 1 : 0);
            }
        }

        return named
            .Select(entry => new PositionPreference(entry.Key, entry.Value.BestRank, entry.Value.ListedBy, entry.Value.FirstBy))
            .OrderBy(p => p.BestRank)
            .ThenByDescending(p => p.ListedBy)
            .ThenByDescending(p => p.FirstChoiceBy)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static CategoryVote VoteOn(IEnumerable<string?> answers)
    {
        var votes = answers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new Vote(g.Key, g.Count()))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Key, StringComparer.Ordinal)
            .ToList();

        if (votes.Count == 0)
        {
            return new CategoryVote(null, votes);
        }

        // A winner only when nobody else has as many votes. Two coaches saying Potential and
        // two saying Squad is a split, and picking the alphabetically first would hide it.
        var winner = votes.Count == 1 || votes[0].Count > votes[1].Count ? votes[0].Key : null;

        return new CategoryVote(winner, votes);
    }

    private static YesNo YesCount(IEnumerable<bool?> answers)
    {
        var given = answers.Where(a => a.HasValue).Select(a => a!.Value).ToList();

        return new YesNo(given.Count(a => a), given.Count);
    }

    // -----------------------------------------------------------------------------------
    // How far off, and how long
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// How far a player is from "ready", and how many weeks that looks like at the rate they
    /// have been moving -- "where the player is off, and how many weeks till they are fit".
    ///
    /// The rate is the least-squares slope through every cycle the player has been rated in,
    /// per week, so one unusual cycle moves it less than it would move a line through two
    /// points. It is a trend, not a promise: it needs two cycles to exist at all, it says
    /// "not closing" rather than inventing a date when the line is flat or falling, and past
    /// <see cref="SuccessionSettings.HorizonWeeks"/> it stops printing a number.
    /// </summary>
    /// <param name="history">Overall readiness per cycle, any order. One entry per cycle.</param>
    public static ReadinessOutlook Outlook(
        IReadOnlyList<(DateOnly CycleStartsOn, double Overall)> history,
        SuccessionSettings settings)
    {
        if (history.Count == 0)
        {
            return new ReadinessOutlook(OutlookKind.NoData, null, null, null, null);
        }

        var ordered = history.OrderBy(h => h.CycleStartsOn).ToList();
        var current = ordered[^1].Overall;
        var gap = Math.Max(0, settings.ReadyAt - current);

        double? perCycle = null;

        if (ordered.Count >= 2)
        {
            var first = ordered[0].CycleStartsOn.DayNumber;
            var points = ordered.Select(h => ((h.CycleStartsOn.DayNumber - first) / 7.0, h.Overall)).ToList();

            perCycle = SlopePerWeek(points) * settings.Cycle.LengthWeeks;
        }

        if (current >= settings.ReadyAt)
        {
            return new ReadinessOutlook(OutlookKind.ReadyNow, current, 0, perCycle, 0);
        }

        if (perCycle is null)
        {
            return new ReadinessOutlook(OutlookKind.NeedsMoreCycles, current, gap, null, null);
        }

        var perWeek = perCycle.Value / settings.Cycle.LengthWeeks;

        // A slope this small is flat for any purpose here: at 0.001 a week, one point on a
        // 1-10 scale takes twenty years.
        if (perWeek <= 0.001)
        {
            return new ReadinessOutlook(OutlookKind.NotClosing, current, gap, perCycle, null);
        }

        var weeks = (int)Math.Ceiling(gap / perWeek);

        return weeks > settings.HorizonWeeks
            ? new ReadinessOutlook(OutlookKind.BeyondHorizon, current, gap, perCycle, null)
            : new ReadinessOutlook(OutlookKind.Weeks, current, gap, perCycle, weeks);
    }

    private static double SlopePerWeek(IReadOnlyList<(double X, double Y)> points)
    {
        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);

        var numerator = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var denominator = points.Sum(p => (p.X - meanX) * (p.X - meanX));

        return denominator == 0 ? 0 : numerator / denominator;
    }

    // -----------------------------------------------------------------------------------
    // The best eleven
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The best eleven in a formation, from the players the coaches have rated, and who is next
    /// in line behind each of them -- "who's in the building to be the best fit".
    ///
    /// A player can fill a slot if any coach named its position among their three. Their FIT for
    /// the slot is their overall readiness, marked down by
    /// <see cref="SuccessionSettings.PositionRankPenalty"/> when it is their 2nd or 3rd position
    /// rather than their 1st: a natural right-back at 7.0 goes ahead of a centre-back covering
    /// there at 7.2.
    ///
    /// Filled strongest pairing first. Every (player, slot) pair is ranked by fit, and taken
    /// when both the slot and the player are still free -- so nobody plays twice, and a slot
    /// nobody was named for stays empty and says so instead of borrowing someone at random.
    /// Ties go to the higher overall, then the better rank, then the player code, so the same
    /// ratings always give the same team.
    /// </summary>
    public static IReadOnlyList<SlotResult> PickEleven(
        FormationDefinition formation,
        IReadOnlyList<ElevenCandidate> candidates,
        SuccessionSettings settings)
    {
        var slots = formation.Slots.ToList();

        var pairs = new List<(int Slot, SlotPick Pick)>();

        for (var slot = 0; slot < slots.Count; slot++)
        {
            foreach (var candidate in candidates)
            {
                if (PickFor(candidate, slots[slot], settings) is { } pick)
                {
                    pairs.Add((slot, pick));
                }
            }
        }

        var ordered = pairs
            .OrderByDescending(p => p.Pick.Fit)
            .ThenByDescending(p => p.Pick.Candidate.Overall)
            .ThenBy(p => p.Pick.Rank)
            .ThenBy(p => p.Pick.Candidate.Code, StringComparer.Ordinal)
            .ThenBy(p => p.Slot)
            .ToList();

        var starters = new SlotPick?[slots.Count];
        var placed = new Dictionary<int, int>(); // player id -> slot

        foreach (var (slot, pick) in ordered)
        {
            if (starters[slot] is not null || placed.ContainsKey(pick.Candidate.PlayerId))
            {
                continue;
            }

            starters[slot] = pick;
            placed[pick.Candidate.PlayerId] = slot;
        }

        return slots
            .Select((position, slot) =>
            {
                var starter = starters[slot];

                // Next in line: the best of the rest for THIS position, whether or not they start
                // somewhere else. That is the succession question -- who replaces the starter --
                // and a player starting at LB who is also the next right-back is worth knowing.
                var next = ordered
                    .Where(p => p.Slot == slot && p.Pick.Candidate.PlayerId != starter?.Candidate.PlayerId)
                    .Take(SuccessionRules.NextInLine)
                    .Select(p => new NextInLine(
                        p.Pick,
                        placed.TryGetValue(p.Pick.Candidate.PlayerId, out var elsewhere) ? slots[elsewhere] : null))
                    .ToList();

                return new SlotResult(slot, position, starter, next);
            })
            .ToList();
    }

    private static SlotPick? PickFor(ElevenCandidate candidate, string position, SuccessionSettings settings)
    {
        var preference = candidate.Positions.FirstOrDefault(p =>
            string.Equals(p.Key, position, StringComparison.OrdinalIgnoreCase));

        if (preference is null)
        {
            return null;
        }

        var penaltyIndex = Math.Clamp(preference.BestRank - 1, 0, settings.PositionRankPenalty.Count - 1);
        var penalty = settings.PositionRankPenalty.Count == 0 ? 0 : settings.PositionRankPenalty[penaltyIndex];

        return new SlotPick(candidate, preference.BestRank, candidate.Overall - penalty);
    }
}

// ---------------------------------------------------------------------------------------
// Results
// ---------------------------------------------------------------------------------------

/// <summary>The workbook's three lights, plus "nothing to light".</summary>
public enum ReadinessLevel
{
    None = 0,
    NotYet = 1,
    Developing = 2,
    Ready = 3
}

/// <summary>One rating across the coaches: the average, and how far apart they are.</summary>
public sealed record RatingSummary(string Key, string Name, double? Mean, int? Min, int? Max, int Count)
{
    /// <summary>Highest minus lowest. Null with fewer than two coaches -- one coach cannot disagree.</summary>
    public int? Spread => Count < 2 ? null : Max - Min;
}

public sealed record Vote(string Key, int Count);

/// <summary>A vote on a category. <see cref="Winner"/> is null when the coaches are split.</summary>
public sealed record CategoryVote(string? Winner, IReadOnlyList<Vote> Votes)
{
    public bool IsSplit => Winner is null && Votes.Count > 1;
}

public sealed record YesNo(int Yes, int Answered);

/// <summary>A position a player was named for: its best rank, and by how many coaches.</summary>
public sealed record PositionPreference(string Key, int BestRank, int ListedBy, int FirstChoiceBy);

/// <summary>What the coaches who rated a player in one cycle say together.</summary>
public sealed class PlayerConsensus
{
    public int RaterCount { get; init; }

    public IReadOnlyList<RatingSummary> Ratings { get; init; } = Array.Empty<RatingSummary>();

    /// <summary>The average of each coach's own overall readiness.</summary>
    public double? Overall { get; init; }

    public double? OverallSpread { get; init; }

    public double? PersonalReadiness { get; init; }

    public double? PersonalReadinessSpread { get; init; }

    public CategoryVote AbilityCategory { get; init; } = new(null, Array.Empty<Vote>());

    public CategoryVote RatedAs { get; init; } = new(null, Array.Empty<Vote>());

    public IReadOnlyList<PositionPreference> Positions { get; init; } = Array.Empty<PositionPreference>();

    /// <summary>The most severe risk any coach named.</summary>
    public string? WorstRisk { get; init; }

    public int WorstRiskCount { get; init; }

    public YesNo PathwayBlocked { get; init; } = new(0, 0);

    public YesNo ExternalNeeded { get; init; } = new(0, 0);

    public double DisagreementAt { get; init; }

    /// <summary>The ratings two coaches are at least <see cref="DisagreementAt"/> apart on.</summary>
    public IEnumerable<RatingSummary> Disagreements =>
        Ratings.Where(r => r.Spread is { } spread && spread >= DisagreementAt);

    /// <summary>Whether this player is worth talking about for the coaches' sake, not the player's.</summary>
    public bool CoachesDisagree =>
        Disagreements.Any()
        || PersonalReadinessSpread >= DisagreementAt
        || AbilityCategory.IsSplit;
}

public enum OutlookKind
{
    /// <summary>Never rated.</summary>
    NoData,

    /// <summary>At or above "ready" already.</summary>
    ReadyNow,

    /// <summary>Rated in one cycle only: a position, but no direction.</summary>
    NeedsMoreCycles,

    /// <summary>Flat or falling. No date to give.</summary>
    NotClosing,

    /// <summary>An estimate, in <see cref="ReadinessOutlook.Weeks"/>.</summary>
    Weeks,

    /// <summary>Closing, but too slowly for an estimate to mean anything.</summary>
    BeyondHorizon
}

/// <param name="Current">Overall readiness in the latest cycle.</param>
/// <param name="Gap">How far below "ready". 0 when ready.</param>
/// <param name="ChangePerCycle">The trend, per cycle. Null with one cycle.</param>
/// <param name="Weeks">Weeks until ready, when there is an estimate.</param>
public sealed record ReadinessOutlook(
    OutlookKind Kind,
    double? Current,
    double? Gap,
    double? ChangePerCycle,
    int? Weeks);

/// <summary>A player who could be picked, with what picking them is based on.</summary>
public sealed record ElevenCandidate(
    int PlayerId,
    string Code,
    double Overall,
    IReadOnlyList<PositionPreference> Positions);

/// <summary>A player in a slot. <see cref="Rank"/> is 1 when it is their 1st position.</summary>
public sealed record SlotPick(ElevenCandidate Candidate, int Rank, double Fit);

/// <summary>Next in line for a slot. <see cref="StartsAt"/> is where they start instead, if they do.</summary>
public sealed record NextInLine(SlotPick Pick, string? StartsAt);

public sealed record SlotResult(int Index, string Position, SlotPick? Starter, IReadOnlyList<NextInLine> NextInLine);
