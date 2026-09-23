using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The succession arithmetic, one rule at a time: overall readiness, the coaches together, how
/// far off and how long, and the best eleven.
///
/// Against the shipped settings file, so a threshold changed in the file is a threshold changed
/// here -- except where a test needs a number of its own, and then it says so.
/// </summary>
public class SuccessionMathTests
{
    private static readonly SuccessionCatalog Catalog = SuccessionCatalogTests.Shipped();

    private static SuccessionSettings Settings => Catalog.Settings;

    // -----------------------------------------------------------------------------------
    // One coach
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Overall_readiness_is_the_average_of_the_six_ratings()
    {
        var assessment = Assessment("coach-a", 8, 6, 7, 9, 5, 7);

        // The workbook's =SUM(N:S)/6: 42 / 6.
        Assert.Equal(7.0, SuccessionMath.OverallOf(assessment, Settings));
    }

    [Fact]
    public void A_missing_rating_is_left_out_not_counted_as_zero()
    {
        // The workbook divides by six whatever is filled in, so a blank drags the player down a
        // sixth. Here five ratings of 6 are a 6, not a 5.
        var assessment = Assessment("coach-a", 6, 6, 6, 6, 6);

        Assert.Equal(6.0, SuccessionMath.OverallOf(assessment, Settings));
    }

    [Fact]
    public void No_ratings_is_no_overall_rather_than_zero()
    {
        Assert.Null(SuccessionMath.OverallOf(new SuccessionAssessment(), Settings));
    }

    [Fact]
    public void A_rating_the_file_does_not_list_is_ignored()
    {
        var assessment = Assessment("coach-a", 6, 6, 6, 6, 6, 6);
        assessment.Ratings.Add(new SuccessionRating { RatingKey = "speed", Value = 1 });

        Assert.Equal(6.0, SuccessionMath.OverallOf(assessment, Settings));
    }

    [Theory]
    [InlineData(8.0, ReadinessLevel.Ready)]
    [InlineData(7.99, ReadinessLevel.Developing)]
    [InlineData(6.0, ReadinessLevel.Developing)]
    [InlineData(5.99, ReadinessLevel.NotYet)]
    public void The_lights_are_the_workbooks_icon_set(double readiness, ReadinessLevel expected)
    {
        // Green from 8, amber from 6, red below -- the icon set's cut-offs in the workbook.
        Assert.Equal(expected, SuccessionMath.LevelOf(readiness, Settings));
    }

    // -----------------------------------------------------------------------------------
    // The coaches together
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Every_coach_counts_once_in_the_overall()
    {
        var consensus = Consensus(
            Assessment("coach-a", 8, 8, 8, 8, 8, 8),
            // Five ratings, not six: a mean of ratings would weight coach-a by six and this one
            // by five. A mean of coaches is (8 + 5) / 2.
            Assessment("coach-b", 5, 5, 5, 5, 5));

        Assert.Equal(6.5, consensus.Overall);
        Assert.Equal(3.0, consensus.OverallSpread);
    }

    [Fact]
    public void Each_rating_has_its_mean_and_the_gap_between_the_furthest_two()
    {
        var consensus = Consensus(
            Assessment("coach-a", 4, 6, 6, 6, 6, 6),
            Assessment("coach-b", 8, 6, 6, 6, 6, 6),
            Assessment("coach-c", 6, 7, 6, 6, 6, 6));

        var physical = consensus.Ratings.Single(r => r.Key == "physical");

        Assert.Equal(6.0, physical.Mean);
        Assert.Equal(4, physical.Spread);

        // Four points apart is past the threshold of three; one point is not.
        Assert.Contains(consensus.Disagreements, r => r.Key == "physical");
        Assert.DoesNotContain(consensus.Disagreements, r => r.Key == "technical");
        Assert.True(consensus.CoachesDisagree);
    }

    [Fact]
    public void One_coach_cannot_disagree_with_themselves()
    {
        var consensus = Consensus(Assessment("coach-a", 1, 10, 1, 10, 1, 10));

        Assert.All(consensus.Ratings, r => Assert.Null(r.Spread));
        Assert.Null(consensus.OverallSpread);
        Assert.False(consensus.CoachesDisagree);
    }

