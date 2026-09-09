using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The order the statements are put in front of a respondent.
///
/// Two properties have to hold at once, and they pull against each other: the same player
/// and period always get the same order, and different periods do not. Everything here is
/// one of those two, or the thing that makes both safe -- that nothing is lost in the
/// shuffle.
/// </summary>
public sealed class QuestionOrderTests
{
    private static readonly IQuestionOrder Order = new QuestionOrder(TestCatalog.Load());

    private static IReadOnlyList<string> KeysFor(int playerId, int roundId) =>
        Order.For(playerId, roundId).Select(q => q.Question.Key).ToList();

    /// <summary>
    /// Stable within a period. This is what makes a correction possible: somebody who saves
    /// and comes back to change one answer has to meet the same form.
    /// </summary>
    [Fact]
    public void The_same_player_and_period_always_get_the_same_order()
    {
        Assert.Equal(KeysFor(14, 2), KeysFor(14, 2));
    }

    /// <summary>
    /// Different between periods. Without this the shuffle is decoration -- the same order
    /// every September is the same autopilot as catalog order, one permutation along.
    /// </summary>
    [Fact]
    public void A_new_period_reshuffles_the_form()
    {
        Assert.NotEqual(KeysFor(14, 2), KeysFor(14, 3));
    }

    /// <summary>
    /// And different between players, so a coach filling in twenty forms in a row is not
    /// answering the same sequence twenty times.
    /// </summary>
    [Fact]
    public void Two_players_in_the_same_period_get_different_orders()
    {
        Assert.NotEqual(KeysFor(14, 2), KeysFor(15, 2));
    }

    /// <summary>
    /// The seed folds the two ids rather than adding them. Player 3 in period 4 and player
    /// 4 in period 3 are different forms; a plain sum would hand them the same order.
    /// </summary>
    [Fact]
    public void Swapping_the_player_and_the_period_is_a_different_order()
    {
        Assert.NotEqual(KeysFor(3, 4), KeysFor(4, 3));
    }

    /// <summary>
    /// Nothing is lost and nothing is duplicated. A shuffle that dropped a statement would
    /// be a form that cannot be submitted -- the validation would point at a statement
    /// nobody was shown.
    /// </summary>
    [Fact]
    public void Every_question_appears_exactly_once()
    {
        var catalog = TestCatalog.Load();
        var ordered = Order.For(14, 2);

        Assert.Equal(
            catalog.Questions.AllQuestions.Select(q => q.Key).OrderBy(k => k, StringComparer.Ordinal),
            ordered.Select(q => q.Question.Key).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// The running number is the position on THIS form, from 1, with no gaps. It is what
    /// the page prints next to each statement.
    /// </summary>
    [Fact]
    public void The_numbers_run_from_one_without_gaps()
    {
        var ordered = Order.For(14, 2);

        Assert.Equal(Enumerable.Range(1, ordered.Count), ordered.Select(q => q.Number));
    }

    /// <summary>
    /// Every statement carries its own category and its own colour. The form no longer
    /// groups by category, so this is the only thing left saying which C a statement is.
    /// </summary>
    [Fact]
    public void Every_statement_carries_its_category_and_colour()
    {
        var catalog = TestCatalog.Load();

        foreach (var item in Order.For(14, 2))
        {
            Assert.Equal(
                catalog.FindCategoryForQuestion(item.Question.Key)!.Key,
                item.Category.Key);

            Assert.Equal(catalog.ColorForQuestion(item.Question.Key), item.Color);
        }
    }

    /// <summary>
    /// Not catalog order. Stated as its own case because every other test here passes on a
    /// shuffle that quietly stopped shuffling.
    /// </summary>
    [Fact]
    public void The_order_is_not_the_catalog_order()
    {
        var catalogOrder = TestCatalog.Load().Questions.AllQuestions.Select(q => q.Key).ToList();

        Assert.NotEqual(catalogOrder, KeysFor(14, 2));
    }
}
