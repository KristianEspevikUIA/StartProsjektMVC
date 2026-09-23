using StartPraksisGruppe3Prosjekt.Models.Identity;
using StartPraksisGruppe3Prosjekt.ViewModels.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

public interface IIdentityBenchmarkBuilder
{
    /// <summary>
    /// The page for a team and a match. <paramref name="teamKey"/> null picks the first team with
    /// data; <paramref name="matchId"/> null or <see cref="IdentityBenchmarkBuilder.AllMatches"/>
    /// is the average of every match. Null when the team or the match does not exist.
    /// </summary>
    IdentityBenchmarkViewModel? Build(string? teamKey, string? matchId);
}

/// <summary>
/// Puts a team's matches next to the Gold Standard. Holds no state; everything comes from the
/// catalog, which read it once at startup.
///
/// Two rules decide what a number on the page is:
///
///   * A marker without a metric is Not measured. It gets no value, no status, and never a
///     different number standing in for it.
///   * The average is the plain mean of the per-match values, rounded to one decimal, and the
///     status is worked out from that rounded number -- the one printed next to it. Otherwise a
///     mean of 57.96 would print as 58.0 and still say Strong Alignment.
/// </summary>
public sealed class IdentityBenchmarkBuilder : IIdentityBenchmarkBuilder
{
    public const string AllMatches = "all";

    private readonly IIdentityCatalog _catalog;

    public IdentityBenchmarkBuilder(IIdentityCatalog catalog)
    {
        _catalog = catalog;
    }

