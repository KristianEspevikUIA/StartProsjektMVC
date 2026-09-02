using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The one thing in this application that must not be got wrong: a player does not see what
/// their coach said about them until the coach has released it.
///
/// The decision is made in the MODEL, not in a view, so that a new page or a forgotten
/// partial cannot leak the numbers. These tests assert exactly that -- the coach's figures
/// are absent from the view model, not merely hidden by the markup.
/// </summary>
public class FiveCFeedbackBuilderTests
{
    private const string PlayerUser = "player-1";
    private const string CoachUser = "coach-1";

    [Fact]
    public async Task Before_release_the_coach_numbers_are_not_in_the_model()
    {
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using var context = database.NewContext();
        var model = await BuildAsync(context, player, round);

        Assert.True(model.CoachHasAnswered);
        Assert.False(model.CoachAnswersReleased);

        Assert.All(model.Comparison!.Categories, category =>
        {
            Assert.Null(category.CoachMean);
            Assert.Equal(0, category.CoachAnswered);
        });

        Assert.Null(model.Comparison.CoachVsPlayer);
        Assert.False(model.Comparison.Differences.HasAny);
    }

    [Fact]
    public async Task The_player_still_learns_that_the_coach_has_answered()
    {
        // Knowing the coach HAS answered is exactly what the player is allowed to know at
        // this stage -- it is the third step of the conversation.
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using var context = database.NewContext();
        var model = await BuildAsync(context, player, round);

        Assert.NotNull(model.Comparison!.CoachSubmittedAt);
        Assert.True(model.Comparison.PlayerHasAnswered);
    }

    [Fact]
    public async Task After_release_the_coach_numbers_are_there()
    {
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using (var release = database.NewContext())
        {
            await new FeedbackReleaseService(release).ReleaseAsync(round.Id, player.Id, CoachUser);
        }

        await using var context = database.NewContext();
        var model = await BuildAsync(context, player, round);

        Assert.True(model.CoachAnswersReleased);
        Assert.All(model.Comparison!.Categories, category => Assert.NotNull(category.CoachMean));
        Assert.NotNull(model.Comparison.CoachVsPlayer);
    }

    [Fact]
    public async Task Withdrawing_hides_the_numbers_again_and_keeps_the_history()
    {
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using (var release = database.NewContext())
        {
            var releases = new FeedbackReleaseService(release);
            await releases.ReleaseAsync(round.Id, player.Id, CoachUser);
            await releases.WithdrawAsync(round.Id, player.Id, CoachUser);
        }

        await using var context = database.NewContext();
        var model = await BuildAsync(context, player, round);

        Assert.False(model.CoachAnswersReleased);
        Assert.All(model.Comparison!.Categories, category => Assert.Null(category.CoachMean));

        // A release that was later withdrawn is still a thing that happened.
        Assert.Equal(2, await context.FeedbackReleases.CountAsync());
    }

    [Fact]
    public async Task Silence_is_not_permission()
    {
        // No release event at all means not released. The default has to be the safe one.
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using var context = database.NewContext();

        Assert.False(await new FeedbackReleaseService(context).IsReleasedAsync(round.Id, player.Id));
    }

    [Fact]
    public async Task A_guardian_sees_exactly_what_the_player_sees()
    {
        using var database = new TestDatabase();
        var (player, round) = await ArrangeAsync(database);

        await using var context = database.NewContext();

        var asPlayer = await BuildAsync(context, player, round, viewerIsGuardian: false);
        var asGuardian = await BuildAsync(context, player, round, viewerIsGuardian: true);

        Assert.Equal(
            asPlayer.Comparison!.Categories.Select(c => c.CoachMean),
            asGuardian.Comparison!.Categories.Select(c => c.CoachMean));

        // What differs is whose form is outstanding, not what is visible.
        Assert.Equal(RespondentType.Guardian, asGuardian.FillRole);
        Assert.Equal(RespondentType.Player, asPlayer.FillRole);
        Assert.False(asGuardian.ViewerHasAnswered);
        Assert.True(asPlayer.ViewerHasAnswered);
    }

    /// <summary>A player who has answered, and a coach who has answered differently.</summary>
    private static async Task<(Player Player, SurveyRound Round)> ArrangeAsync(TestDatabase database)
    {
        var player = await database.AddPlayerAsync(userId: PlayerUser);
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Player, PlayerUser, value: 4));
        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Coach, CoachUser, value: 2));

        return (player, round);
    }

    private static Task<FiveCFeedbackViewModel> BuildAsync(
        AppDbContext context,
        Player player,
        SurveyRound round,
        bool viewerIsGuardian = false)
    {
        var builder = new FiveCFeedbackBuilder(
            new FiveCAnalysisService(new EfSurveySubmissionStore(context), TestCatalog.Load()),
            new FeedbackReleaseService(context),
            // Empty on purpose: this test is about redaction, not about the access log.
            // PlayerAccessLogTests covers that against a real database.
            new FakePlayerAccessLog());

        return builder.BuildAsync(
            player,
            round,
            Array.Empty<FiveCTeamViewModel.RoundOption>(),
            viewerIsGuardian);
    }
}
