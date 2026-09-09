using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
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
            < html.IndexOf("Players</h2>", StringComparison.Ordinal),
            "The team overview should come before the player list.");

        // English does not pluralise "coach" by adding an s, and the respondent summary
        // writes the word once for the whole form and once per category. See
        // RespondentGap.PluralName.
        Assert.Contains("coaches", html);
        Assert.DoesNotContain("coachs", html);
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
    public async Task Each_section_is_marked_as_a_panel_so_the_page_can_become_tabs()
    {
        var third = await AddPlayerAsync("TS-TEST-03", "user-third");

        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);
        await AnswerAsync(_factory.OtherPlayerId, "TS-TEST-02", StartCompassFactory.OtherPlayerUserId, value: 3);
        await AnswerAsync(third, "TS-TEST-03", "user-third", value: 5);

        var html = await TeamPageAsync();

        // survey.js builds the strip from these, so a missing label is a missing tab.
        Assert.Contains("data-tab-label=\"Overview\"", html);
        Assert.Contains("data-tab-label=\"Per statement\"", html);
        Assert.Contains("data-tab-label=\"Players\"", html);

        // The squad size rides along on the tab.
        Assert.Contains("data-tab-count=\"3\"", html);

        // Nothing is hidden server side: with JavaScript off this is the page it always
        // was, one section after another. The tabs are an enhancement, not the structure.
        Assert.DoesNotContain("sc-tabs", html);
        Assert.DoesNotContain("<section class=\"sc-section\" hidden", html);
    }

    [Fact]
    public async Task A_squad_with_nothing_to_break_down_gets_no_per_statement_panel()
    {
        // One answer is under the threshold, so there is no aggregate -- and so nothing to
        // show statement by statement. A tab onto an empty table is worse than no tab.
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);

        var html = await TeamPageAsync();

        Assert.Contains("data-tab-label=\"Overview\"", html);
        Assert.Contains("data-tab-label=\"Players\"", html);
        Assert.DoesNotContain("data-tab-label=\"Per statement\"", html);
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

    [Fact]
    public async Task The_all_column_is_coloured_by_the_same_rule_as_the_player_page()
    {
        // Two voices about one player, far enough apart to score a difference at all.
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 5);
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.CoachUserId, value: 1,
            role: RespondentType.Coach);

        var html = await TeamPageAsync();

        // "All" is the same number the player page shows as "Between all", so it is banded
        // by the same rule and wears the same badge. It used to be bare bold text -- the one
        // column that matters most was the only one on the row with no colour in it.
        Assert.Contains(AgreementLevels.BadgeClass(AgreementLevel.LargeDifference), html);
        Assert.DoesNotContain("<strong>4.0</strong>", html);
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

    private Task AnswerAsync(
        int playerId,
        string code,
        string userId,
        int value,
        RespondentType role = RespondentType.Player) =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(Submissions.Filled(
                catalog,
                _factory.RoundId,
                playerId,
                code,
                role,
                userId,
                value));
        });

    /// <summary>
    /// The Overview panel holds a second, nested strip: one tab per C, plus the five at a
    /// glance. Marked with data-subtab-* rather than data-tab-*, because the page-level
    /// query looks for the latter and would otherwise flatten the two levels into one long
    /// strip of nine tabs.
    /// </summary>
    [Fact]
    public async Task The_overview_breaks_down_into_one_tab_per_category()
    {
        await ThreeAnswersAsync();

        var html = await TeamPageAsync();

        Assert.Contains("data-subtabs", html);
        Assert.Contains("data-subtab-label=\"All five\"", html);

        foreach (var category in Catalog().Questions.Categories)
        {
            Assert.Contains($"data-subtab-label=\"{category.Name}\"", html);
        }

        // The nested panels must not be visible to the page-level strip, or the coach gets
        // one strip of nine tabs instead of two strips of four and six.
        Assert.DoesNotContain("data-tab-panel data-tab-label=\"Commitment\"", html);
    }

    /// <summary>
    /// The spread is shown next to the average, everywhere the average is. It is the number
    /// that tells a squad that agrees apart from one that does not, and an overview with
    /// only means cannot say which it is looking at.
    /// </summary>
    [Fact]
    public async Task The_overview_shows_how_far_apart_the_squad_is_and_not_only_its_average()
    {
        await ThreeAnswersAsync();

        var html = await TeamPageAsync();

        Assert.Contains("Spread", html);
        Assert.Contains("±", html);
    }

    /// <summary>
    /// Numbers are coloured by the band they fall in, and the key that explains the bands is
    /// on the page with them. A colour scale nobody explains is decoration.
    /// </summary>
    [Fact]
    public async Task Averages_are_coloured_by_their_band_and_the_bands_are_explained()
    {
        await ThreeAnswersAsync();

        var html = await TeamPageAsync();

        // Three players answering 4, 3 and 5 average 4.0 -- a strength.
        Assert.Contains(ScoreLevels.ScoreClass(ScoreLevel.Strong), html);

        Assert.Contains(ScoreLevels.DisplayName(ScoreLevel.Low), html);
        Assert.Contains(ScoreLevels.DisplayName(ScoreLevel.Strong), html);

        // A score and a difference are two scales on one page, and they must not share a
        // class -- the badge is the difference, the tinted number is the score.
        Assert.DoesNotContain("sc-badge sc-mean", html);
    }

    /// <summary>
    /// The statement table carries the same marker the respondent saw on the form. Once the
    /// form is shuffled, that colour is what connects "statement 4 on my form" to a row here.
    /// </summary>
    [Fact]
    public async Task Every_statement_row_carries_its_category_marker()
    {
        await ThreeAnswersAsync();

        var html = await TeamPageAsync();

        Assert.Contains("sc-qcolor--dot", html);

        foreach (var category in Catalog().Questions.Categories)
        {
            Assert.Contains(QuestionColors.CssClass(Catalog().ColorForCategory(category.Key)), html);
        }
    }

    /// <summary>
    /// The squad's shape, from the same partial the player page uses. Drawn only when there
    /// is an aggregate at all -- a grid with no polygon on it says nothing.
    /// </summary>
    [Fact]
    public async Task The_squad_gets_a_pentagon_once_there_is_an_aggregate()
    {
        await ThreeAnswersAsync();

        Assert.Contains("sc-pentagon", await TeamPageAsync());
    }

    private IQuestionCatalog Catalog()
    {
        using var scope = _factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IQuestionCatalog>();
    }

    /// <summary>Three players answering, which is the minimum for an aggregate at all.</summary>
    private async Task ThreeAnswersAsync()
    {
        var third = await AddPlayerAsync("TS-TEST-03", "user-third");

        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);
        await AnswerAsync(_factory.OtherPlayerId, "TS-TEST-02", StartCompassFactory.OtherPlayerUserId, value: 3);
        await AnswerAsync(third, "TS-TEST-03", "user-third", value: 5);
    }
}
