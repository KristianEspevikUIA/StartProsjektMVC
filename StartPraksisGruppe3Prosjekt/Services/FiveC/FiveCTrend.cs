namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// One player's own scores across several periods -- "Commitment went from 2.1 to 3.4".
///
/// Only the PLAYER's own answers are tracked here, not the coach's and not the guardian's.
/// Development over time is the player's development; a line that mixed in what other people
/// thought of them would move when the coach changed their mind, which is a different thing
/// and would be read as the same thing.
///
/// Nothing is stored. Like every other number in the 5C picture, it is recalculated from the
/// raw answers each time it is asked for.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerCode">Player code. Codes, not names.</param>
/// <param name="Periods">The periods, oldest first. Every category lines up with this list.</param>
/// <param name="Categories">One line per C.</param>
public sealed record PlayerTrend(
    int PlayerId,
    string PlayerCode,
    IReadOnlyList<TrendPeriod> Periods,
    IReadOnlyList<CategoryTrend> Categories)
{
    /// <summary>
    /// True when there is more than one period with answers in it. With one, there is a
    /// score but no development, and the page should say so rather than draw a flat line.
    /// </summary>
    public bool HasComparablePeriods =>
        Categories.Any(c => c.Means.Count(m => m.HasValue) > 1);

    /// <summary>The categories that moved most, largest change first. Empty when nothing moved.</summary>
    public IReadOnlyList<CategoryTrend> BiggestMovers =>
        Categories
            .Where(c => c.Change.HasValue)
            .OrderByDescending(c => Math.Abs(c.Change!.Value))
            .ToList();
}

/// <param name="RoundId">The period.</param>
/// <param name="Name">Period name, e.g. "Autumn 2026".</param>
/// <param name="ClosesAt">When it closed or closes. What the periods are ordered by.</param>
public sealed record TrendPeriod(int RoundId, string Name, DateTimeOffset ClosesAt);

/// <summary>
/// One C over time.
/// </summary>
/// <param name="CategoryKey">Category key, e.g. "commitment".</param>
/// <param name="CategoryName">Heading, e.g. "Commitment".</param>
/// <param name="Means">
/// The player's average per period, aligned with <see cref="PlayerTrend.Periods"/>. Null
/// where the player did not answer that period -- kept in place rather than dropped, so the
/// line does not silently close a gap the player left.
/// </param>
public sealed record CategoryTrend(
    string CategoryKey,
    string CategoryName,
    IReadOnlyList<double?> Means)
{
    /// <summary>The first period the player answered this category in.</summary>
    public double? First => Means.FirstOrDefault(m => m.HasValue);

    /// <summary>The most recent period they answered it in.</summary>
    public double? Latest => Means.LastOrDefault(m => m.HasValue);

    /// <summary>
    /// Latest minus first. Positive is improvement, because scores are stored after
    /// reversal and a higher score always means better.
    ///
    /// Null when fewer than two periods have an answer -- one measurement is a position,
    /// not a direction.
    /// </summary>
    public double? Change =>
        Means.Count(m => m.HasValue) > 1 && First is { } first && Latest is { } latest
            ? latest - first
            : null;

    /// <summary>
    /// Whether the change is big enough to be worth a sentence. Half a point on a five
    /// point scale, the same threshold the difference scores treat as "some difference" --
    /// below it, two measurements of the same player are not saying different things.
    /// </summary>
    public bool HasMoved => Change is { } change && Math.Abs(change) >= 0.5;

    /// <summary>Points on the line, with their period index. Gaps are left out.</summary>
    public IEnumerable<(int Index, double Mean)> Points =>
        Means.Select((mean, index) => (Index: index, Mean: mean))
             .Where(p => p.Mean.HasValue)
             .Select(p => (p.Index, p.Mean!.Value));
}