    [Fact]
    public void A_category_with_the_most_votes_wins()
    {
        var consensus = Consensus(
            With(Assessment("coach-a", 6, 6, 6, 6, 6, 6), a => a.AbilityCategory = "potential"),
            With(Assessment("coach-b", 6, 6, 6, 6, 6, 6), a => a.AbilityCategory = "potential"),
            With(Assessment("coach-c", 6, 6, 6, 6, 6, 6), a => a.AbilityCategory = "squad"));

        Assert.Equal("potential", consensus.AbilityCategory.Winner);
        Assert.False(consensus.AbilityCategory.IsSplit);
    }

    [Fact]
    public void A_tie_is_a_split_and_not_the_alphabetically_first()
    {
        var consensus = Consensus(
            With(Assessment("coach-a", 6, 6, 6, 6, 6, 6), a => a.AbilityCategory = "potential"),
            With(Assessment("coach-b", 6, 6, 6, 6, 6, 6), a => a.AbilityCategory = "squad"));

        Assert.Null(consensus.AbilityCategory.Winner);
        Assert.True(consensus.AbilityCategory.IsSplit);

        // A split on the category is a disagreement worth talking about even when the numbers agree.
        Assert.True(consensus.CoachesDisagree);
    }

    [Fact]
    public void A_position_counts_at_the_best_rank_any_coach_gave_it()
    {
        var positions = SuccessionMath.PositionsOf(new[]
        {
            With(new SuccessionAssessment(), a => (a.FirstPosition, a.SecondPosition, a.ThirdPosition) = ("RCB", "RB", "CB")),
            With(new SuccessionAssessment(), a => (a.FirstPosition, a.SecondPosition) = ("RB", "RCB"))
        });

        var rb = positions.Single(p => p.Key == "RB");
        Assert.Equal(1, rb.BestRank);
        Assert.Equal(2, rb.ListedBy);
        Assert.Equal(1, rb.FirstChoiceBy);

        // RB and RCB are each a 1st position for one coach and named by both, so they tie on
        // everything but the key. CB, a 3rd position only, comes after both.
        Assert.Equal(new[] { "RB", "RCB", "CB" }, positions.Select(p => p.Key));
    }

    [Fact]
    public void Each_position_column_is_a_vote_of_its_own()
    {
        var consensus = Consensus(
            Positions(Assessment("coach-a", 6, 6, 6, 6, 6, 6), "RB", "RWB", "RCB"),
            Positions(Assessment("coach-b", 6, 6, 6, 6, 6, 6), "RB", "RCB", "RWB"),
            Positions(Assessment("coach-c", 6, 6, 6, 6, 6, 6), "RCB", "RWB", null));

        // 1st: RB twice, RCB once. 2nd: RWB twice. 3rd: one RCB, one RWB -- a split, and a
        // blank is nobody's vote.
        Assert.Equal("RB", consensus.PositionsByRank[0].Winner);
        Assert.Equal("RWB", consensus.PositionsByRank[1].Winner);
        Assert.Null(consensus.PositionsByRank[2].Winner);
        Assert.True(consensus.PositionsByRank[2].IsSplit);
        Assert.Equal(new[] { "RCB", "RWB" }, consensus.PositionsByRank[2].Votes.Select(v => v.Key));
    }

    [Fact]
    public void The_columns_can_differ_from_the_list_the_eleven_picks_from()
    {
        // Two coaches, two different 1st positions. The 1st column is a split -- the board says
        // the coaches disagree where the player plays -- while the eleven may still use both,
        // because each is somebody's 1st.
        var consensus = Consensus(
            Positions(Assessment("coach-a", 6, 6, 6, 6, 6, 6), "RB", null, null),
            Positions(Assessment("coach-b", 6, 6, 6, 6, 6, 6), "LB", null, null));

        Assert.True(consensus.PositionsByRank[0].IsSplit);
        Assert.All(consensus.Positions, p => Assert.Equal(1, p.BestRank));
        Assert.Equal(2, consensus.Positions.Count);
    }