    public IdentityBenchmarkViewModel? Build(string? teamKey, string? matchId)
    {
        var standard = _catalog.GoldStandard;

        var team = string.IsNullOrWhiteSpace(teamKey)
            ? _catalog.Teams.FirstOrDefault(t => t.HasData) ?? _catalog.Teams.FirstOrDefault()
            : _catalog.FindTeam(teamKey);

        var teams = _catalog.Teams.Select(t => new TeamOption(t.Key, t.DisplayName, t.HasData)).ToList();

        var legend = IdentityStatuses.All
            .Select(s => new StatusLegendEntry(s, IdentityStatuses.Label(s), IdentityStatusRules.Describe(s, standard.StatusRules)))
            .ToList();

        if (team is null)
        {
            // No teams configured at all is a page with a message. A team asked for by name
            // that does not exist is a 404.
            return string.IsNullOrWhiteSpace(teamKey)
                ? new IdentityBenchmarkViewModel
                {
                    GoldStandard = standard, TeamKey = string.Empty, TeamName = string.Empty, Legend = legend
                }
                : null;
        }

        if (!team.HasData)
        {
            if (!string.IsNullOrWhiteSpace(matchId) && !IsAll(matchId))
            {
                return null;
            }

            return new IdentityBenchmarkViewModel
            {
                GoldStandard = standard,
                Teams = teams,
                TeamKey = team.Key,
                TeamName = team.DisplayName,
                Legend = legend
            };
        }

        var matches = team.Data!.Matches;

        IdentityMatch? match = null;
        if (!string.IsNullOrWhiteSpace(matchId) && !IsAll(matchId))
        {
            match = matches.FirstOrDefault(m => string.Equals(m.Id, matchId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }
        }

        IReadOnlyList<IdentityMatch> selected = match is null ? matches : new[] { match };
        var isAverage = match is null;

        var phases = standard.Phases
            .Select(phase => new PhaseReading(
                phase,
                phase.Markers.Select(marker => Read(marker, selected, isAverage, standard.StatusRules)).ToList()))
            .ToList();

        var readings = phases.SelectMany(p => p.Readings).ToList();

        return new IdentityBenchmarkViewModel
        {
            GoldStandard = standard,
            Teams = teams,
            TeamKey = team.Key,
            TeamName = team.DisplayName,
            HasData = true,
            Matches = new[] { new MatchOption(AllMatches, $"All matches (average of {matches.Count})") }
                .Concat(matches.Select(m => new MatchOption(m.Id, MatchLabel(m))))
                .ToList(),
            Match = match,
            MatchCount = selected.Count,
            FirstMatchDate = matches.Min(m => m.Date),
            LastMatchDate = matches.Max(m => m.Date),
            SelectionLabel = match is null ? $"Average of {matches.Count} matches" : MatchLabel(match),
            Phases = phases,
            Insights = IdentityInsightsBuilder.Build(readings, selected),
            Highlight = BuildHighlight(standard, selected, isAverage),
            Trends = BuildTrends(standard, matches),
            Legend = legend,
            DataGeneratedAt = team.Data.GeneratedAt
        };
    }

    private static bool IsAll(string matchId) =>
        string.Equals(matchId, AllMatches, StringComparison.OrdinalIgnoreCase);

    /// <summary>"13 Jun 2026 · Start U14 6–2 Viking U14". Home team first, as in the report.</summary>
    public static string MatchLabel(IdentityMatch match)
    {
        var homeGoals = match.StartIsHome ? match.Goals.Start : match.Goals.Opponent;
        var awayGoals = match.StartIsHome ? match.Goals.Opponent : match.Goals.Start;

        return $"{IdentityFormat.Date(match.Date)} · {match.HomeTeam} {homeGoals}–{awayGoals} {match.AwayTeam}";
    }

    private static MarkerReading Read(
        IdentityMarker marker,
        IReadOnlyList<IdentityMatch> matches,
        bool isAverage,
        StatusRules rules)
    {
        if (!marker.Measurement.IsMeasured)
        {
            return new MarkerReading { Marker = marker };
        }

        // Present in every match: the catalog refuses a file where it is not.
        var metrics = matches.Select(m => m.Metrics[marker.Measurement.Metric!]).ToList();

        var value = isAverage
            ? Math.Round(metrics.Average(m => m.Value), 1, MidpointRounding.AwayFromZero)
            : metrics[0].Value;

        var formulas = metrics.Select(m => m.Formula).Distinct().ToList();
        var perMatch = string.Join(" ", formulas);

        return new MarkerReading
        {
            Marker = marker,
            Value = value,
            DisplayValue = IdentityFormat.Value(value, marker.Target.Unit, isAverage),
            Status = IdentityStatusRules.Evaluate(marker, value, rules),
            Sources = metrics.Select(m => m.Source).ToList(),
            Formula = isAverage
                ? $"Mean of the {metrics.Count} per-match values, rounded to one decimal. Per match: {perMatch}"
                : perMatch,
            MatchesAtOrAboveRange = isAverage
                ? metrics.Count(m => IdentityStatusRules.Evaluate(marker, m.Value, rules) >= IdentityStatus.EliteAlignment)
                : null
        };
    }

    /// <summary>
    /// Who led Start in Successful Dribbles and Interceptions: in the match, or in total across
    /// every match. A marker nobody scored on has no leader, and with no leaders there is no
    /// highlight at all.
    /// </summary>
    private static PlayerHighlight? BuildHighlight(
        GoldStandard standard,
        IReadOnlyList<IdentityMatch> matches,
        bool isAverage)
    {
        var leaders = new List<HighlightLeader>();

        foreach (var metric in new[] { IdentityFormat.SuccessfulDribbles, IdentityFormat.Interceptions })
        {
            var marker = standard.AllMarkers.FirstOrDefault(m => m.Measurement.Metric == metric);
            if (marker is null)
            {
                continue;
            }

            var totals = matches
                .SelectMany(m => m.Players)
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .Select(g => (Name: g.Key, Value: g.Sum(p => IdentityFormat.PlayerValue(p, metric) ?? 0)))
                .ToList();

            var best = totals.Count == 0 ? 0 : totals.Max(t => t.Value);
            if (best <= 0)
            {
                continue;
            }

            leaders.Add(new HighlightLeader(
                marker.Name,
                totals.Where(t => t.Value == best).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                best));
        }

        if (leaders.Count == 0)
        {
            return null;
        }

        // A portrait only when it is one person on every marker. A tie is not "the" player.
        var singles = leaders.Select(l => l.Players).Where(p => p.Count == 1).Select(p => p[0]).Distinct().ToList();
        var player = leaders.Count > 1 && leaders.All(l => l.Players.Count == 1) && singles.Count == 1
            ? singles[0]
            : null;

        return new PlayerHighlight
        {
            Player = player,
            Scope = isAverage ? $"Totals across {matches.Count} matches" : "This match",
            Leaders = leaders,
            Sources = matches.Select(m => m.PlayersSource).ToList()
        };
    }

    /// <summary>
    /// One line per measured marker across every match, oldest first. Always the whole season,
    /// whatever is selected above -- the point is to see the selected match in context.
    /// </summary>
    private static IReadOnlyList<MarkerTrend> BuildTrends(GoldStandard standard, IReadOnlyList<IdentityMatch> matches)
    {
        if (matches.Count < 2)
        {
            return Array.Empty<MarkerTrend>();
        }

        var ordered = matches.OrderBy(m => m.Date).ToList();

        return standard.AllMarkers
            .Where(marker => marker.Measurement.IsMeasured)
            .Select(marker =>
            {
                var points = ordered
                    .Select(m =>
                    {
                        var metric = m.Metrics[marker.Measurement.Metric!];
                        return new TrendPoint(
                            m.Id,
                            m.Date,
                            $"{IdentityFormat.Date(m.Date)} · {(m.StartIsHome ? "vs" : "at")} {m.Opponent}",
                            metric.Value,
                            IdentityFormat.Value(metric.Value, marker.Target.Unit, isAverage: false),
                            IdentityStatusRules.Evaluate(marker, metric.Value, standard.StatusRules),
                            metric.Source);
                    })
                    .ToList();

                return new MarkerTrend(marker, points, AxisMax(marker, points));
            })
            .ToList();
    }

    /// <summary>
    /// The top of the chart. Percentages always run to 100; counts to the next ten above both the
    /// highest value and the top of the Elite range. The range has to fit: a line that fills the
    /// chart with the target band off the top would look like a team at the ceiling.
    /// </summary>
    private static double AxisMax(IdentityMarker marker, IReadOnlyList<TrendPoint> points)
    {
        if (marker.Target.Unit == "%")
        {
            return 100;
        }

        var highest = Math.Max(points.Max(p => p.Value), marker.Target.Max ?? 0);
        return Math.Max(10, Math.Ceiling(highest * 1.1 / 10) * 10);
    }
}
