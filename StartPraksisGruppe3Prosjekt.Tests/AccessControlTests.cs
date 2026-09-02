using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Who may open a page about one particular player.
///
/// These are the rules the whole system rests on, and until now they could only be checked
/// by signing in as four different people and clicking. Each test below is one sentence from
/// the README, asked as a question the build can answer.
/// </summary>
public sealed class AccessControlTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anonymous_request_is_refused()
    {
        // What this proves is that the page is closed to anonymous users at all, which comes
        // from the fallback policy in Program.cs rather than from an [Authorize] attribute
        // on the controller.
        //
        // It asserts "refused" rather than "302 to /Identity/Account/Login": the redirect is
        // the cookie handler's behaviour, and the cookie handler is exactly what the test
        // host replaces. Under the test scheme a challenge is a plain 401. Testing the
        // redirect itself would be testing Identity, not our rules.
        var response = await _factory.AnonymousClient()
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        Assert.False(response.IsSuccessStatusCode);
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect,
            $"Anonymous access should be refused, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Player_cannot_open_the_coach_view()
    {
        var response = await _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Guardian_can_open_their_own_child()
    {
        var response = await _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian)
            .GetAsync($"/Guardian/Player/{_factory.PlayerId}");

        await _factory.AssertOkAsync(response);
    }

    [Fact]
    public async Task Guardian_cannot_open_somebody_elses_child()
    {
        // The role grants nothing. Only the Guardianship row does, and there is no row
        // linking this guardian to the second player.
        var response = await _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian)
            .GetAsync($"/Guardian/Player/{_factory.OtherPlayerId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Coach_can_open_a_player_who_has_not_consented()
    {
        // The fixture seeds no ConsentEvent at all, so the current level is None. Coaches
        // used to be refused here; the club asked for the opposite, and this is that rule.
        // If somebody puts the consent check back, this test says so.
        var response = await _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Opening_a_player_is_written_to_the_audit_log()
    {
        // The counterweight to the test above. Consent no longer stops a coach, so the log
        // is the only thing left that can say who looked at whom. If it stops being written,
        // the rule above is unguarded.
        var response = await _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var entry = await db.PlayerAccessEvents
                .AsNoTracking()
                .Where(a => a.PlayerId == _factory.PlayerId)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            Assert.NotNull(entry);
            Assert.Equal(StartCompassFactory.CoachUserId, entry!.ViewedByUserId);
            Assert.Equal(Roles.Coach, entry.ViewedByRole);
            Assert.Equal("Coach/FiveCPlayer", entry.Context);
        });
    }

    [Fact]
    public async Task A_player_looking_at_themselves_is_not_logged()
    {
        // The log is about other people looking at a player. A row every time a fourteen-
        // year-old opens their own page is noise that buries the rows that matter.
        var response = await _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player)
            .GetAsync("/Player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var selfLookups = await db.PlayerAccessEvents
                .AsNoTracking()
                .CountAsync(a => a.ViewedByUserId == StartCompassFactory.PlayerUserId);

            Assert.Equal(0, selfLookups);
        });
    }
}