    [Fact]
    public void Nobody_naming_a_2nd_position_is_no_vote_rather_than_a_split()
    {
        var consensus = Consensus(Positions(Assessment("coach-a", 6, 6, 6, 6, 6, 6), "GK", null, null));

        Assert.Equal("GK", consensus.PositionsByRank[0].Winner);
        Assert.Null(consensus.PositionsByRank[1].Winner);
        Assert.False(consensus.PositionsByRank[1].IsSplit);
        Assert.Empty(consensus.PositionsByRank[1].Votes);
    }

    [Fact]
    public void The_same_position_twice_on_one_row_counts_once()
    {
        var positions = SuccessionMath.PositionsOf(new[]
        {
            With(new SuccessionAssessment(), a => (a.FirstPosition, a.SecondPosition) = ("LB", "LB"))
        });

        var lb = Assert.Single(positions);
        Assert.Equal(1, lb.ListedBy);
    }

    [Fact]
    public void The_most_severe_risk_named_is_the_one_shown()
    {
        var consensus = Consensus(
            With(Assessment("coach-a", 6, 6, 6, 6, 6, 6), a => a.SuccessionRisk = "green"),
            With(Assessment("coach-b", 6, 6, 6, 6, 6, 6), a => a.SuccessionRisk = "red"),
            With(Assessment("coach-c", 6, 6, 6, 6, 6, 6), a => a.SuccessionRisk = "amber"));

        Assert.Equal("red", consensus.WorstRisk);
        Assert.Equal(1, consensus.WorstRiskCount);
    }

    [Fact]
    public void Yes_and_no_count_only_the_coaches_who_answered()
    {
        var consensus = Consensus(
            With(Assessment("coach-a", 6, 6, 6, 6, 6, 6), a => a.PathwayBlocked = true),
            With(Assessment("coach-b", 6, 6, 6, 6, 6, 6), a => a.PathwayBlocked = false),
            // Not answered is not "no".
            Assessment("coach-c", 6, 6, 6, 6, 6, 6));

        Assert.Equal(new YesNo(1, 2), consensus.PathwayBlocked);
    }

    // -----------------------------------------------------------------------------------
    // How far off, and how long
    // -----------------------------------------------------------------------------------

    private static readonly DateOnly Cycle1 = new(2026, 1, 5);
    private static readonly DateOnly Cycle2 = Cycle1.AddDays(56);
    private static readonly DateOnly Cycle3 = Cycle2.AddDays(56);

    [Fact]
    public void Nothing_rated_is_no_data()
    {
        Assert.Equal(OutlookKind.NoData, SuccessionMath.Outlook(Array.Empty<(DateOnly, double)>(), Settings).Kind);
    }

    [Fact]
    public void At_or_above_ready_is_ready_now()
    {
        var outlook = SuccessionMath.Outlook(new[] { (Cycle1, 7.0), (Cycle2, 8.2) }, Settings);

        Assert.Equal(OutlookKind.ReadyNow, outlook.Kind);
        Assert.Equal(0, outlook.Gap);
    }

    [Fact]
    public void One_cycle_is_a_position_but_no_direction()
    {
        var outlook = SuccessionMath.Outlook(new[] { (Cycle1, 6.5) }, Settings);

        Assert.Equal(OutlookKind.NeedsMoreCycles, outlook.Kind);
        Assert.Equal(1.5, outlook.Gap);
        Assert.Null(outlook.Weeks);
    }

    [Fact]
    public void Weeks_to_ready_is_the_gap_over_the_weekly_trend_rounded_up()
    {
        // +0.5 a cycle, eight weeks a cycle: 0.0625 a week. From 7.0, the gap to 8 is one point,
        // which is 16 weeks.
        var outlook = SuccessionMath.Outlook(new[] { (Cycle1, 6.0), (Cycle2, 6.5), (Cycle3, 7.0) }, Settings);

        Assert.Equal(OutlookKind.Weeks, outlook.Kind);
        Assert.Equal(1.0, outlook.Gap!.Value, 3);
        Assert.Equal(0.5, outlook.ChangePerCycle!.Value, 3);
        Assert.Equal(16, outlook.Weeks);
    }

