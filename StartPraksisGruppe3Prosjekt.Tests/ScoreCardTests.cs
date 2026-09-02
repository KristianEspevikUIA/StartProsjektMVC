using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The difference cards at the top of the coach's player page.
///
/// There are four pairs worth naming and three of them involve the player. The fourth --
/// coach and guardian -- is the two adults around the player disagreeing with each other,
/// which is a different conversation from either of them disagreeing with the player.
///
/// The titles are asserted because they used to be derived from a css class name, so a new
/// pair silently got the wrong heading. They are now built from the pair itself.
/// </summary>
public sealed class ScoreCardTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task All_four_pairs_are_shown_when_everyone_has_answered()
    {
        await AnswerAsync(RespondentType.Player, StartCompassFactory.PlayerUserId, value: 5);
        await AnswerAsync(RespondentType.Guardian, StartCompassFactory.GuardianUserId, value: 3);
        await AnswerAsync(RespondentType.Coach, StartCompassFactory.CoachUserId, value: 2);

        var html = await CoachPageAsync();

        Assert.Contains("Player and coach", html);
        Assert.Contains("Player and guardian", html);
        Assert.Contains("Coach and guardian", html);
        Assert.Contains("Between all three", html);
    }

    [Fact]
    public async Task The_coach_and_guardian_card_keeps_its_title_when_it_cannot_be_measured()
    {
        // Only the coach has answered, so there is no guardian to compare against. The card
        // still has to be there and still has to be called the same thing -- a page that
        // renames its own headings as answers arrive is a page nobody trusts.
        await AnswerAsync(RespondentType.Player, StartCompassFactory.PlayerUserId, value: 4);
        await AnswerAsync(RespondentType.Coach, StartCompassFactory.CoachUserId, value: 4);

        var html = await CoachPageAsync();

        Assert.Contains("Coach and guardian", html);
        Assert.Contains("No guardian has answered", html);
    }

    [Fact]
    public async Task The_coach_and_guardian_card_says_which_of_the_two_is_missing()
    {
        // Neither adult has answered. Saying "no guardian has answered" here would be true
        // but misleading -- the coach has not answered either.
        await AnswerAsync(RespondentType.Player, StartCompassFactory.PlayerUserId, value: 4);

        var html = await CoachPageAsync();

        Assert.Contains("Neither a coach nor a guardian has answered", html);
    }

    [Fact]
    public async Task The_player_page_is_panelled_like_every_other_page()
    {
        await AnswerAsync(RespondentType.Player, StartCompassFactory.PlayerUserId, value: 4);

        var html = await CoachPageAsync();

        // The same component the team page and the form use, so a coach moving between
        // them does not meet a different navigation on each.
        Assert.Contains("data-tab-label=\"Differences\"", html);
        // The apostrophe is encoded; match up to it rather than guess the entity.
        Assert.Contains("data-tab-label=\"The five C", html);
        Assert.Contains("data-tab-label=\"Statements\"", html);

        // Both halves of sharing are one panel: two adjacent tabs called "Sharing" and
        // "Share the form" would be a coin toss every time.
        Assert.Contains("data-tab-label=\"Sharing\"", html);
        Assert.DoesNotContain("data-tab-label=\"Share the form\"", html);

        // Nothing is hidden server side. With JavaScript off this is the page it was.
        Assert.DoesNotContain("sc-tabs", html);
    }

    private async Task<string> CoachPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }

    private Task AnswerAsync(RespondentType role, string userId, int value) =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(Submissions.Filled(
                catalog,
                _factory.RoundId,
                _factory.PlayerId,
                "TS-TEST-01",
                role,
                userId,
                value));
        });
}
