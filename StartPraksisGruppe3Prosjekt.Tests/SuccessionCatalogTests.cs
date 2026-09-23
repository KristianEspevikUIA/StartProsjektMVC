using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The succession settings file against the coaches' workbook, what the catalog refuses to
/// load, and the eight-week cycles.
///
/// The first tests load THE file the application ships (linked into the test output) and hold
/// its lists to the workbook's "Text - Do Not Touch" sheet. If the coaches change the workbook,
/// these tests are the checklist.
/// </summary>
public class SuccessionCatalogTests
{
    private static readonly string ShippedPath =
        Path.Combine(AppContext.BaseDirectory, "Data", "Succession", "succession-planning.json");

    /// <summary>The shipped file, loaded and validated the way the application does it.</summary>
    internal static SuccessionCatalog Shipped() => new(SuccessionCatalog.Load(ShippedPath));

    [Fact]
    public void The_positions_are_the_workbooks_nineteen_in_its_order()
    {
        var expected = new[]
        {
            "RB", "RCB", "CB", "LCB", "LB", "RWB", "LWB", "R6", "C6", "L6",
            "R8", "L8", "RW", "ACM", "LW", "RST", "CF", "LST", "GK"
        };

        Assert.Equal(expected, Shipped().Settings.Positions.Select(p => p.Key));
    }

    [Fact]
    public void The_ratings_are_the_workbooks_six_columns()
    {
        Assert.Equal(
            new[] { "Physical", "Technical", "Tactical", "Mental", "Professionalism", "Availability" },
            Shipped().Settings.Ratings.Select(r => r.Name));
    }

    [Fact]
    public void The_lists_are_the_workbooks_lists()
    {
        var settings = Shipped().Settings;

        Assert.Equal(
            new[] { "Performance & Potential", "Potential", "Performance", "Squad" },
            settings.AbilityCategories.Select(c => c.Name));

        Assert.Equal(new[] { "pro", "youth", "non" }, settings.ContractTypes.Select(c => c.Key));

        Assert.Equal(
            new[]
            {
                "1st team", "U21s", "U19s", "U17s", "U15s", "U14s", "U13s",
                "Loan ES", "Loan 1.Div", "Loan 2.Div", "Loan 3.Div", "Loan 4.Div"
            },
            settings.Levels.Select(l => l.Name));

        Assert.Equal(new[] { "green", "amber", "red" }, settings.Risks.Select(r => r.Key));
    }

    [Fact]
    public void The_thresholds_are_the_workbooks_colour_scale_and_icon_set()
    {
        var settings = Shipped().Settings;

        // Colour scale 1 - 6 - 10; icon set lights at 6 and 8; ratings every eight weeks.
        Assert.Equal(1, settings.Scale.Min);
        Assert.Equal(10, settings.Scale.Max);
        Assert.Equal(6, settings.DevelopingAt);
        Assert.Equal(8, settings.ReadyAt);
        Assert.Equal(8, settings.Cycle.LengthWeeks);
    }

    [Fact]
    public void The_page_opens_on_the_three_five_two_and_every_formation_is_eleven()
    {
        // The coaches asked for the 1-3-5-2 -- the 3-5-2 with the keeper counted -- first, and
        // drew it: a 10 ahead of two 8s, the wing-backs level with the 10.
        var catalog = Shipped();

        Assert.Equal("3-5-2", catalog.DefaultFormation.Key);
        Assert.All(catalog.Settings.Formations, f => Assert.Equal(11, f.Slots.Count()));
        Assert.Equal(
            new[] { "LST", "RST", "LWB", "ACM", "RWB", "L8", "R8", "LCB", "CB", "RCB", "GK" },
            catalog.DefaultFormation.Slots);
        Assert.NotNull(catalog.FindFormation("4-3-3"));
    }

    [Fact]
    public void A_formation_can_be_asked_for_with_the_goalkeeper_counted()
    {
        // "Formation might be 1-3-5-2": the coaches' own way of writing the 3-5-2.
        var catalog = Shipped();

        Assert.Equal("3-5-2", catalog.FindFormation("1-3-5-2")!.Key);
        Assert.Equal("4-3-3", catalog.FindFormation(" 1-4-3-3 ")!.Key);
        Assert.Equal("1-3-5-2", catalog.FindFormation("3-5-2")!.GoalkeeperNotation);
        Assert.Null(catalog.FindFormation("1-2-3-5"));
        Assert.Null(catalog.FindFormation("1-"));
    }

