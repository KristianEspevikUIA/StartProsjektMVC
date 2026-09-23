using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StartPraksisGruppe3Prosjekt.Models.Identity;
using StartPraksisGruppe3Prosjekt.Services.Identity;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The Gold Standard file against the club's document, and what the catalog refuses to load.
///
/// The first tests load THE file the application ships (linked into the test output) and hold
/// it to "IK Start – Identity Gold Standard.pdf" word for word. That PDF has no text layer; the
/// expected values below were read off the rendered page. If the club changes the document,
/// these tests are the checklist.
///
/// Six of the club's ten markers sit under the substitutes that replaced them. They are held
/// to the document all the same: the club's words are all still in the file.
/// </summary>
public class IdentityCatalogTests
{
    private const string FixtureFolder = "Identity/Fixtures";

    internal static IdentityCatalog Load(string contentRoot, string matchDataPath = FixtureFolder, params string[] teams) =>
        new(
            new FakeWebHostEnvironment(contentRoot),
            Options.Create(new IdentityBenchmarkOptions
            {
                MatchDataPath = matchDataPath,
                Teams = teams.Length == 0 ? new[] { "U14", "U15" } : teams
            }),
            NullLogger<IdentityCatalog>.Instance);

    [Fact]
    public void The_shipped_markers_are_the_documents_markers_in_the_documents_order()
    {
        var expected = new[]
        {
            ("in-possession", "Possession %", "58% – 70%", "Barcelona / PSG (~70%)"),
            ("in-possession", "Progressive Passes", "50 – 112 / match", "Malmö FF (peak 112)"),
            ("in-possession", "Match Tempo", "15.5 – 19.8 p/min", "Bayern München (peak 19.79)"),
            ("in-possession", "Pass Accuracy", "83% – 91%", "Como / Bayern München (~91%)"),
            ("in-possession", "Successful Dribbles", "10 – 25 / match", "Lamine Yamal, Barcelona (23)"),
            ("out-of-possession", "PPDA (Intensity)", "Under 10.0", "Barcelona (peak 5.04)"),
            ("out-of-possession", "Opp. Half Recoveries", "22 – 42 / match", "Malmö FF (peak 42)"),
            ("out-of-possession", "Total Duels Won", "44% – 53%", "Racing Santander (peak 53%)"),
            ("out-of-possession", "Total Regains", "70 – 90 / match", "Racing Santander (peak 90)"),
            ("out-of-possession", "Interceptions", "25 – 56 / match", "Racing Santander (peak 56)")
        };

        var standard = Load(AppContext.BaseDirectory).GoldStandard;

        // Each marker on the page, or the club's marker a substitute replaced in that place.
        var actual = standard.Phases
            .SelectMany(p => p.Markers.Select(m => (Phase: p.Key, Club: m.Replaces?.Marker ?? m)))
            .Select(x => (x.Phase, x.Club.Name, x.Club.EliteRange, x.Club.BestAtIt))
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(actual.Select(a => a.Name), standard.ClubMarkers.Select(m => m.Name));
    }

    [Fact]
    public void Substitutes_stand_in_the_places_of_the_six_markers_a_report_cannot_answer()
    {
        var standard = Load(AppContext.BaseDirectory).GoldStandard;

        var substitutes = standard.AllMarkers
            .Where(m => m.IsSubstitute)
            .Select(m => (Replaces: m.Replaces!.Marker.Name, m.Name, m.Measurement.Metric))
            .ToArray();

        Assert.Equal(new[]
        {
            ("Progressive Passes", "Final Third Passes", (string?)"finalThirdPasses"),
            ("Match Tempo", "Total Passes", "totalPasses"),
            ("PPDA (Intensity)", "Pressures", "pressures"),
            ("Opp. Half Recoveries", "Counterpresses", "counterpresses"),
            ("Total Duels Won", "Tackle Success %", "tackleSuccessPct"),
            ("Total Regains", "Pressure Regains", "pressureRegains")
        }, substitutes);

        // The club's marker under each is kept whole -- still not measured, still saying why --
        // and the substitute says why it is the closest measure.
        Assert.All(standard.AllMarkers.Where(m => m.IsSubstitute), m =>
        {
            Assert.False(m.Replaces!.Marker.Measurement.IsMeasured);
            Assert.False(string.IsNullOrWhiteSpace(m.Replaces.Marker.Measurement.NotMeasuredReason));
            Assert.False(string.IsNullOrWhiteSpace(m.Replaces.Rationale));
        });
    }

