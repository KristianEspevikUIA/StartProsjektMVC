using StartPraksisGruppe3Prosjekt.Authorization;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The forms page, and the front page link that leads to it.
///
/// The forms page is one list with three meanings: a player sees one row about themselves,
/// a guardian one per child, a coach one per player in the club. It is a TABLE and not a
/// grid of cards, because the coach case is thirty of them -- thirty cards is a page you
/// scroll, thirty rows is a page you scan.
/// </summary>
public sealed class SurveyIndexPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_forms_are_a_list_with_the_fields_the_squad_is_read_by()
    {
        var html = await FormsPageAsync();

        Assert.Contains("<th scope=\"col\">Player</th>", html);
        Assert.Contains("<th scope=\"col\">Position</th>", html);
        Assert.Contains("<th scope=\"col\">Team</th>", html);
        Assert.Contains("<th scope=\"col\">Age</th>", html);

        // The identifying column is the player CODE. There are no names in this data model,
        // deliberately, so this is what a player is called in every list in the application.
        Assert.Contains("TS-TEST-01", html);

        // Position and team come from the player, not from the submission.
        Assert.Contains("Midfielder", html);
        Assert.Contains("Test team", html);
    }

    /// <summary>
    /// A player born in 2008 is not eighteen in every year. The age is computed from the
    /// birth date by the same rule the guardian requirement uses, and the birth date itself
    /// is never printed -- an age is what a coach reading a squad list needs, and a date of
    /// birth is more than that.
    /// </summary>
    [Fact]
    public async Task The_list_shows_an_age_and_not_a_date_of_birth()
    {
        var html = await FormsPageAsync();

        Assert.DoesNotContain("2008-05-16", html);
        Assert.DoesNotContain("16.05.2008", html);
    }

    /// <summary>
    /// "My teams" goes to CoachController, which is [Authorize(Coach, Admin)]. A button
    /// that leads to a 403 is worse than no button, so it is shown to those two roles and
    /// to nobody else.
    /// </summary>
    [Fact]
    public async Task My_teams_is_offered_to_a_coach()
    {
        var html = await HomePageAsync(StartCompassFactory.CoachUserId, Roles.Coach);

        Assert.Contains("My teams", html);
        Assert.Contains("/Coach", html);
    }

    [Fact]
    public async Task My_teams_is_not_offered_to_a_player()
    {
        var html = await HomePageAsync(StartCompassFactory.PlayerUserId, Roles.Player);

        Assert.DoesNotContain("My teams", html);

        // The form is still there. A player's way in is unchanged.
        Assert.Contains("Go to the form", html);
    }

    private async Task<string> FormsPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync("/Survey");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> HomePageAsync(string userId, string role)
    {
        var response = await _factory.ClientAs(userId, role).GetAsync("/");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }
}
