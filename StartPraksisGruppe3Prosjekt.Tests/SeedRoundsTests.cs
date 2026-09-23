using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The period seeding, run on its own.
///
/// One rule is worth holding here: after this step there is somewhere to answer. The
/// placeholder exists so that a database nobody has configured yet still has an open
/// period in it, and it used to stop being one. Seeded with a window of a few weeks, it
/// closed itself a few weeks later, and because the step only ever asked whether a period
/// of that NAME existed, every later start skipped straight past it.
/// </summary>
public sealed class SeedRoundsTests : IDisposable
{
    private readonly TestDatabase _db = new();

    private static string Placeholder => $"Autumn {DateTimeOffset.UtcNow.Year}";

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_fresh_database_gets_one_open_placeholder()
    {
        await SeedAsync();

        var round = await SingleRoundAsync();

        Assert.Equal(Placeholder, round.Name);
        Assert.True(round.IsOpenAt(DateTimeOffset.UtcNow), "The placeholder should be open.");
    }

    /// <summary>
    /// The bug this step had. A database seeded in August had no open period in September
    /// and no way back to one short of the admin page or SQL.
    /// </summary>
    [Fact]
    public async Task An_expired_placeholder_is_reopened_when_nothing_else_is_open()
    {
        var now = DateTimeOffset.UtcNow;

        await _db.AddRoundAsync(Placeholder, now.AddDays(-60), now.AddDays(-32));

        await SeedAsync();

        var round = await SingleRoundAsync();

        Assert.True(round.IsOpenAt(now), "An expired placeholder should be reopened.");

        // Reopened, not replaced: the row keeps its id, so answers already in it stay in it.
        Assert.Equal(Placeholder, round.Name);
    }

    /// <summary>
    /// The other half of the same rule. Closing a period from Admin/Periods is a decision,
    /// and a seed step that undid it on the next start would be worse than the expiry it
    /// was written to fix.
    /// </summary>
    [Fact]
    public async Task A_closed_placeholder_is_left_alone_while_another_period_is_open()
    {
        var now = DateTimeOffset.UtcNow;
        var closedAt = now.AddDays(-2);

        await _db.AddRoundAsync(Placeholder, now.AddDays(-60), closedAt);

        // A real period, with an answer in it so the clean-up step leaves it standing.
        var real = await _db.AddRoundAsync("Meso 3", now.AddDays(-3), now.AddDays(25));
        await AddAnswerAsync(real.Id);

        await SeedAsync();

        await using var context = _db.NewContext();
        var placeholder = await context.SurveyRounds.SingleAsync(r => r.Name == Placeholder);

        Assert.False(placeholder.IsOpenAt(now), "A deliberately closed placeholder should stay closed.");
        Assert.Equal(closedAt.ToUnixTimeSeconds(), placeholder.ClosesAt.ToUnixTimeSeconds());
    }

    /// <summary>
    /// The placeholder is not the only thing that can be open. A club running its own
    /// periods should never see this step touch anything.
    /// </summary>
    [Fact]
    public async Task An_expired_placeholder_stays_closed_while_a_real_period_is_open()
    {
        var now = DateTimeOffset.UtcNow;

        await _db.AddRoundAsync(Placeholder, now.AddDays(-90), now.AddDays(-60));

        var real = await _db.AddRoundAsync("Meso 3", now.AddDays(-3), now.AddDays(25));
        await AddAnswerAsync(real.Id);

        await SeedAsync();

        await using var context = _db.NewContext();

        Assert.False(
            (await context.SurveyRounds.SingleAsync(r => r.Name == Placeholder)).IsOpenAt(now),
            "With a real period open, the placeholder has nothing left to do.");
    }

    private async Task SeedAsync()
    {
        await using var context = _db.NewContext();

        await SeedData.SeedRoundsAsync(context, NullLogger.Instance);
    }

    private async Task<SurveyRound> SingleRoundAsync()
    {
        await using var context = _db.NewContext();

        return await context.SurveyRounds.SingleAsync();
    }

    /// <summary>
    /// One answer, so RemoveEmptyRoundsExceptAsync leaves the period standing. An empty
    /// period that is not the placeholder is removed by the same step under test.
    /// </summary>
    private async Task AddAnswerAsync(int roundId)
    {
        var player = await _db.AddPlayerAsync();

        await using var context = _db.NewContext();

        context.FiveCSubmissions.Add(new FiveCSubmission
        {
            RoundId = roundId,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = "player",
            RespondentUserId = "user-seed-test",
            QuestionSetVersion = "test",
            SubmittedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();
    }
}
