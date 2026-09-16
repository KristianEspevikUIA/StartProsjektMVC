namespace StartPraksisGruppe3Prosjekt.ViewModels.Identity;

/// <summary>The black Gold Standard band at the top of both Identity pages.</summary>
public sealed record IdentityHeader(string Eyebrow, string Title, string Organisation, string Methodology);

/// <summary>One of the two benchmark tables. <see cref="ValueHeading"/> is the team, e.g. "U14".</summary>
public sealed record PhaseTableModel(PhaseReading Reading, string ValueHeading, bool IsAverage, int MatchCount);

/// <summary>One development chart. The selected match, if any, is ringed.</summary>
public sealed record TrendChartModel(MarkerTrend Trend, string TeamKey, string? SelectedMatchId);
