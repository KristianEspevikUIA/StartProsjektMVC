namespace StartPraksisGruppe3Prosjekt.ViewModels.Identity;

/// <summary>The black Gold Standard band at the top of both Identity pages.</summary>
public sealed record IdentityHeader(string Eyebrow, string Title, string Organisation, string Methodology);

/// <summary>One of the two benchmark tables. <see cref="ValueHeading"/> is the team, e.g. "U14".</summary>
public sealed record PhaseTableModel(PhaseReading Reading, string ValueHeading, bool IsAverage, int MatchCount);

/// <summary>One development chart. The selected match, if any, is ringed.</summary>
public sealed record TrendChartModel(MarkerTrend Trend, string TeamKey, string? SelectedMatchId);

/// <summary>
/// One of the three Identity benchmark pages, as a button. <see cref="Action"/> is the action
/// on IdentityController, and the name of its view.
/// </summary>
public sealed record IdentitySection(string Action, string Label)
{
    /// <summary>In the order of the buttons.</summary>
    public static IReadOnlyList<IdentitySection> All { get; } = new[]
    {
        new IdentitySection("Index", "Overview"),
        new IdentitySection("Insights", "Key Insights"),
        new IdentitySection("Development", "Development over time")
    };
}
