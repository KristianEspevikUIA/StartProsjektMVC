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
}