    [Fact]
    public void The_trend_is_fitted_through_every_cycle_not_the_last_two()
    {
        // A dip in the middle. The last two alone say +1.0 a cycle; the line through all three
        // says +0.25, because the player is only half a point further on than they started.
        var outlook = SuccessionMath.Outlook(new[] { (Cycle1, 6.5), (Cycle2, 6.0), (Cycle3, 7.0) }, Settings);

        Assert.Equal(0.25, outlook.ChangePerCycle!.Value, 3);
        Assert.Equal(32, outlook.Weeks);
    }

    [Fact]
    public void Flat_or_falling_says_not_closing_and_gives_no_date()
    {
        var falling = SuccessionMath.Outlook(new[] { (Cycle1, 7.0), (Cycle2, 6.5) }, Settings);
        var flat = SuccessionMath.Outlook(new[] { (Cycle1, 6.5), (Cycle2, 6.5) }, Settings);

        Assert.Equal(OutlookKind.NotClosing, falling.Kind);
        Assert.Equal(OutlookKind.NotClosing, flat.Kind);
        Assert.Null(falling.Weeks);
    }

    [Fact]
    public void Past_the_horizon_is_not_a_number()
    {
        // +0.1 a cycle is 0.0125 a week. Five points to go from 3.0 is four hundred weeks.
        var outlook = SuccessionMath.Outlook(new[] { (Cycle1, 2.9), (Cycle2, 3.0) }, Settings);

        Assert.Equal(OutlookKind.BeyondHorizon, outlook.Kind);
        Assert.Null(outlook.Weeks);
    }

    [Fact]
    public void History_in_any_order_gives_the_same_answer()
    {
        var ordered = SuccessionMath.Outlook(new[] { (Cycle1, 6.0), (Cycle2, 6.5), (Cycle3, 7.0) }, Settings);
        var shuffled = SuccessionMath.Outlook(new[] { (Cycle3, 7.0), (Cycle1, 6.0), (Cycle2, 6.5) }, Settings);

        Assert.Equal(ordered, shuffled);
    }

    // -----------------------------------------------------------------------------------
    // The best eleven
    // -----------------------------------------------------------------------------------

    private static FormationDefinition FourThreeThree => Catalog.FindFormation("4-3-3")!;

    [Theory]
    [InlineData(1, 7.0)]
    [InlineData(2, 6.5)]
    [InlineData(3, 6.0)]
    [InlineData(null, 5.0)]
    public void A_players_readiness_in_a_position_is_marked_down_by_how_far_from_their_own(int? rank, double expected)
    {
        // What each shirt on the pitch shows, and what the team rating averages: a natural in
        // full, a 2nd or 3rd position less, and a position nobody named for them the most.
        Assert.Equal(expected, SuccessionMath.PositionFit(7.0, rank, Settings), 3);
    }

    [Fact]
    public void The_rows_of_a_formation_are_attack_midfield_defence_and_the_keeper()
    {
        // The 3-5-2 as the file draws it: two strikers, two rows of midfield, three at the back.
        var units = Enumerable.Range(0, 5).Select(line => SuccessionMath.UnitOf(line, 5)).ToList();

        Assert.Equal(
            new[] { TeamUnit.Attack, TeamUnit.Midfield, TeamUnit.Midfield, TeamUnit.Defence, TeamUnit.Goalkeeper },
            units);
    }

    [Fact]
    public void A_natural_in_the_position_goes_ahead_of_a_slightly_better_player_covering()
    {
        var slots = SuccessionMath.PickEleven(FourThreeThree, new[]
        {
            Candidate(1, "TS-RB", 7.0, ("RB", 1)),
            // Higher overall, but RB is their 2nd position: 7.2 - 0.5 = 6.7 for the slot.
            Candidate(2, "TS-CB", 7.2, ("RCB", 1), ("RB", 2))
        }, Settings);

        Assert.Equal(1, Slot(slots, "RB").Starter!.Candidate.PlayerId);
        Assert.Equal(2, Slot(slots, "RCB").Starter!.Candidate.PlayerId);
    }

