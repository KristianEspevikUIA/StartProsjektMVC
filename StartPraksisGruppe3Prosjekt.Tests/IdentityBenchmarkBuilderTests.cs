using StartPraksisGruppe3Prosjekt.Models.Identity;
using StartPraksisGruppe3Prosjekt.Services.Identity;
using StartPraksisGruppe3Prosjekt.ViewModels.Identity;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// What the Identity page says about a team, against the fictional squad in
/// Identity/Fixtures/u14.json and the shipped Gold Standard.
///
/// The fixture, by match -- the club's four markers (possession, pass accuracy, dribbles,
/// interceptions), then the six substitutes (final-third passes, total passes, pressures,
/// counterpresses, tackle success, pressure regains):
///   2026-04-01 home vs Testby       60 / 80 / 12 / 20     70 / 560 / 200 / 50 / 60 / 65
///   2026-05-01 away at Prøvestad    45 / 84 / 30 / 26    150 / 300 / 250 / 60 / 70 / 70
///   2026-06-01 home vs Eksempel     51 / 86 /  3 / 10     60 / 580 / 170 / 40 / 90 / 50
///
/// The substitutes' averages are all inside their ranges, so they never push the club's
/// markers out of the insights that the tests below read.
/// </summary>
public class IdentityBenchmarkBuilderTests
{
    private const string Testby = "2026-04-01-home-testby-u14";
    private const string Provestad = "2026-05-01-away-provestad-u14";
    private const string Eksempel = "2026-06-01-home-eksempel-u14";

    private static readonly IdentityCatalog Catalog = IdentityCatalogTests.Load(AppContext.BaseDirectory);

    private static IdentityBenchmarkViewModel Build(string? match, IIdentityCatalog? catalog = null) =>
        new IdentityBenchmarkBuilder(catalog ?? Catalog).Build("U14", match)
        ?? throw new InvalidOperationException("Expected a page.");

    private static MarkerReading Reading(IdentityBenchmarkViewModel page, string markerKey) =>
        page.AllReadings.Single(r => r.Marker.Key == markerKey);

    [Fact]
    public void A_match_shows_its_own_values_each_with_file_and_page()
    {
        var page = Build(Testby);

        var possession = Reading(page, "possession");
        Assert.Equal(60, possession.Value);
        Assert.Equal("60%", possession.DisplayValue);
        Assert.Equal(IdentityStatus.EliteAlignment, possession.Status);
        var source = Assert.Single(possession.Sources);
        Assert.Equal("Testserien G14 2026 Start U14 Testby U14.pdf", source.File);
        Assert.Equal(4, source.Page);

        Assert.Equal(IdentityStatus.StrongAlignment, Reading(page, "pass-accuracy").Status);   // 80 vs 83
        Assert.Equal(IdentityStatus.EliteAlignment, Reading(page, "successful-dribbles").Status);
        Assert.Equal("20", Reading(page, "interceptions").DisplayValue);
        Assert.Equal(IdentityStatus.Developing, Reading(page, "interceptions").Status);        // 20 vs 25
        Assert.Equal(23, Reading(page, "interceptions").Sources.Single().Page);

        Assert.Equal(15, Reading(page, "counterpresses").Sources.Single().Page);               // the Gegenpressing page
        Assert.Equal(IdentityStatus.StrongAlignment, Reading(page, "tackle-success").Status);  // 60 vs 64
    }

    [Fact]
    public void A_substitute_is_graded_against_its_own_provisional_range()
    {
        var page = Build(Provestad);

        var passes = Reading(page, "total-passes");
        Assert.True(passes.Marker.Target.Provisional);
        Assert.Equal(300, passes.Value);
        Assert.Equal("300", passes.DisplayValue);
        Assert.Equal(IdentityStatus.BelowTarget, passes.Status);                               // 300 of 471 is under 75 %
        Assert.Equal(3, passes.Sources.Single().Page);

        Assert.Equal(IdentityStatus.Exceptional, Reading(page, "final-third-passes").Status);   // 150 over 140
        Assert.Equal(21, Reading(page, "final-third-passes").Sources.Single().Page);
        Assert.Equal("70%", Reading(page, "tackle-success").DisplayValue);
    }

