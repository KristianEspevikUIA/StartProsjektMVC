using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Models;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The three append-only logs: consent, who looked at a player, and the coach releasing
/// their answers. Each of them is documentation of what was allowed and what happened, and
/// a log that can be edited documents nothing.
///
/// The guard lives in AppDbContext.SaveChanges rather than in the services, so that it holds
/// whoever does the writing. These tests write straight to the context on purpose -- going
/// through a service would test the service, not the guard.
/// </summary>
public class AppendOnlyLogTests
{
    [Fact]
    public async Task Consent_events_can_be_added()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using var context = database.NewContext();

        context.ConsentEvents.Add(new ConsentEvent
        {
            PlayerId = player.Id,
            Level = ConsentLevel.Full,
            ChangedByUserId = "guardian-1",
            OccurredAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.ConsentEvents.CountAsync());
    }

    [Fact]
    public async Task Editing_a_consent_event_throws()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var arrange = database.NewContext())
        {
            arrange.ConsentEvents.Add(new ConsentEvent
            {
                PlayerId = player.Id,
                Level = ConsentLevel.Full,
                ChangedByUserId = "guardian-1",
                OccurredAt = DateTimeOffset.UtcNow
            });

            await arrange.SaveChangesAsync();
        }

        await using var context = database.NewContext();
        var stored = await context.ConsentEvents.SingleAsync();

        // Withdrawing consent is a NEW event at a lower level, never an edit of the old one.
        stored.Level = ConsentLevel.None;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Contains("ConsentEvent", error.Message);
    }

    [Fact]
    public async Task Deleting_a_player_access_event_throws()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var arrange = database.NewContext())
        {
            arrange.PlayerAccessEvents.Add(new PlayerAccessEvent
            {
                PlayerId = player.Id,
                ViewedByUserId = "coach-1",
                ViewedByRole = "Coach",
                Context = "Coach/FiveCPlayer",
                OccurredAt = DateTimeOffset.UtcNow
            });

            await arrange.SaveChangesAsync();
        }

        await using var context = database.NewContext();
        context.PlayerAccessEvents.Remove(await context.PlayerAccessEvents.SingleAsync());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Contains("PlayerAccessEvent", error.Message);
    }

    [Fact]
    public async Task Editing_a_feedback_release_throws()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();

        await using (var arrange = database.NewContext())
        {
            arrange.FeedbackReleases.Add(new FeedbackRelease
            {
                RoundId = round.Id,
                PlayerId = player.Id,
                CoachUserId = "coach-1",
                IsReleased = true,
                OccurredAt = DateTimeOffset.UtcNow
            });

            await arrange.SaveChangesAsync();
        }

        await using var context = database.NewContext();
        var stored = await context.FeedbackReleases.SingleAsync();

        // Withdrawing is a new row with IsReleased = false. The release still happened.
        stored.IsReleased = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_guard_survives_the_parameterless_SaveChanges()
    {
        // SaveChanges() calls through to SaveChanges(bool), which is the overload the guard
        // is on. Overriding only the parameterless one would leave SaveChanges(false) open.
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var arrange = database.NewContext())
        {
            arrange.ConsentEvents.Add(new ConsentEvent
            {
                PlayerId = player.Id,
                Level = ConsentLevel.Aggregated,
                ChangedByUserId = "admin-1",
                OccurredAt = DateTimeOffset.UtcNow
            });

            arrange.SaveChanges();
        }

        await using var context = database.NewContext();
        var stored = context.ConsentEvents.Single();
        stored.Level = ConsentLevel.Full;

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }
}