    [Fact]
    public void Nobody_plays_twice()
    {
        // One very good player named for three positions, and nobody else.
        var slots = SuccessionMath.PickEleven(FourThreeThree, new[]
        {
            Candidate(1, "TS-ALL", 9.0, ("LW", 1), ("CF", 2), ("RW", 3))
        }, Settings);

        Assert.Single(slots, s => s.Starter is not null);
        Assert.Equal(1, Slot(slots, "LW").Starter!.Candidate.PlayerId);
    }

    [Fact]
    public void A_slot_nobody_is_named_for_stays_empty()
    {
        var slots = SuccessionMath.PickEleven(FourThreeThree, new[]
        {
            Candidate(1, "TS-GK", 7.0, ("GK", 1))
        }, Settings);

        Assert.Equal(SuccessionRules.PlayersOnThePitch, slots.Count);
        Assert.NotNull(Slot(slots, "GK").Starter);
        Assert.Null(Slot(slots, "CF").Starter);
        Assert.Empty(Slot(slots, "CF").NextInLine);
    }

    [Fact]
    public void Next_in_line_is_the_best_of_the_rest_and_says_where_they_start_instead()
    {
        var slots = SuccessionMath.PickEleven(FourThreeThree, new[]
        {
            Candidate(1, "TS-A", 8.0, ("RB", 1)),
            Candidate(2, "TS-B", 7.5, ("LB", 1), ("RB", 2)),
            Candidate(3, "TS-C", 6.0, ("RB", 1)),
            Candidate(4, "TS-D", 5.0, ("RB", 1))
        }, Settings);

        var rb = Slot(slots, "RB");

        Assert.Equal(1, rb.Starter!.Candidate.PlayerId);
        Assert.Equal(SuccessionRules.NextInLine, rb.NextInLine.Count);

        // TS-B at 7.0 for the slot (7.5 minus the 2nd-position mark-down) is next, and starts at LB.
        Assert.Equal(2, rb.NextInLine[0].Pick.Candidate.PlayerId);
        Assert.Equal("LB", rb.NextInLine[0].StartsAt);
        Assert.Equal(3, rb.NextInLine[1].Pick.Candidate.PlayerId);
        Assert.Null(rb.NextInLine[1].StartsAt);
    }

    [Fact]
    public void The_same_ratings_always_give_the_same_team()
    {
        // Two identical players: the tie goes to the player code, so a reload never swaps them.
        var candidates = new[]
        {
            Candidate(2, "TS-02", 7.0, ("CF", 1)),
            Candidate(1, "TS-01", 7.0, ("CF", 1))
        };

        var first = SuccessionMath.PickEleven(FourThreeThree, candidates, Settings);
        var again = SuccessionMath.PickEleven(FourThreeThree, candidates.Reverse().ToArray(), Settings);

        Assert.Equal("TS-01", Slot(first, "CF").Starter!.Candidate.Code);
        Assert.Equal("TS-01", Slot(again, "CF").Starter!.Candidate.Code);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>Ratings in the file's order: physical, technical, tactical, mental, professionalism, availability.</summary>
    internal static SuccessionAssessment Assessment(string rater, params int[] ratings) => new()
    {
        RaterUserId = rater,
        Ratings = ratings
            .Select((value, index) => new SuccessionRating { RatingKey = Settings.Ratings[index].Key, Value = value })
            .ToList()
    };

    private static SuccessionAssessment With(SuccessionAssessment assessment, Action<SuccessionAssessment> change)
    {
        change(assessment);
        return assessment;
    }

    private static SuccessionAssessment Positions(SuccessionAssessment assessment, string? first, string? second, string? third)
    {
        assessment.FirstPosition = first;
        assessment.SecondPosition = second;
        assessment.ThirdPosition = third;
        return assessment;
    }

    private static PlayerConsensus Consensus(params SuccessionAssessment[] assessments) =>
        SuccessionMath.Consensus(assessments, Settings, Catalog.RiskSeverity);

    private static ElevenCandidate Candidate(int id, string code, double overall, params (string Key, int Rank)[] positions) =>
        new(id, code, overall, positions.Select(p => new PositionPreference(p.Key, p.Rank, 1, p.Rank == 1 ? 1 : 0)).ToList());

    private static SlotResult Slot(IEnumerable<SlotResult> slots, string position) =>
        slots.Single(s => s.Position == position);
}
