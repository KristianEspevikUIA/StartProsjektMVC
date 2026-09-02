using Microsoft.Extensions.Logging.Abstractions;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Periods. Both the admin page and the seeding go through this service, so the rules for
/// what makes a usable period are tested here rather than once per caller.
/// </summary>
public class PeriodServiceTests
{
    private static readonly DateTimeOffset Opens = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closes = new(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Creating_a_period_stores_it_in_utc()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        // Npgsql maps DateTimeOffset to timestamptz and rejects any offset but zero, so the
        // conversion has to happen before the driver sees it.
        var result = await periods.CreateAsync(
            "Autumn 2026",
            new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.FromHours(2)),
            Closes);

        Assert.True(result.Succeeded);
        Assert.Equal(TimeSpan.Zero, result.Round!.OpensAt.Offset);
        Assert.Equal(TimeSpan.Zero, result.Round.ClosesAt.Offset);
    }

    [Fact]
    public async Task A_period_name_is_trimmed()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        var result = await periods.CreateAsync("  Autumn 2026  ", Opens, Closes);

        Assert.Equal("Autumn 2026", result.Round!.Name);
    }

    [Fact]
    public async Task A_period_needs_a_name()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        var result = await periods.CreateAsync("   ", Opens, Closes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Contains("name"));
    }

    [Fact]
    public async Task Two_periods_cannot_share_a_name()
    {
        // Two periods with the same name are indistinguishable in every list in the app, and
        // answers would silently split between them.
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        await periods.CreateAsync("Autumn 2026", Opens, Closes);
        var second = await periods.CreateAsync("Autumn 2026", Opens, Closes);

        Assert.False(second.Succeeded);
        Assert.Contains(second.Problems, p => p.Contains("Autumn 2026"));
    }

    [Fact]
    public async Task A_period_cannot_close_before_it_opens()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        var result = await periods.CreateAsync("Backwards", Closes, Opens);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Contains("after"));
    }

    [Fact]
    public async Task The_current_period_is_the_open_one_closing_last()
    {
        using var database = new TestDatabase();
        var now = DateTimeOffset.UtcNow;

        await database.AddRoundAsync("Closed", now.AddDays(-60), now.AddDays(-30));
        await database.AddRoundAsync("Open, closing soon", now.AddDays(-10), now.AddDays(3));
        var expected = await database.AddRoundAsync("Open, closing later", now.AddDays(-1), now.AddDays(30));

        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        // More than one open period is normal when a new one starts before the previous has
        // closed. The form lands on the one that closes last, so a new period takes over as
        // soon as it opens.
        Assert.Equal(expected.Id, (await periods.GetCurrentAsync())!.Id);
    }

    [Fact]
    public async Task With_nothing_open_the_current_period_is_the_most_recent()
    {
        using var database = new TestDatabase();
        var now = DateTimeOffset.UtcNow;

        await database.AddRoundAsync("Older", now.AddDays(-90), now.AddDays(-60));
        var expected = await database.AddRoundAsync("Newer", now.AddDays(-60), now.AddDays(-30));

        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        Assert.Equal(expected.Id, (await periods.GetCurrentAsync())!.Id);
    }

    [Fact]
    public async Task With_no_periods_at_all_there_is_no_current_one()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        Assert.Null(await periods.GetCurrentAsync());
    }

    [Fact]
    public async Task Closing_a_period_keeps_the_answers()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);
        var periods = new PeriodService(context, store);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Player, "player-1"));

        var result = await periods.CloseNowAsync(round.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Round!.IsOpenAt(DateTimeOffset.UtcNow));
        Assert.Single(await store.GetForPlayerAsync(round.Id, player.Id));
    }

    [Fact]
    public async Task Closing_a_closed_period_says_so()
    {
        using var database = new TestDatabase();
        var now = DateTimeOffset.UtcNow;
        var round = await database.AddRoundAsync("Closed", now.AddDays(-60), now.AddDays(-30));

        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        var result = await periods.CloseNowAsync(round.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Contains("Closed"));
    }

    [Fact]
    public async Task Closing_a_period_that_does_not_exist_says_so()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();
        var periods = new PeriodService(context, new EfSurveySubmissionStore(context));

        var result = await periods.CloseNowAsync(4242);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Contains("does not exist"));
    }

    [Fact]
    public async Task Submission_counts_cover_every_period_including_the_empty_ones()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var answered = await database.AddOpenRoundAsync("Answered");
        var empty = await database.AddRoundAsync(
            "Empty", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);
        var periods = new PeriodService(context, store);

        await store.SaveAsync(Submissions.Filled(
            catalog, answered.Id, player.Id, player.Code, RespondentType.Player, "player-1"));
        await store.SaveAsync(Submissions.Filled(
            catalog, answered.Id, player.Id, player.Code, RespondentType.Coach, "coach-1"));

        var counts = await periods.GetSubmissionCountsAsync();

        Assert.Equal(2, counts[answered.Id]);
        Assert.Equal(0, counts[empty.Id]);
    }

    [Fact]
    public async Task Submission_counts_take_one_call_however_many_periods_there_are()
    {
        // This used to be one store call per period, each one reading every submission with
        // all twenty-five of its answers only to count the list. Against the PostgREST store
        // that was two HTTP requests per period.
        using var database = new TestDatabase();

        for (var i = 0; i < 5; i++)
        {
            await database.AddRoundAsync(
                $"Period {i}",
                DateTimeOffset.UtcNow.AddDays(-30 - i),
                DateTimeOffset.UtcNow.AddDays(30 - i));
        }

        await using var context = database.NewContext();
        var store = new CountingStore(new EfSurveySubmissionStore(context));
        var periods = new PeriodService(context, store);

        var counts = await periods.GetSubmissionCountsAsync();

        Assert.Equal(5, counts.Count);
        Assert.Equal(1, store.CountByRoundCalls);
        Assert.Equal(0, store.ReadCalls);
    }

    /// <summary>Wraps a store and remembers how it was called.</summary>
    private sealed class CountingStore : ISurveySubmissionStore
    {
        private readonly ISurveySubmissionStore _inner;

        public CountingStore(ISurveySubmissionStore inner) => _inner = inner;

        public int CountByRoundCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public string Description => _inner.Description;

        public Task SaveAsync(SurveySubmission submission, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(submission, cancellationToken);

        public Task<SurveySubmission?> FindAsync(
            int roundId, int playerId, string respondentUserId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return _inner.FindAsync(roundId, playerId, respondentUserId, cancellationToken);
        }

        public Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
            int roundId, int playerId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return _inner.GetForPlayerAsync(roundId, playerId, cancellationToken);
        }

        public Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
            int roundId, IEnumerable<int> playerIds, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return _inner.GetForPlayersAsync(roundId, playerIds, cancellationToken);
        }

        public Task<IReadOnlyDictionary<int, int>> CountByRoundAsync(
            IEnumerable<int> roundIds, CancellationToken cancellationToken = default)
        {
            CountByRoundCalls++;
            return _inner.CountByRoundAsync(roundIds, cancellationToken);
        }
    }
}

/// <summary>The real question set, loaded once per test run.</summary>
internal static class TestCatalog
{
    private static readonly Lazy<IQuestionCatalog> Instance = new(() =>
        new QuestionCatalog(
            new FakeWebHostEnvironment(AppContext.BaseDirectory),
            NullLogger<QuestionCatalog>.Instance));

    public static IQuestionCatalog Load() => Instance.Value;
}
