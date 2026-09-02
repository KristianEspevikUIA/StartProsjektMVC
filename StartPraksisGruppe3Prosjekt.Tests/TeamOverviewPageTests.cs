using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The coach's team page, through the whole pipeline: routing, the policies, the controller
/// and the rendered view.
///
/// What is worth proving here rather than against the service is the ORDER OF THE PAGE and
/// what it says when it has nothing to show. The squad aggregate sits above the players, is
/// gated by CanViewTeamAggregate rather than by CanViewPlayer, and when the policy says no
/// the section says so in words -- a section that silently disappears reads as a bug.
/// </summary>
public sealed class TeamOverviewPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_squad_overview_appears_once_enough_players_have_answered()
    {
        var third = await AddPlayerAsync("TS-TEST-03", "user-third");

        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);
        await AnswerAsync(_factory.OtherPlayerId, "TS-TEST-02", StartCompassFactory.OtherPlayerUserId, value: 3);
        await AnswerAsync(third, "TS-TEST-03", "user-third", value: 5);

        var html = await TeamPageAsync();

        Assert.Contains("Team overview", html);
        Assert.Contains("All statements", html);
        Assert.Contains("Per category", html);
        Assert.Contains("Per statement", html);

        // Above the squad, not below it: the aggregate is the question the page is opened
        // with, and it is the half that names nobody.
        Assert.True(
            html.IndexOf("Team overview", StringComparison.Ordinal)
            < html.IndexOf("<h2>Players</h2>", StringComparison.Ordinal),
            "The team overview should come before the player list.");
    }

    [Fact]
    public async Task Too_few_answers_gives_a_reason_rather_than_a_missing_section()
    {
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);

        var html = await TeamPageAsync();

        Assert.Contains("Team overview", html);
        Assert.Contains("No team average yet", html);
        Assert.Contains(
            $"needs at least {CanViewTeamAggregateRequirement.MinimumResponses}",
            html);

        // And no aggregate leaked out anyway.
        Assert.DoesNotContain("Per statement", html);
    }

    [Fact]
    public async Task The_player_list_carries_a_search_scoped_to_this_squad()
    {
        var html = await TeamPageAsync();

        Assert.Contains("data-player-filter", html);
        Assert.Contains("Find a player in Test team", html);

        // Code and position are what it matches on. There are no names in this system to
        // search by, and none are wanted.
        Assert.Contains("data-player-search=\"TS-TEST-01 Midfielder\"", html);
        Assert.Contains("data-player-search=\"TS-TEST-02 Striker\"", html);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private async Task<string> TeamPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCTeam/{_factory.TeamId}");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<int> AddPlayerAsync(string code, string userId)
    {
        var id = 0;

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var player = new Player
            {
                Code = code,
                TeamId = _factory.TeamId,
                UserId = userId,
                BirthDate = new DateOnly(2010, 3, 1),
                Position = "Goalkeeper"
            };

            db.Players.Add(player);
            await db.SaveChangesAsync();

            id = player.Id;
        });

        return id;
    }

    private Task AnswerAsync(int playerId, string code, string userId, int value) =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(Submissions.Filled(
                catalog,
                _factory.RoundId,
                playerId,
                code,
                RespondentType.Player,
                userId,
                value));
        });
}
