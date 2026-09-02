using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The scoring rules, tested without a web host.
///
/// These are small, pure and easy to get subtly wrong -- and two of them are rules the
/// coaching team argued about, so a change here should be a deliberate one rather than
/// something that slips through.
/// </summary>
public class FiveCRuleTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void A_normal_statement_scores_as_answered(int answer, int expected) =>
        Assert.Equal(expected, FiveCRules.Score(answer, reversed: false));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    [InlineData(5, 1)]
    public void A_reversed_statement_is_scored_as_six_minus_the_answer(int answer, int expected) =>
        // "I am afraid of making mistakes" answered 5 is a LOW score. The form shows the
        // statement as written and the reversal happens here, once.
        Assert.Equal(expected, FiveCRules.Score(answer, reversed: true));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void An_answer_outside_the_scale_is_rejected(int answer) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FiveCRules.Score(answer, reversed: false));

    [Fact]
    public void Follow_up_needs_a_mean_below_two() =>
        Assert.True(FiveCRules.NeedsFollowUp(1.8, answeredQuestions: 5));

    [Fact]
    public void A_mean_of_exactly_two_is_not_followed_up() =>
        // The threshold is "below 2", not "2 or below". A player answering Disagree to
        // everything lands exactly on 2, and that is the boundary the club chose.
        Assert.False(FiveCRules.NeedsFollowUp(2.0, answeredQuestions: 5));

    [Fact]
    public void Follow_up_needs_enough_answers_behind_it() =>
        // One low answer is a bad day. The flag is meant to mean "consistently low", and
        // that is what the minimum count is for.
        Assert.False(FiveCRules.NeedsFollowUp(1.0, answeredQuestions: 1));

    [Fact]
    public void An_unanswered_category_is_not_followed_up() =>
        Assert.False(FiveCRules.NeedsFollowUp(null, answeredQuestions: 0));

    [Theory]
    [InlineData(0.0, AgreementLevel.Agree)]
    [InlineData(0.44, AgreementLevel.Agree)]
    [InlineData(0.6, AgreementLevel.SomeDifference)]
    [InlineData(1.4, AgreementLevel.LargeDifference)]
    public void Differences_band_the_way_the_page_prints_them(double difference, AgreementLevel expected) =>
        Assert.Equal(expected, FiveCRules.LevelOf(difference));

    [Fact]
    public void A_difference_that_prints_as_half_a_point_bands_as_half_a_point() =>
        // 0.45 prints as "0,5" and must band as 0.5, not as 0.4. Banding on the unrounded
        // number would put a visible contradiction on the page.
        Assert.Equal(AgreementLevel.SomeDifference, FiveCRules.LevelOf(0.45));
}
