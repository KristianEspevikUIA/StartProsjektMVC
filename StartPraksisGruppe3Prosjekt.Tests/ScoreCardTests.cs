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

    /// <summary>
    /// The way back to the squad is above the player's code, and it lands on Player overview
    /// -- where the coach came from -- rather than on the team page's first tab. It used to be
    /// a button under the last section of a six-tab page.
    /// </summary>
    [Fact]
    public async Task The_way_back_is_at_the_top_and_opens_player_overview()
    {
        var html = await CoachPageAsync();

        Assert.Contains("class=\"sc-hero__back\"", html);
        Assert.Contains("#sc-panel-1\"", html);
        Assert.Contains("Test team · Player overview</a>", html);

        Assert.True(
            html.IndexOf("sc-hero__back", StringComparison.Ordinal)
            < html.IndexOf("<h1>", StringComparison.Ordinal),
            "The way back should come before the heading.");

        Assert.DoesNotContain("Back to Test team", html);
    }

    /// <summary>
    /// Three people who agree on a low score: a difference of 0.0 and a follow-up on every
    /// C. That read as a contradiction, so the notice and the badge both say the flag is the
    /// player's own average, with the number, and that it is not about disagreement.
    /// </summary>
    [Fact]
    public async Task Follow_up_says_it_is_the_players_own_score_and_not_disagreement()
    {
        await AnswerAsync(RespondentType.Player, StartCompassFactory.PlayerUserId, value: 1);
        await AnswerAsync(RespondentType.Guardian, StartCompassFactory.GuardianUserId, value: 1);
        await AnswerAsync(RespondentType.Coach, StartCompassFactory.CoachUserId, value: 1);

        // Whitespace flattened: the sentence is wrapped over several lines of the view.
        var html = System.Text.RegularExpressions.Regex.Replace(await CoachPageAsync(), @"\s+", " ");

        Assert.Contains("TS-TEST-01's own average is below 2 in", html);
        Assert.Contains("(1.0)", html);
        Assert.Contains("This is about a low score, not about disagreement.", html);
        Assert.Contains("href=\"/Help#follow-up\"", html);

        // On the card, beside the difference it is easy to read it as.
        Assert.Contains("Follow up · own 1.0", html);
        Assert.Contains("Difference 0.0 across everyone who answered.", html);
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