    [Fact]
    public void Out_of_position_has_to_cost_at_least_as_much_as_a_3rd_position()
    {
        // Less would make a position nobody named a better fit than one a coach put third.
        var message = Refused(s => new SuccessionSettings
        {
            Version = s.Version,
            Cycle = s.Cycle,
            Scale = s.Scale,
            ReadyAt = s.ReadyAt,
            DevelopingAt = s.DevelopingAt,
            DisagreementAt = s.DisagreementAt,
            HorizonWeeks = s.HorizonWeeks,
            PositionRankPenalty = s.PositionRankPenalty,
            OutOfPositionPenalty = 0.5,
            Ratings = s.Ratings,
            Positions = s.Positions,
            Formations = s.Formations,
            AbilityCategories = s.AbilityCategories,
            ContractTypes = s.ContractTypes,
            Levels = s.Levels,
            Risks = s.Risks
        });

        Assert.Contains("outOfPositionPenalty", message);
        Assert.Equal(2.0, Shipped().Settings.OutOfPositionPenalty);
    }

    [Fact]
    public void A_formation_with_a_name_that_is_not_numbers_has_no_goalkeeper_notation()
    {
        var diamond = new FormationDefinition
        {
            Key = "diamond",
            Name = "Diamond",
            Lines = new[] { new[] { "GK" } }
        };

        Assert.Null(diamond.GoalkeeperNotation);
    }

    [Fact]
    public void Lookups_ignore_case_and_say_null_for_what_is_not_there()
    {
        var catalog = Shipped();

        Assert.Equal("Right-back", catalog.Position("rb")!.Name);
        Assert.NotNull(catalog.FindFormation("4-2-3-1"));
        Assert.Null(catalog.Position("SW"));
        Assert.Null(catalog.FindFormation("2-3-5"));
        Assert.Equal(2, catalog.RiskSeverity("red"));
        Assert.Equal(-1, catalog.RiskSeverity("purple"));
    }

    // -----------------------------------------------------------------------------------
    // What it refuses
    // -----------------------------------------------------------------------------------

    [Fact]
    public void A_formation_of_ten_stops_startup()
    {
        var error = Refused(s => Replace(s, formations: new[]
        {
            Formation("4-3-2", new[] { "CF", "RW" }, new[] { "L8", "R8", "C6" }, new[] { "LB", "LCB", "RCB", "RB" }, new[] { "GK" })
        }));

        Assert.Contains("4-3-2", error);
        Assert.Contains("10 slots", error);
    }

    [Fact]
    public void A_slot_naming_an_unknown_position_stops_startup()
    {
        var error = Refused(s => Replace(s, formations: new[]
        {
            Formation("4-3-3", new[] { "LW", "ST", "RW" }, new[] { "L8", "R8" }, new[] { "C6" }, new[] { "LB", "LCB", "RCB", "RB" }, new[] { "GK" })
        }));

        Assert.Contains("'ST'", error);
    }

    [Fact]
    public void The_same_position_twice_in_a_formation_stops_startup()
    {
        var error = Refused(s => Replace(s, formations: new[]
        {
            Formation("4-4-2", new[] { "CF", "CF" }, new[] { "LW", "L8", "R8", "RW" }, new[] { "LB", "LCB", "RCB", "RB" }, new[] { "GK" })
        }));

        Assert.Contains("'CF' twice", error);
    }

    [Fact]
    public void A_tone_outside_the_fixed_set_stops_startup()
    {
        var error = Refused(s => Replace(s, risks: new[]
        {
            new SuccessionOption { Key = "green", Name = "Green", Tone = "#00ff00" }
        }));

        Assert.Contains("#00ff00", error);
        Assert.Contains("blue, green, amber, red, grey", error);
    }

    [Fact]
    public void Thresholds_in_the_wrong_order_stop_startup()
    {
        var error = Refused(s => Replace(s, readyAt: 5, developingAt: 7));

        Assert.Contains("developingAt < readyAt", error);
    }