    [Fact]
    public void A_club_marker_put_back_without_data_gets_no_value_no_status_and_no_source()
    {
        // The club's six, moved back out in place of their substitutes: what the page does with
        // any marker the reports cannot answer.
        var matches = Catalog.FindTeam("U14")!.Data!.Matches.ToArray();
        var page = Build(Testby, new FixedCatalog(WithClubMarkersRestored(Catalog.GoldStandard), matches));

        var notMeasured = page.AllReadings.Where(r => !r.IsMeasured).ToList();

        Assert.Equal(
            new[] { "progressive-passes", "match-tempo", "ppda", "opp-half-recoveries", "total-duels-won", "total-regains" },
            notMeasured.Select(r => r.Marker.Key));

        Assert.All(notMeasured, r =>
        {
            Assert.Null(r.Value);
            Assert.Null(r.DisplayValue);
            Assert.Null(r.Status);
            Assert.Empty(r.Sources);
        });

        // Nor are they written about, or drawn.
        Assert.DoesNotContain(page.Insights, i => notMeasured.Any(r => r.Marker.Name == i.MarkerName));
        Assert.Equal(
            new[] { "possession", "pass-accuracy", "successful-dribbles", "interceptions" },
            page.Trends.SelectMany(g => g.Trends).Select(t => t.Marker.Key));
    }

    [Fact]
    public void The_rows_follow_the_gold_standard_order()
    {
        var page = Build(null);

        Assert.Equal(
            Catalog.GoldStandard.AllMarkers.Select(m => m.Key),
            page.AllReadings.Select(r => r.Marker.Key));
    }

    [Fact]
    public void The_average_is_the_mean_of_every_match_with_every_source()
    {
        var page = Build(IdentityBenchmarkBuilder.AllMatches);

        Assert.True(page.IsAverage);
        Assert.Equal(3, page.MatchCount);

        var possession = Reading(page, "possession");     // (60 + 45 + 51) / 3
        Assert.Equal(52.0, possession.Value);
        Assert.Equal("52.0%", possession.DisplayValue);
        Assert.Equal(IdentityStatus.Developing, possession.Status);
        Assert.Equal(3, possession.Sources.Count);
        Assert.Equal(1, possession.MatchesAtOrAboveRange);
        Assert.StartsWith("Mean of the 3 per-match values", possession.Formula);

        Assert.Equal(83.3, Reading(page, "pass-accuracy").Value);          // 83.33
        Assert.Equal(IdentityStatus.EliteAlignment, Reading(page, "pass-accuracy").Status);
        Assert.Equal(18.7, Reading(page, "interceptions").Value);          // 18.67
        Assert.Equal(IdentityStatus.BelowTarget, Reading(page, "interceptions").Status);
    }

    [Fact]
    public void The_status_of_an_average_is_taken_from_the_value_that_is_printed()
    {
        // A mean of 82.96 is printed as 83.0 -- inside the 83-91 range -- so the badge next to it
        // must say Elite Alignment. Graded on the raw mean it would say Strong, beside "83.0%".
        var catalog = new FixedCatalog(Catalog.GoldStandard, Match("a", 1, passAccuracy: 82.96), Match("b", 2, passAccuracy: 82.96));

        var reading = Reading(Build(null, catalog), "pass-accuracy");

        Assert.Equal("83.0%", reading.DisplayValue);
        Assert.Equal(IdentityStatus.EliteAlignment, reading.Status);
    }

    [Fact]
    public void Matches_are_offered_after_the_average_oldest_first()
    {
        var page = Build(null);

        Assert.Equal(new[] { IdentityBenchmarkBuilder.AllMatches, Testby, Provestad, Eksempel }, page.Matches.Select(m => m.Value));
        Assert.Equal("1 May 2026 · Prøvestad U14 1–3 Start U14", page.Matches[2].Label);
    }

    [Fact]
    public void The_highlight_is_one_player_only_when_they_lead_both_markers()
    {
        var highlight = Build(Eksempel).Highlight;

        Assert.NotNull(highlight);
        Assert.Equal("Alfa Testspiller", highlight.Player);
        Assert.Equal("This match", highlight.Scope);
        Assert.Equal(new[] { ("Successful Dribbles", 3), ("Interceptions", 10) },
            highlight.Leaders.Select(l => (l.MarkerName, l.Value)));
        Assert.Equal(24, highlight.Sources.Single().Page);
    }

    [Fact]
    public void Different_leaders_are_named_per_marker_without_a_single_player()
    {
        var highlight = Build(Testby).Highlight!;

        Assert.Null(highlight.Player);
        Assert.Equal(new[] { "Alfa Testspiller" }, highlight.Leaders[0].Players);
        Assert.Equal(new[] { "Bravo Testspiller" }, highlight.Leaders[1].Players);
        Assert.Equal(9, highlight.Leaders[1].Value);
    }

