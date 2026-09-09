using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The conversation: the player is told the coach HAS answered, and sees what they answered
/// only once the coach shares it.
///
/// This is the rule that is easiest to break by accident. Any new page, partial or debug
/// dump that renders the comparison would leak it, and nothing about the code makes that
/// obvious. So it is asserted at the HTTP level, on the page a player actually opens.
/// </summary>
public sealed class FeedbackReleaseTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public async Task InitializeAsync()
    {
        await _factory.InitialiseAsync();
        await SubmitAnswersForBothAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Player_is_told_the_coach_answered_but_not_what()
    {
        var html = await PlayerPageAsync();

        Assert.Contains("has not shared their answers yet", html);

        // Structural, not just wording: the comparison chart is what carries the coach's
        // numbers, and it must not be on the page at all before the coach shares.
        Assert.DoesNotContain("sc-legend__swatch--coach", html);
    }

    [Fact]
    public async Task Player_sees_the_comparison_once_the_coach_shares()
    {
        await ReleaseAsync(release: true);

        var html = await PlayerPageAsync();

        Assert.Contains("The coach has shared their answers", html);
        Assert.Contains("sc-legend__swatch--coach", html);
    }

    [Fact]
    public async Task Withdrawing_hides_the_comparison_again()
    {
        await ReleaseAsync(release: true);
        await ReleaseAsync(release: false);

        var html = await PlayerPageAsync();

        Assert.DoesNotContain("sc-legend__swatch--coach", html);
    }

    [Fact]
    public async Task Guardian_sees_the_same_as_the_player()
    {
        await ReleaseAsync(release: true);

        var response = await _factory
            .ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian)
            .GetAsync($"/Guardian/Player/{_factory.PlayerId}");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("The coach has shared their answers", html);
    }

    private async Task<string> PlayerPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.PlayerUserId, Roles.Player)
            .GetAsync("/Player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    private Task ReleaseAsync(bool release) =>
        _factory.WithServicesAsync(async services =>
        {
            var releases = services.GetRequiredService<IFeedbackReleaseService>();

            if (release)
            {
                await releases.ReleaseAsync(
                    _factory.RoundId, _factory.PlayerId, StartCompassFactory.CoachUserId);
            }
            else
            {
                await releases.WithdrawAsync(
                    _factory.RoundId, _factory.PlayerId, StartCompassFactory.CoachUserId);
            }
        });

    /// <summary>
    /// One submission from the player and one from the coach, so there is something to
    /// compare. Written through the store rather than by posting the form: the form is
    /// covered separately, and antiforgery would make this setup about tokens.
    /// </summary>
    private Task SubmitAnswersForBothAsync() =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(Build(catalog, RespondentType.Player, StartCompassFactory.PlayerUserId, 4));
            await store.SaveAsync(Build(catalog, RespondentType.Coach, StartCompassFactory.CoachUserId, 2));
        });

    private SurveySubmission Build(
        IQuestionCatalog catalog,
        RespondentType respondent,
        string userId,
        int value) => new()
    {
        RoundId = _factory.RoundId,
        PlayerId = _factory.PlayerId,
        PlayerCode = "TS-TEST-01",
        RespondentRole = SurveySubmission.Roles.From(respondent),
        RespondentUserId = userId,
        QuestionSetVersion = catalog.Questions.Version,
        SubmittedAt = DateTimeOffset.UtcNow,
        Answers = catalog.Questions.Categories
            .SelectMany(c => c.Questions.Select(q => new SurveyAnswer
            {
                QuestionKey = q.Key,
                CategoryKey = c.Key,
                Value = value
            }))
            .ToList()
    };
}