    [Fact]
    public void A_range_that_is_not_the_clubs_is_provisional_and_says_where_it_comes_from()
    {
        var standard = Load(AppContext.BaseDirectory).GoldStandard;

        // The club's own four are held to the club's own ranges.
        Assert.All(standard.AllMarkers.Where(m => !m.IsSubstitute), m => Assert.False(m.Target.Provisional));

        // The substitutes' ranges were derived from the reports (scripts/identity/derive_targets.py),
        // and each says so: the opponents' average up to their best single match.
        Assert.All(standard.AllMarkers.Where(m => m.IsSubstitute), m =>
        {
            Assert.True(m.Target.Provisional, m.Name);
            Assert.Contains("opponents", m.Target.Basis);
            Assert.Contains("best single match", m.Target.Basis);
        });

        Assert.False(string.IsNullOrWhiteSpace(standard.Substitutes.Text));
    }

    [Fact]
    public void The_shipped_footnotes_and_texts_are_the_documents_own_words()
    {
        var standard = Load(AppContext.BaseDirectory).GoldStandard;

        var footnotes = standard.Phases
            .SelectMany(p => p.Footnotes.Select(f => $"{f.Term} {f.Separator} {f.Text}"))
            .ToArray();

        Assert.Equal(new[]
        {
            "Accuracy = total successful completions regardless of distance, rewarding overall technical security.",
            "Dribbles = a successful action that specifically beats an opponent or breaks a defensive line.",
            "PPDA — lower = higher pressing intensity; 5.04 (Barcelona) is the elite ceiling for a suffocating high press.",
            "Interceptions — identifies anticipation & football IQ (\"Malmö style\") rather than physical duels."
        }, footnotes);

        Assert.Equal("The \"Protagonist\" — controlling the game's climate & vertical intent", standard.Phases[0].Tagline);
        Assert.Equal("The \"Defending Forward\" — aggression & tactical reading to suffocate opponents", standard.Phases[1].Tagline);
        Assert.Equal(
            new[] { "Barcelona", "Bayern München", "PSG", "Como", "Racing Santander", "Malmö FF" },
            standard.ReferenceClubs);
        Assert.StartsWith("Benchmarks combine consistent team averages (the floor of performance)", standard.Why.Text);
    }

    [Fact]
    public void Every_printed_range_agrees_with_the_numbers_the_status_is_worked_out_from()
    {
        // The printed range is what a coach reads; min and max are what decide the badge. An
        // edit to one without the other would show one target and grade against another. The
        // replaced club markers too: moved back out, they would be graded on theirs.
        var standard = Load(AppContext.BaseDirectory).GoldStandard;
        foreach (var marker in standard.AllMarkers.Concat(standard.ClubMarkers).Distinct())
        {
            var printed = Regex.Matches(marker.EliteRange, @"\d+(?:\.\d+)?")
                .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            var configured = marker.Target.Direction == TargetDirection.LowerIsBetter
                ? new[] { marker.Target.Max!.Value }
                : new[] { marker.Target.Min!.Value, marker.Target.Max!.Value };

            Assert.True(configured.SequenceEqual(printed),
                $"{marker.Name}: printed '{marker.EliteRange}', configured [{string.Join(", ", configured)}].");
        }
    }

    [Fact]
    public void Every_marker_on_the_page_is_measured_from_the_match_reports()
    {
        var measured = Load(AppContext.BaseDirectory).GoldStandard.AllMarkers
            .Select(m => (m.Name, m.Measurement.Metric))
            .ToArray();

        // The keys scripts/identity/extract_stats.py writes, in the Gold Standard's order.
        Assert.Equal(new[]
        {
            ("Possession %", (string?)"possessionPct"),
            ("Final Third Passes", "finalThirdPasses"),
            ("Total Passes", "totalPasses"),
            ("Pass Accuracy", "passCompletionPct"),
            ("Successful Dribbles", "successfulDribbles"),
            ("Pressures", "pressures"),
            ("Counterpresses", "counterpresses"),
            ("Tackle Success %", "tackleSuccessPct"),
            ("Pressure Regains", "pressureRegains"),
            ("Interceptions", "interceptions")
        }, measured);
    }

    [Fact]
    public void The_fixture_squad_loads_oldest_match_first()
    {
        var team = Load(AppContext.BaseDirectory).FindTeam("u14");

        Assert.NotNull(team);
        Assert.True(team.HasData);
        Assert.Equal("Start U14", team.DisplayName);
        Assert.Equal(
            new[] { "2026-04-01-home-testby-u14", "2026-05-01-away-provestad-u14", "2026-06-01-home-eksempel-u14" },
            team.Data!.Matches.Select(m => m.Id));
    }

