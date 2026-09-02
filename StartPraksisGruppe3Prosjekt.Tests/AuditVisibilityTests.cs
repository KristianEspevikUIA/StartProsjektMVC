using StartPraksisGruppe3Prosjekt.Authorization;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// A player can see who has looked at their answers.
///
/// This is the other half of coaches not needing consent: the club can account for every
/// lookup, and so can the person it is about.
/// </summary>
public sealed class AuditVisibilityTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_coach_lookup_shows_up_on_the_players_own_page()
    {
        // Nothing has happened yet.
        var before = await PlayerPageAsync();
        Assert.Contains("Nobody has opened your answers yet", before);

        // The coach opens the player.
        var coachVisit = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        await _factory.AssertOkAsync(coachVisit);

        var after = await PlayerPageAsync();

        Assert.DoesNotContain("Nobody has opened your answers yet", after);
        Assert.Contains("Who has looked", after);
        Assert.Contains("Coach", after);
    }

    [Fact]
    public async Task The_players_own_visits_are_not_listed()
    {
        // Opening your own page three times should not produce a list of yourself.
        await PlayerPageAsync();
        await PlayerPageAsync();

        var html = await PlayerPageAsync();

        Assert.Contains("Nobody has opened your answers yet", html);
    }

    private async Task<string> PlayerPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.PlayerUserId, Roles.Player)
            .GetAsync("/Player");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }
}
