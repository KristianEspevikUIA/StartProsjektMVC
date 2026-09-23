using StartPraksisGruppe3Prosjekt.Models.Identity;

namespace StartPraksisGruppe3Prosjekt.ViewModels.Identity;

/// <summary>
/// The Identity Benchmarking page for one team: one match, or the average of all of them,
/// against the Gold Standard.
///
/// Every number here carries its sources. A marker the match reports cannot answer has no
/// value, no status and a reason instead -- see <see cref="MarkerReading.IsMeasured"/>.
/// </summary>
public sealed class IdentityBenchmarkViewModel
{
    public required GoldStandard GoldStandard { get; init; }

    public IReadOnlyList<TeamOption> Teams { get; init; } = Array.Empty<TeamOption>();

    /// <summary>"U14".</summary>
    public required string TeamKey { get; init; }

    /// <summary>"Start U14".</summary>
    public required string TeamName { get; init; }

    /// <summary>False when no match data has been loaded for the team. The rest is then empty.</summary>
    public bool HasData { get; init; }

    public IReadOnlyList<MatchOption> Matches { get; init; } = Array.Empty<MatchOption>();

    /// <summary>The match shown, or null for the average of all matches.</summary>
    public IdentityMatch? Match { get; init; }

    public bool IsAverage => Match is null;

    /// <summary>How many matches the numbers are drawn from: 1, or all of them.</summary>
    public int MatchCount { get; init; }

    /// <summary>The first and last match the team has data for.</summary>
    public DateOnly FirstMatchDate { get; init; }

    public DateOnly LastMatchDate { get; init; }

    /// <summary>E.g. "Start U14 6–2 Viking U14 · 13 Jun 2026", or "Average of 5 matches".</summary>
    public string SelectionLabel { get; init; } = string.Empty;

    public IReadOnlyList<PhaseReading> Phases { get; init; } = Array.Empty<PhaseReading>();

    public IReadOnlyList<IdentityInsight> Insights { get; init; } = Array.Empty<IdentityInsight>();

    public PlayerHighlight? Highlight { get; init; }

    /// <summary>
    /// Development over time, one group per phase that has a measured marker. Empty when there
    /// are fewer than two matches to draw a line through.
    /// </summary>
    public IReadOnlyList<PhaseTrends> Trends { get; init; } = Array.Empty<PhaseTrends>();

    public IReadOnlyList<StatusLegendEntry> Legend { get; init; } = Array.Empty<StatusLegendEntry>();

    public DateTimeOffset? DataGeneratedAt { get; init; }

    public IEnumerable<MarkerReading> AllReadings => Phases.SelectMany(p => p.Readings);
}

public sealed record TeamOption(string Key, string Label, bool HasData);

/// <summary>"all" is the average; anything else is a match id.</summary>
public sealed record MatchOption(string Value, string Label);

public sealed record PhaseReading(IdentityPhase Phase, IReadOnlyList<MarkerReading> Readings);

/// <summary>One row of the benchmark table.</summary>
public sealed record MarkerReading
{
    public required IdentityMarker Marker { get; init; }

    /// <summary>
    /// The value the status was worked out from: the match's own number, or the mean of all
    /// matches rounded to one decimal. Null when the marker is not measured.
    /// </summary>
    public double? Value { get; init; }

    /// <summary>E.g. "34%", "44.8%" or "36". Null when not measured.</summary>
    public string? DisplayValue { get; init; }

    public IdentityStatus? Status { get; init; }

    /// <summary>Every file and page the value was read from -- one per match.</summary>
    public IReadOnlyList<SourceReference> Sources { get; init; } = Array.Empty<SourceReference>();

    /// <summary>How the value was arrived at, in words.</summary>
    public string? Formula { get; init; }

    /// <summary>For the average only: in how many matches the value was inside or above the range.</summary>
    public int? MatchesAtOrAboveRange { get; init; }

    public bool IsMeasured => Value is not null;
}

/// <summary>
/// A generated line in Key Tactical Insights, e.g. ("Growth area", "Interceptions", "14 — 11 short
/// of the elite range of 25 – 56 / match."). Built only from values on the page.
/// </summary>
public sealed record IdentityInsight(string Label, string MarkerName, string Text, IdentityStatus Status);

/// <summary>
/// Who led Start on the two player-level markers. <see cref="Player"/> is set only when one
/// player led both -- the case the comparison document makes into a portrait.
/// </summary>
public sealed record PlayerHighlight
{
    public string? Player { get; init; }

    public required string Scope { get; init; }

    public IReadOnlyList<HighlightLeader> Leaders { get; init; } = Array.Empty<HighlightLeader>();

    public IReadOnlyList<SourceReference> Sources { get; init; } = Array.Empty<SourceReference>();
}

/// <summary>The leader(s) on one marker. Several names means a tie.</summary>
public sealed record HighlightLeader(string MarkerName, IReadOnlyList<string> Players, int Value);

/// <summary>The development charts of In Possession or Out of Possession, in the Gold Standard's order.</summary>
public sealed record PhaseTrends(IdentityPhase Phase, IReadOnlyList<MarkerTrend> Trends);

public sealed record MarkerTrend(IdentityMarker Marker, IReadOnlyList<TrendPoint> Points, double AxisMax);

public sealed record TrendPoint(
    string MatchId,
    DateOnly Date,
    string Label,
    double Value,
    string DisplayValue,
    IdentityStatus Status,
    SourceReference Source);

public sealed record StatusLegendEntry(IdentityStatus Status, string Label, string Description);
