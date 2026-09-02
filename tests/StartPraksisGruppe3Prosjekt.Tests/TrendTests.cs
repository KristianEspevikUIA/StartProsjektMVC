using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Development over time -- "Commitment went from 2.1 to 3.4".
///
/// The direction matters more than the number here: a sign error would tell a coach a player
/// is getting worse when they are getting better, and nothing on the page would look wrong.
/// </summary>
public sealed class TrendTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_player_answering_higher_later_shows_as_improvement()
    {
        var earlier = await AddPeriodAsync("Earlier period", closesInDays: -10);
        var later = await AddPeriodAsync("Later period", closesInDays: 10);

        await AnswerAsync(earlier, value: 2);
        await AnswerAsync(later, value: 4);

        var trend = await TrendAsync(earlier, later);

        Assert.True(trend.HasComparablePeriods);

        foreach (var category in trend.Categories)
        {
            // Every statement was answered the same way, so every category moves the same
            // distance. Reversed statements are scored before averaging, which is why a
            // straight "4 everywhere" does not average to 4.
            Assert.NotNull(category.Change);
            Assert.True(
                category.Change > 0,
                $"{category.CategoryName} should have improved, but changed by {category.Change}.");
        }
    }

    [Fact]
    public async Task One_period_gives_a_score_but_no_direction()
    {
        var only = await AddPeriodAsync("Only period", closesInDays: 5);
        await AnswerAsync(only, value: 3);

        var trend = await TrendAsync(only);

        Assert.False(trend.HasComparablePeriods);
        Assert.All(trend.Categories, c => Assert.Null(c.Change));
    }

    [Fact]
    public async Task Periods_are_ordered_oldest_first_whatever_order_they_arrive_in()
    {
        var earlier = await AddPeriodAsync("Earlier period", closesInDays: -20);
        var later = await AddPeriodAsync("Later period", closesInDays: 20);

        // Deliberately passed newest first.
        var trend = await TrendAsync(later, earlier);

        Assert.Equal("Earlier period", trend.Periods[0].Name);
        Assert.Equal("Later period", trend.Periods[1].Name);
    }

    private async Task<SurveyRound> AddPeriodAsync(string name, int closesInDays)
    {
        SurveyRound? created = null;

        await _factory.WithServicesAsync(async services =>
        {
            var periods = services.GetRequiredService<IPeriodService>();

            var result = await periods.CreateAsync(
                name,
                DateTimeOffset.UtcNow.AddDays(closesInDays - 30),
                DateTimeOffset.UtcNow.AddDays(closesInDays));

            Assert.True(result.Succeeded, string.Join(" ", result.Problems));
            created = result.Round;
        });

        return created!;
    }

    private Task AnswerAsync(SurveyRound round, int value) =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(new SurveySubmission
            {
                RoundId = round.Id,
                PlayerId = _factory.PlayerId,
                PlayerCode = "TS-TEST-01",
                RespondentRole = SurveySubmission.Roles.From(RespondentType.Player),
                RespondentUserId = StartCompassFactory.PlayerUserId,
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
            });
        });

    private async Task<PlayerTrend> TrendAsync(params SurveyRound[] rounds)
    {
        PlayerTrend? trend = null;

        await _factory.WithServicesAsync(async services =>
        {
            var analysis = services.GetRequiredService<IFiveCAnalysisService>();

            trend = await analysis.GetTrendAsync(
                _factory.PlayerId,
                "TS-TEST-01",
                rounds.Select(r => new TrendPeriod(r.Id, r.Name, r.ClosesAt)).ToList());
        });

        return trend!;
    }
}