    [Fact]
    public void A_tie_names_everyone_tied_and_is_not_a_single_player()
    {
        var highlight = Build(Provestad).Highlight!;

        Assert.Null(highlight.Player);
        Assert.Equal(new[] { "Alfa Testspiller", "Bravo Testspiller" }, highlight.Leaders[1].Players);
        Assert.Equal(13, highlight.Leaders[1].Value);
    }

    [Fact]
    public void Across_all_matches_the_highlight_uses_season_totals()
    {
        var highlight = Build(null).Highlight!;

        Assert.Equal("Totals across 3 matches", highlight.Scope);
        Assert.Equal("Alfa Testspiller", highlight.Player);   // 30 dribbles, 28 interceptions
        Assert.Equal(new[] { 30, 28 }, highlight.Leaders.Select(l => l.Value));
        Assert.Equal(3, highlight.Sources.Count);
    }

    [Fact]
    public void Insights_speak_only_about_measured_markers_strengths_first_and_at_most_four()
    {
        var measured = Catalog.GoldStandard.AllMarkers.Where(m => m.Measurement.IsMeasured).Select(m => m.Name).ToHashSet();

        foreach (var match in new[] { null, Testby, Provestad, Eksempel })
        {
            var insights = Build(match).Insights;

            Assert.InRange(insights.Count, 3, IdentityInsightsBuilder.MaxInsights);
            Assert.All(insights, i => Assert.Contains(i.MarkerName, measured));

            // Strengths, then the rest: the statuses never go up again once they have gone down.
            var firstGrowth = insights.ToList().FindIndex(i => i.Status < IdentityStatus.EliteAlignment);
            if (firstGrowth >= 0)
            {
                Assert.All(insights.Skip(firstGrowth), i => Assert.True(i.Status < IdentityStatus.EliteAlignment));
            }
        }
    }

    [Fact]
    public void An_insight_states_the_value_the_range_and_who_contributed()
    {
        var interceptions = Build(Testby).Insights.Single(i => i.MarkerName == "Interceptions");

        Assert.Equal("Growth area", interceptions.Label);
        Assert.Equal(
            "20 — 5 short of the elite range of 25 – 56 / match. " +
            "Led by Bravo Testspiller (9), Charlie Testspiller (6) and Alfa Testspiller (5).",
            interceptions.Text);

        var possession = Build(null).Insights.Single(i => i.MarkerName == "Possession %");
        Assert.Equal(
            "52.0% on average — 6 percentage points short of the elite range of 58% – 70%. " +
            "In or above the range in 1 of 3 matches.",
            possession.Text);
    }

    [Fact]
    public void An_insight_on_a_substitute_calls_its_range_provisional_not_elite()
    {
        var insights = Build(Provestad).Insights;

        var passes = insights.Single(i => i.MarkerName == "Total Passes");
        Assert.Equal("Growth area", passes.Label);
        Assert.Equal("300 — 171 short of the provisional range of 471 – 596 / match.", passes.Text);

        var finalThird = insights.Single(i => i.MarkerName == "Final Third Passes");
        Assert.Equal("Strength", finalThird.Label);
        Assert.Equal("150 — above the provisional range of 68 – 140 / match.", finalThird.Text);
    }

    [Fact]
    public void An_insight_leaves_out_a_tie_that_crosses_the_three_names_it_gives()
    {
        // Four players on one interception each: naming whichever sorts first would rank them.
        var catalog = new FixedCatalog(
            Catalog.GoldStandard,
            Match("a", 1, passAccuracy: 80, ("Delta", 1), ("Alfa", 5), ("Echo", 1), ("Bravo", 3), ("Charlie", 1), ("Foxtrot", 1)));

        var interceptions = Build("a", catalog).Insights.Single(i => i.MarkerName == "Interceptions");

        Assert.EndsWith("Led by Alfa (5) and Bravo (3).", interceptions.Text);
    }

