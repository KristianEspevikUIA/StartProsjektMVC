using StartPraksisGruppe3Prosjekt.Models.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The scoring rules. Nothing here touches a database, and nothing here should ever need to:
/// these are the numbers every 5C view reads, and "do not write 6 - value anywhere else" is
/// only enforceable if this one place is right.
/// </summary>
public class FiveCRulesTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void Score_leaves_a_normal_statement_alone(int raw, int expected) =>
        Assert.Equal(expected, FiveCRules.Score(raw, reversed: false));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    public void Score_flips_a_reversed_statement(int raw, int expected) =>
        Assert.Equal(expected, FiveCRules.Score(raw, reversed: true));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Score_rejects_a_value_off_the_scale(int raw) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FiveCRules.Score(raw, reversed: false));

    [Fact]
    public void Follow_up_needs_a_mean_at_all()
    {
        // A category nobody answered is not a category answered badly.
        Assert.False(FiveCRules.NeedsFollowUp(null, answeredQuestions: 5));
    }

    [Fact]
    public void Follow_up_fires_on_a_low_mean_with_enough_answers() =>
        Assert.True(FiveCRules.NeedsFollowUp(1.6, FiveCRules.MinimumAnswersForFollowUp));

    [Fact]
    public void Follow_up_does_not_fire_on_too_few_answers()
    {
        // One bad answer is a bad day. The flag is for scoring low consistently.
        Assert.False(FiveCRules.NeedsFollowUp(1.0, FiveCRules.MinimumAnswersForFollowUp - 1));
    }

    [Fact]
    public void Follow_up_is_strictly_below_the_threshold() =>
        Assert.False(FiveCRules.NeedsFollowUp(FiveCRules.FollowUpThreshold, answeredQuestions: 5));

    [Theory]
    [InlineData(0.0, AgreementLevel.Agree)]
    [InlineData(0.44, AgreementLevel.Agree)]
    [InlineData(0.5, AgreementLevel.SomeDifference)]
    [InlineData(0.94, AgreementLevel.SomeDifference)]
    [InlineData(1.0, AgreementLevel.LargeDifference)]
    [InlineData(4.0, AgreementLevel.LargeDifference)]
    public void LevelOf_bands_a_difference(double difference, AgreementLevel expected) =>
        Assert.Equal(expected, FiveCRules.LevelOf(difference));

    [Theory]
    [InlineData(0.45, AgreementLevel.SomeDifference)]
    [InlineData(0.95, AgreementLevel.LargeDifference)]
    public void LevelOf_rounds_the_way_the_page_prints(double difference, AgreementLevel expected)
    {
        // The band sits next to the number, and the number is printed to one decimal with
        // halves going away from zero. Banding 0.45 as "close agreement" while the page
        // says "0,5" would put a contradiction on screen -- which is the whole reason
        // LevelOf rounds before it compares.
        Assert.Equal(expected, FiveCRules.LevelOf(difference));
        Assert.Equal(
            Math.Round(difference, 1, MidpointRounding.AwayFromZero),
            double.Parse(difference.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1.0, ScoreLevel.Low)]
    [InlineData(1.94, ScoreLevel.Low)]
    [InlineData(2.0, ScoreLevel.Middling)]
    [InlineData(3.44, ScoreLevel.Middling)]
    [InlineData(3.5, ScoreLevel.Strong)]
    [InlineData(5.0, ScoreLevel.Strong)]
    public void LevelOfScore_bands_a_mean(double mean, ScoreLevel expected) =>
        Assert.Equal(expected, FiveCRules.LevelOfScore(mean));

    [Theory]
    [InlineData(1.95, ScoreLevel.Middling)]
    [InlineData(3.45, ScoreLevel.Strong)]
    public void LevelOfScore_rounds_the_way_the_page_prints(double mean, ScoreLevel expected) =>
        // Same rule as LevelOf, and for the same reason: the colour sits on the number, and
        // the number is printed to one decimal. Colouring 3.45 as "on the way" while the
        // page prints "3,5" beside a legend that says 3.5 is a strength is a contradiction
        // a reader can see.
        Assert.Equal(expected, FiveCRules.LevelOfScore(mean));

    /// <summary>
    /// The red band and the follow-up flag are the same line, deliberately. A number the
    /// overview colours red while the page's own notice says the squad is fine would be the
    /// page contradicting itself -- so the two read off one constant, and this is what says
    /// so if somebody gives the colour a threshold of its own.
    /// </summary>
    [Fact]
    public void The_red_band_starts_exactly_where_the_follow_up_flag_does()
    {
        Assert.Equal(
            ScoreLevel.Low,
            FiveCRules.LevelOfScore(FiveCRules.FollowUpThreshold - 0.1));

        Assert.Equal(
            ScoreLevel.Middling,
            FiveCRules.LevelOfScore(FiveCRules.FollowUpThreshold));

        Assert.True(FiveCRules.NeedsFollowUp(
            FiveCRules.FollowUpThreshold - 0.1,
            FiveCRules.MinimumAnswersForFollowUp));

        Assert.False(FiveCRules.NeedsFollowUp(
            FiveCRules.FollowUpThreshold,
            FiveCRules.MinimumAnswersForFollowUp));
    }

    /// <summary>
    /// The two colour scales on the coach overview are separate enums on purpose: one bands
    /// a squad's SCORE, the other how far apart two people are. They are both red-amber-
    /// green, they appear on the same page, and a single enum for both would eventually
    /// colour a difference as though it were a score. Reading 4.0 both ways is the case
    /// that shows they are not interchangeable -- a mean of 4.0 is a strength, a difference
    /// of 4.0 is as far apart as two people can be.
    /// </summary>
    [Fact]
    public void A_score_and_a_difference_are_not_banded_by_the_same_rule()
    {
        Assert.Equal(ScoreLevel.Strong, FiveCRules.LevelOfScore(4.0));
        Assert.Equal(AgreementLevel.LargeDifference, FiveCRules.LevelOf(4.0));

        // And they do not share a class name either, so a stylesheet cannot merge them
        // back together by accident.
        Assert.DoesNotContain(
            AgreementLevels.CssSuffix(AgreementLevel.LargeDifference),
            ScoreLevels.ScoreClass(ScoreLevel.Low));
    }
}