    [Fact]
    public void A_team_without_a_file_is_listed_without_data_rather_than_failing()
    {
        var catalog = Load(AppContext.BaseDirectory);

        Assert.Equal(new[] { "U14", "U15" }, catalog.Teams.Select(t => t.Key));
        Assert.False(catalog.FindTeam("U15")!.HasData);
    }

    [Fact]
    public void A_value_without_a_file_and_page_is_refused()
    {
        using var content = TemporaryContent.WithFixture(match =>
            match["matches"]![0]!["metrics"]!["interceptions"]!["source"]!["page"] = 0);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("'interceptions' has no source file and page", error.Message);
    }

    [Theory]
    [InlineData("possessionPct")]
    [InlineData("counterpresses")]
    public void A_match_missing_a_measured_value_is_refused(string metric)
    {
        using var content = TemporaryContent.WithFixture(match =>
            match["matches"]![1]!["metrics"]!.AsObject().Remove(metric));

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains($"has no '{metric}'", error.Message);
    }

    [Fact]
    public void Match_data_from_before_the_substitutes_is_refused()
    {
        // Version 1 has four markers' values; showing it would leave six rows empty.
        using var content = TemporaryContent.WithFixture(match => match["schemaVersion"] = 1);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("'schemaVersion' is 1", error.Message);
        Assert.Contains("Re-run scripts/identity/extract_stats.py", error.Message);
    }

    [Fact]
    public void A_marker_that_is_both_measured_and_explained_away_is_refused()
    {
        using var content = TemporaryContent.WithGoldStandard(standard =>
            standard["phases"]![1]!["markers"]![0]!["measurement"]!["notMeasuredReason"] = "No reason.");

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("Marker pressures needs exactly one of", error.Message);
    }

    [Fact]
    public void A_provisional_range_that_does_not_say_where_it_comes_from_is_refused()
    {
        using var content = TemporaryContent.WithGoldStandard(standard =>
            standard["phases"]![1]!["markers"]![0]!["target"]!.AsObject().Remove("basis"));

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("Marker pressures has a provisional target and needs 'target.basis'", error.Message);
    }

    [Fact]
    public void A_substitute_for_a_marker_that_can_be_measured_is_refused()
    {
        // Then there is nothing to replace: the club's marker goes back in its place instead.
        using var content = TemporaryContent.WithGoldStandard(standard =>
            standard["phases"]![1]!["markers"]![0]!["replaces"]!["marker"]!["measurement"] =
                new JsonObject { ["metric"] = "pressures" });

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("Marker pressures replaces 'ppda', which is measured", error.Message);
    }

    [Fact]
    public void A_substitute_under_the_key_of_the_marker_it_replaces_is_refused()
    {
        // It would pass for the club's own marker.
        using var content = TemporaryContent.WithGoldStandard(standard =>
            standard["phases"]![1]!["markers"]![0]!["key"] = "ppda");

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("The marker key 'ppda' is used more than once", error.Message);
    }

    [Fact]
    public void Status_thresholds_out_of_order_are_refused()
    {
        using var content = TemporaryContent.WithGoldStandard(standard =>
            standard["statusRules"]!["developingAtFractionOfMin"] = 0.95);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root, TemporaryContent.MatchFolder, "U14"));

        Assert.Contains("statusRules", error.Message);
    }

    /// <summary>A content root with a copy of the shipped Gold Standard and the fixture, either edited.</summary>
    private sealed class TemporaryContent : IDisposable
    {
        public const string MatchFolder = "Matches";

        private readonly DirectoryInfo _root;

        private TemporaryContent(Action<JsonNode>? editStandard, Action<JsonNode>? editFixture)
        {
            _root = Directory.CreateTempSubdirectory("startcompass-identity-tests-");

            Write(IdentityCatalog.GoldStandardPath, Path.Combine(AppContext.BaseDirectory, IdentityCatalog.GoldStandardPath), editStandard);
            Write($"{MatchFolder}/u14.json", Path.Combine(AppContext.BaseDirectory, FixtureFolder, "u14.json"), editFixture);
        }

        public static TemporaryContent WithFixture(Action<JsonNode> edit) => new(null, edit);

        public static TemporaryContent WithGoldStandard(Action<JsonNode> edit) => new(edit, null);

        public string Root => _root.FullName;

        private void Write(string relative, string original, Action<JsonNode>? edit)
        {
            var node = JsonNode.Parse(File.ReadAllText(original),
                documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
            edit?.Invoke(node);

            var file = Path.Combine(_root.FullName, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, node.ToJsonString());
        }

        public void Dispose() => _root.Delete(recursive: true);
    }
}