    [Fact]
    public void A_missing_file_stops_startup_and_says_where_it_looked()
    {
        var path = Path.Combine(Path.GetTempPath(), "does-not-exist", "succession-planning.json");

        var error = Assert.Throws<InvalidOperationException>(() => SuccessionCatalog.Load(path));

        Assert.Contains(path, error.Message);
    }

    // -----------------------------------------------------------------------------------
    // Cycles
    // -----------------------------------------------------------------------------------

    private static CycleSettings Cycles => Shipped().Settings.Cycle;

    [Fact]
    public void The_first_cycle_starts_on_the_first_Monday_of_2026_and_lasts_eight_weeks()
    {
        var cycle = SuccessionCycle.Of(new DateOnly(2026, 1, 5), Cycles);

        Assert.Equal(1, cycle.Number);
        Assert.Equal(new DateOnly(2026, 1, 5), cycle.StartsOn);
        Assert.Equal(new DateOnly(2026, 3, 1), cycle.EndsOn);
    }

    [Theory]
    [InlineData("2026-03-01", "2026-01-05")] // the last day of cycle one
    [InlineData("2026-03-02", "2026-03-02")] // the first of cycle two
    [InlineData("2026-09-23", "2026-08-17")]
    [InlineData("2027-01-10", "2026-12-07")] // across the new year without a gap
    [InlineData("2026-01-04", "2025-11-10")] // before the first cycle: counted backwards
    public void Every_date_is_in_exactly_one_cycle(string date, string startsOn)
    {
        var cycle = SuccessionCycle.Of(DateOnly.Parse(date), Cycles);

        Assert.Equal(DateOnly.Parse(startsOn), cycle.StartsOn);
        Assert.True(cycle.Contains(DateOnly.Parse(date)));
    }

    [Fact]
    public void The_cycle_before_is_the_eight_weeks_before()
    {
        var cycle = SuccessionCycle.Of(new DateOnly(2026, 9, 23), Cycles);

        Assert.Equal(new DateOnly(2026, 6, 22), cycle.Previous(Cycles).StartsOn);
    }

    [Fact]
    public void A_cycle_in_a_url_is_its_first_day_and_any_day_in_it_finds_it()
    {
        var cycle = SuccessionCycle.Of(new DateOnly(2026, 9, 23), Cycles);

        Assert.Equal("2026-08-17", cycle.Key);
        Assert.Equal(cycle, SuccessionCycle.TryParse("2026-09-01", Cycles));
        Assert.Null(SuccessionCycle.TryParse("17.08.2026", Cycles));
        Assert.Null(SuccessionCycle.TryParse(null, Cycles));
    }

    [Fact]
    public void The_label_is_dates_in_English_whatever_the_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nb-NO");

            var cycle = SuccessionCycle.Of(new DateOnly(2026, 9, 23), Cycles);

            Assert.Equal("17 Aug – 11 Oct 2026", cycle.Label);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static string Refused(Func<SuccessionSettings, SuccessionSettings> change)
    {
        var settings = change(SuccessionCatalog.Load(ShippedPath));

        return Assert.Throws<InvalidOperationException>(() => new SuccessionCatalog(settings)).Message;
    }

    private static FormationDefinition Formation(string key, params string[][] lines) => new()
    {
        Key = key,
        Name = key,
        Lines = lines
    };

    /// <summary>The shipped settings with one part swapped out. Everything else stays valid.</summary>
    private static SuccessionSettings Replace(
        SuccessionSettings s,
        IReadOnlyList<FormationDefinition>? formations = null,
        IReadOnlyList<SuccessionOption>? risks = null,
        double? readyAt = null,
        double? developingAt = null) => new()
    {
        Version = s.Version,
        Cycle = s.Cycle,
        Scale = s.Scale,
        ReadyAt = readyAt ?? s.ReadyAt,
        DevelopingAt = developingAt ?? s.DevelopingAt,
        DisagreementAt = s.DisagreementAt,
        HorizonWeeks = s.HorizonWeeks,
        PositionRankPenalty = s.PositionRankPenalty,
        OutOfPositionPenalty = s.OutOfPositionPenalty,
        Ratings = s.Ratings,
        Positions = s.Positions,
        Formations = formations ?? s.Formations,
        AbilityCategories = s.AbilityCategories,
        ContractTypes = s.ContractTypes,
        Levels = s.Levels,
        Risks = risks ?? s.Risks
    };
}
