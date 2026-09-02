using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The ten-statement form's scoring. Most of ScoringService is still a TODO, but ScoreOf is
/// finished on purpose -- it is the definition of the reversal rule, and the authorisation
/// and the 5C side both lean on that rule being written down once.
/// </summary>
public class ScoringServiceTests
{
    private static ScoringService Service(TestDatabase database) => new(database.NewContext());

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void A_normal_statement_scores_as_answered(int raw)
    {
        using var database = new TestDatabase();

        Assert.Equal(raw, Service(database).ScoreOf(new Item { Number = 1 }, raw));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    [InlineData(5, 1)]
    public void A_reversed_statement_is_flipped(int raw, int expected)
    {
        using var database = new TestDatabase();

        Assert.Equal(
            expected,
            Service(database).ScoreOf(new Item { Number = 5, IsReversed = true }, raw));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_value_off_the_scale_is_rejected(int raw)
    {
        using var database = new TestDatabase();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Service(database).ScoreOf(new Item { Number = 1 }, raw));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_two_forms_score_a_reversal_identically(int raw)
    {
        // Two questionnaires, one rule. If these ever disagree, one of them has written
        // "6 -" somewhere it should not have.
        using var database = new TestDatabase();

        Assert.Equal(
            FiveCRules.Score(raw, reversed: true),
            Service(database).ScoreOf(new Item { Number = 5, IsReversed = true }, raw));
    }
}
