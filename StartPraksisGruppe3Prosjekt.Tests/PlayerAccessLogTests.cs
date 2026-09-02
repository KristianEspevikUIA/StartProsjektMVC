using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The audit log. It is what stands in for the consent check coaches used to need, so "the
/// row was written" and "nothing else was written" are both part of the contract.
/// </summary>
public class PlayerAccessLogTests
{
    private static PlayerAccessLog Log(TestDatabase database, StartPraksisGruppe3Prosjekt.Data.AppDbContext context) =>
        new(context, database.ScopeFactory, NullLogger<PlayerAccessLog>.Instance);

    [Fact]
    public async Task A_coach_lookup_is_recorded()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync(userId: "player-1");
        var round = await database.AddOpenRoundAsync();

        await using (var context = database.NewContext())
        {
            await Log(database, context).RecordAsync(
                Submissions.User("coach-1", Roles.Coach),
                player.Id,
                "Coach/FiveCPlayer",
                round.Id);
        }

        await using var assert = database.NewContext();
        var entry = await assert.PlayerAccessEvents.SingleAsync();

        Assert.Equal(player.Id, entry.PlayerId);
        Assert.Equal("coach-1", entry.ViewedByUserId);
        Assert.Equal(Roles.Coach, entry.ViewedByRole);
        Assert.Equal("Coach/FiveCPlayer", entry.Context);
        Assert.Equal(round.Id, entry.RoundId);
    }

    [Fact]
    public async Task A_player_looking_at_themselves_is_not_recorded()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync(userId: "player-1");

        await using (var context = database.NewContext())
        {
            await Log(database, context).RecordAsync(
                Submissions.User("player-1", Roles.Player),
                player.Id,
                "Player/Index");
        }

        // A row every time a fourteen-year-old opens their own page is noise that hides the
        // rows that matter.
        await using var assert = database.NewContext();
        Assert.Equal(0, await assert.PlayerAccessEvents.CountAsync());
    }

    [Fact]
    public async Task An_admin_who_is_also_a_coach_is_logged_as_an_admin()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var context = database.NewContext())
        {
            await Log(database, context).RecordAsync(
                Submissions.User("admin-1", Roles.Coach, Roles.Admin),
                player.Id,
                "Admin/Export");
        }

        await using var assert = database.NewContext();
        Assert.Equal(Roles.Admin, (await assert.PlayerAccessEvents.SingleAsync()).ViewedByRole);
    }

    [Fact]
    public async Task Nothing_is_recorded_without_a_signed_in_user()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var context = database.NewContext())
        {
            await Log(database, context).RecordAsync(
                new System.Security.Claims.ClaimsPrincipal(),
                player.Id,
                "Coach/FiveCPlayer");
        }

        await using var assert = database.NewContext();
        Assert.Equal(0, await assert.PlayerAccessEvents.CountAsync());
    }

    [Fact]
    public async Task Recording_does_not_save_whatever_else_the_request_is_holding()
    {
        // The log used to be written through the request's own DbContext, so SaveChanges
        // committed everything that context happened to be tracking. A controller that was
        // half-way through its own work had that half-finished work saved as a side effect
        // of logging a page view -- which is not something the name RecordAsync warns about.
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();

        await using (var context = database.NewContext())
        {
            context.Teams.Add(new Team { Name = "Half-finished work" });

            await Log(database, context).RecordAsync(
                Submissions.User("coach-1", Roles.Coach),
                player.Id,
                "Coach/FiveCPlayer");
        }

        await using var assert = database.NewContext();

        Assert.Equal(1, await assert.PlayerAccessEvents.CountAsync());
        Assert.False(await assert.Teams.AnyAsync(t => t.Name == "Half-finished work"));
    }

    [Fact]
    public async Task Recording_survives_an_untouchable_row_on_the_request_context()
    {
        // The append-only guard runs over everything the context tracks. With the log
        // sharing the request's context, an edited ConsentEvent somewhere else in the
        // request made logging throw -- and a lookup would go unrecorded because of a fault
        // that had nothing to do with it.
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

        await using (var context = database.NewContext())
        {
            (await context.ConsentEvents.SingleAsync()).Level = ConsentLevel.None;

            await Log(database, context).RecordAsync(
                Submissions.User("coach-1", Roles.Coach),
                player.Id,
                "Coach/FiveCPlayer");
        }

        await using var assert = database.NewContext();

        Assert.Equal(1, await assert.PlayerAccessEvents.CountAsync());
        Assert.Equal(ConsentLevel.Full, (await assert.ConsentEvents.SingleAsync()).Level);
    }

    [Fact]
    public async Task The_log_for_a_player_comes_back_newest_first()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var other = await database.AddPlayerAsync(code: "TS-09-02");

        await using (var context = database.NewContext())
        {
            context.PlayerAccessEvents.AddRange(
                Event(player.Id, "coach-1", DateTimeOffset.UtcNow.AddDays(-2)),
                Event(player.Id, "coach-2", DateTimeOffset.UtcNow),
                Event(other.Id, "coach-3", DateTimeOffset.UtcNow));

            await context.SaveChangesAsync();
        }

        await using var assert = database.NewContext();
        var entries = await Log(database, assert).GetForPlayerAsync(player.Id);

        Assert.Equal(new[] { "coach-2", "coach-1" }, entries.Select(e => e.ViewedByUserId));
    }

    private static PlayerAccessEvent Event(int playerId, string userId, DateTimeOffset at) => new()
    {
        PlayerId = playerId,
        ViewedByUserId = userId,
        ViewedByRole = Roles.Coach,
        Context = "Coach/FiveCPlayer",
        OccurredAt = at
    };
}