    [Fact]
    public void Development_covers_every_match_oldest_first_for_measured_markers_only()
    {
        var groups = Build(Eksempel).Trends;

        // In Possession, then Out of Possession, each in the Gold Standard's order.
        Assert.Equal(new[] { "in-possession", "out-of-possession" }, groups.Select(g => g.Phase.Key));
        Assert.Equal(
            new[] { "possession", "final-third-passes", "total-passes", "pass-accuracy", "successful-dribbles" },
            groups[0].Trends.Select(t => t.Marker.Key));
        Assert.Equal(
            new[] { "pressures", "counterpresses", "tackle-success", "pressure-regains", "interceptions" },
            groups[1].Trends.Select(t => t.Marker.Key));

        var trends = groups.SelectMany(g => g.Trends).ToList();
        Assert.All(trends, t => Assert.Equal(new[] { Testby, Provestad, Eksempel }, t.Points.Select(p => p.MatchId)));

        MarkerTrend Trend(string key) => trends.Single(t => t.Marker.Key == key);

        Assert.Equal(100, Trend("possession").AxisMax);               // a percentage
        Assert.Equal(40, Trend("successful-dribbles").AxisMax);       // 30 * 1.1 -> next ten
        Assert.Equal(IdentityStatus.Exceptional, Trend("successful-dribbles").Points[1].Status);
        Assert.Equal(660, Trend("total-passes").AxisMax);             // the range's 596 fits: 655.6 -> next ten
    }

    [Fact]
    public void An_unknown_team_or_match_is_not_a_page()
    {
        var builder = new IdentityBenchmarkBuilder(Catalog);

        Assert.Null(builder.Build("U99", null));
        Assert.Null(builder.Build("U14", "2020-01-01-home-nobody"));
    }

    [Fact]
    public void A_team_without_data_is_a_page_without_rows()
    {
        var page = new IdentityBenchmarkBuilder(Catalog).Build("U15", null)!;

        Assert.False(page.HasData);
        Assert.Empty(page.Phases);
        Assert.Empty(page.Insights);
        Assert.Null(page.Highlight);
        Assert.Equal(new[] { "U14", "U15" }, page.Teams.Select(t => t.Key));
    }

    private static IdentityMatch Match(string id, int day, double passAccuracy, params (string Name, int Interceptions)[] players)
    {
        SourcedMetric Metric(double value) => new()
        {
            Value = value,
            Source = new SourceReference { File = $"{id}.pdf", Page = 4 },
            Formula = "Test."
        };

        // Every marker a test does not set sits at the floor of its range: Elite Alignment, so it
        // never crowds the one under test out of the insights.
        var metrics = Catalog.GoldStandard.AllMarkers
            .Where(m => m.Measurement.IsMeasured)
            .ToDictionary(m => m.Measurement.Metric!, m => Metric(m.Target.Min ?? m.Target.Max!.Value));
        metrics["possessionPct"] = Metric(50);
        metrics["passCompletionPct"] = Metric(passAccuracy);
        metrics["successfulDribbles"] = Metric(10);
        metrics["interceptions"] = Metric(players.Sum(p => p.Interceptions));

        return new IdentityMatch
        {
            Id = id,
            Date = new DateOnly(2026, 1, day),
            File = $"{id}.pdf",
            HomeTeam = "Start U14",
            AwayTeam = "Test U14",
            StartIsHome = true,
            Metrics = metrics,
            Players = players
                .Select(p => new MatchPlayerLine { Name = p.Name, Interceptions = p.Interceptions })
                .ToList(),
            PlayersSource = new SourceReference { File = $"{id}.pdf", Page = 22 }
        };
    }

    /// <summary>The shipped Gold Standard with each substitute's club marker back in its place.</summary>
    private static GoldStandard WithClubMarkersRestored(GoldStandard standard) => new()
    {
        Version = standard.Version,
        Title = standard.Title,
        StatusRules = standard.StatusRules,
        Phases = standard.Phases
            .Select(phase => new IdentityPhase
            {
                Key = phase.Key,
                Title = phase.Title,
                Tagline = phase.Tagline,
                Footnotes = phase.Footnotes,
                Markers = phase.Markers.Select(m => m.Replaces?.Marker ?? m).ToList()
            })
            .ToList()
    };

    private sealed class FixedCatalog : IIdentityCatalog
    {
        private readonly IdentityTeam _team;

        public FixedCatalog(GoldStandard standard, params IdentityMatch[] matches)
        {
            GoldStandard = standard;
            _team = new IdentityTeam("U14", new TeamMatchData { Team = "U14", TeamName = "Start U14", Matches = matches });
        }

        public GoldStandard GoldStandard { get; }

        public IReadOnlyList<IdentityTeam> Teams => new[] { _team };

        public IdentityTeam? FindTeam(string key) => key == "U14" ? _team : null;
    }
}
