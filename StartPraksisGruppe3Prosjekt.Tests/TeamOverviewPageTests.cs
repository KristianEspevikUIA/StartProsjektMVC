using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
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
        Assert.Contains("<th scope=\"row\">All statements</th>", html);
        Assert.Contains("Per statement", html);

        // One table, a row per C -- laid out like the player list, which is the part of the
        // page coaches found easy to read -- rather than a card of bars and prose per C.
        foreach (var category in Catalog().Questions.Categories)
        {
            Assert.Contains(category.Name, html);
        }

        Assert.DoesNotContain("sc-compare__card", html);

        // Above the squad, not below it: the aggregate is the question the page is opened
        // with, and it is the half that names nobody.
        Assert.True(
            html.IndexOf("Team overview", StringComparison.Ordinal)
            < html.IndexOf("Player overview</h2>", StringComparison.Ordinal),
            "The team overview should come before the player overview.");

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

        // survey.js builds the strip from these, so a missing label is a missing tab. Two at
        // the top -- the team, and the players -- and the readings of the team one level down.
        Assert.Contains("data-tab-label=\"Team overview\"", html);
        Assert.Contains("data-tab-label=\"Player overview\"", html);
        Assert.Contains("data-subtab-label=\"Per statement\"", html);

        // Exactly two, in that order. The player page links back to #sc-panel-1 and expects
        // it to be Player overview; a third top-level panel ahead of it would break that.
        Assert.Equal(2, Occurrences(html, "data-tab-panel"));
        Assert.True(
            html.IndexOf("data-tab-label=\"Team overview\"", StringComparison.Ordinal)
            < html.IndexOf("data-tab-label=\"Player overview\"", StringComparison.Ordinal),
            "Player overview should be the second top-level panel.");

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

        Assert.Contains("data-tab-label=\"Team overview\"", html);
        Assert.Contains("data-tab-label=\"Player overview\"", html);
        Assert.DoesNotContain("data-subtab-label=\"Per statement\"", html);
    }

    [Fact]
    public async Task The_player_list_carries_a_search_scoped_to_this_squad()
    {
        var html = await TeamPageAsync();

        Assert.Contains("data-player-filter", html);
        Assert.Contains("Find a player in Test team", html);

        // Name and position are what it matches on.
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

    private async Task<int> AddClosedRoundAsync(string name)
    {
        var id = 0;

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;

            var round = new SurveyRound
            {
                Name = name,
                OpensAt = now.AddDays(-200),
                ClosesAt = now.AddDays(-150)
            };

            db.SurveyRounds.Add(round);
            await db.SaveChangesAsync();

            id = round.Id;
        });

        return id;
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
    /// Team overview holds a second, quieter strip: the five C's side by side, every
    /// statement, and the change over time. Marked with data-subtab-* rather than data-tab-*,
    /// because the page-level query looks for the latter and would otherwise flatten the two
    /// levels into one strip.
    ///
    /// And no tab per C. There used to be one inside the old Overview panel -- a third row of
    /// tabs, each repeating statements the Per statement view already groups by C.
    /// </summary>
    [Fact]
    public async Task The_team_overview_has_its_readings_one_level_down_and_no_tab_per_category()
    {
        await ThreeAnswersAsync();

        var html = await TeamPageAsync();

        Assert.Contains("data-subtabs", html);
        // The apostrophe may be encoded; match up to it rather than guess the entity.
        Assert.Contains("data-subtab-label=\"The five C", html);
        Assert.Contains("data-subtab-label=\"Per statement\"", html);

        foreach (var category in Catalog().Questions.Categories)
        {
            Assert.DoesNotContain($"data-subtab-label=\"{category.Name}\"", html);
            Assert.DoesNotContain($"data-tab-label=\"{category.Name}\"", html);
        }

        // Every C is still on the page, as a card.
        foreach (var category in Catalog().Questions.Categories)
        {
            Assert.Contains(category.Name, html);
        }
    }

    /// <summary>
    /// One control for the period rather than one button per period. The row of buttons was
    /// the heaviest thing on the page and grew by one every season.
    /// </summary>
    [Fact]
    public async Task The_period_is_chosen_from_one_select_and_not_a_row_of_buttons()
    {
        var earlier = await AddClosedRoundAsync("Spring 2026");

        var html = await TeamPageAsync();

        Assert.Contains("class=\"sc-period\"", html);
        Assert.Contains("name=\"roundId\"", html);
        Assert.Contains($"<option value=\"{earlier}\"", html);

        // Not a link per period any more.
        Assert.DoesNotContain($"roundId={earlier}\">", html);
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

    /// <summary>
    /// The page's body is method -- how an average is built, what a spread is, why a bar is
    /// missing -- and that prose earns its place. What it does not do is answer the question
    /// the page is opened with on a Sunday evening. The strongest and weakest C were a
    /// paragraph three tabs and two thousand pixels down; they are now the first thing in
    /// Team overview, the tab the page opens on, and they are said once.
    /// </summary>
    [Fact]
    public async Task The_page_opens_with_the_short_answer_and_keeps_the_method_under_it()
    {
        await ThreeAnswersWithAWeakCategoryAsync();

        var html = await TeamPageAsync();

        Assert.Contains("Where to start", html);

        // Inside Team overview, and ahead of the method.
        Assert.True(
            html.IndexOf("data-tab-label=\"Team overview\"", StringComparison.Ordinal)
            < html.IndexOf("Where to start", StringComparison.Ordinal),
            "The summary should open the team overview.");

        Assert.True(
            html.IndexOf("Where to start", StringComparison.Ordinal)
            < html.IndexOf("All statements", StringComparison.Ordinal),
            "The summary should come before the method.");

        // Concentration was answered 1 where everything else was answered 5.
        Assert.Contains("Highest in", html);
        Assert.Contains("Lowest in", html);
        Assert.Contains("Concentration</strong>", html);

        // Once. Two copies of one sentence on a page makes a reader stop to work out
        // whether they are two different facts.
        Assert.Equal(1, Occurrences(html, "Highest in"));

        // The method is no longer a paragraph over every table: it is on the help page, and
        // the page links to its section there.
        Assert.DoesNotContain("one number per person, then averaged", html);
        Assert.Contains("href=\"/Help#team-page\"", html);
    }

    /// <summary>
    /// Follow-up is the player's OWN low score, and difference is disagreement. A coach saw
    /// "Concentration" in the follow-up column beside a difference of 0.0 and read it as a
    /// bug: the player, guardian and coach had all answered the same low answers. So the
    /// badge carries the score it is based on, and the column stands apart from the
    /// differences under a heading that says what it is.
    /// </summary>
    [Fact]
    public async Task A_follow_up_badge_shows_the_players_own_score_apart_from_the_differences()
    {
        // All three agree on 1 everywhere: no difference at all, and a follow-up on every C.
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 1);
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.CoachUserId, value: 1,
            role: RespondentType.Coach);

        var html = await TeamPageAsync();

        var commitment = Catalog().Questions.Categories.First().Name;
        Assert.Equal(1, Occurrences(html, $"{commitment} 1.0 </span>"));
        Assert.Contains("Own average below 2", html);

        // The follow-up heading comes before the difference group, not at the end of it.
        Assert.True(
            html.IndexOf("Own average below 2", StringComparison.Ordinal)
            < html.IndexOf(">How far apart<", StringComparison.Ordinal),
            "Follow-up should stand apart from, and before, the difference columns.");
    }

    /// <summary>
    /// Who has not answered, by name, so the coach can chase them without opening the
    /// player list to find out who they are. Who answered is neutral progress -- it says
    /// that somebody answered, never what they answered.
    /// </summary>
    [Fact]
    public async Task The_summary_names_the_players_who_have_not_answered()
    {
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);

        var html = await TeamPageAsync();

        Assert.Equal(1, Occurrences(html, "players have answered about themselves."));
        Assert.Equal(1, Occurrences(html, "Not yet:"));

        // By name, in the card, after the words that introduce it.
        var notYet = html.IndexOf("Not yet:", StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("TS-TEST-02", notYet, StringComparison.Ordinal) > notYet,
            "The player still to answer should be named in the Answers card.");
    }

    /// <summary>
    /// The follow-up card says how many, once, and only turns to the alert colour when there
    /// is somebody to follow up.
    /// </summary>
    [Fact]
    public async Task The_follow_up_sentence_is_said_once_and_only_when_there_is_one()
    {
        var quiet = await TeamPageAsync();

        Assert.Contains("Follow-up", quiet);
        Assert.Equal(1, Occurrences(quiet, "No player's own average is below 2 in any C."));
        Assert.Equal(0, Occurrences(quiet, "with their own average below 2 in a C."));
        Assert.DoesNotContain("sc-stat--alert", quiet);

        // One player answering 1 everywhere is under FiveCRules.FollowUpThreshold on all
        // five C's.
        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 1);

        var flagged = await TeamPageAsync();

        Assert.Equal(1, Occurrences(flagged, "with their own average below 2 in a C."));
        Assert.Equal(0, Occurrences(flagged, "No player's own average is below 2 in any C."));
        Assert.Contains("sc-stat--alert", flagged);
    }

    /// <summary>
    /// How many times a sentence appears, counted over the page with its whitespace
    /// flattened. A sentence written across four lines of a view reaches the browser with
    /// the view's line breaks and indentation still in it, and asserting on those is
    /// asserting on how the Razor file happens to be wrapped.
    /// </summary>
    private static int Occurrences(string haystack, string needle)
    {
        haystack = Squash(haystack);
        needle = Squash(needle);

        var count = 0;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Squash(string text) =>
        Regex.Replace(text, @"\s+", " ");

    private IQuestionCatalog Catalog()
    {
        using var scope = _factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IQuestionCatalog>();
    }

    /// <summary>
    /// Three players again, but answering one category far lower than the other four. The
    /// shared helper answers every statement with the same number, which leaves the five
    /// C's exactly level and gives the squad no strongest or weakest to name.
    /// </summary>
    private async Task ThreeAnswersWithAWeakCategoryAsync()
    {
        var third = await AddPlayerAsync("TS-TEST-03", "user-third");

        await LopsidedAnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId);
        await LopsidedAnswerAsync(_factory.OtherPlayerId, "TS-TEST-02", StartCompassFactory.OtherPlayerUserId);
        await LopsidedAnswerAsync(third, "TS-TEST-03", "user-third");
    }

    private Task LopsidedAnswerAsync(int playerId, string code, string userId) =>
        _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            var answers = catalog.Questions.Categories
                .SelectMany(category => category.Questions.Select(question => new SurveyAnswer
                {
                    QuestionKey = question.Key,
                    CategoryKey = category.Key,
                    Value = category.Key == "concentration" ? 1 : 5
                }))
                .ToList();

            await store.SaveAsync(new SurveySubmission
            {
                RoundId = _factory.RoundId,
                PlayerId = playerId,
                PlayerCode = code,
                RespondentRole = SurveySubmission.Roles.From(RespondentType.Player),
                RespondentUserId = userId,
                QuestionSetVersion = catalog.Questions.Version,
                SubmittedAt = DateTimeOffset.UtcNow,
                Answers = answers
            });
        });

    /// <summary>Three players answering, which is the minimum for an aggregate at all.</summary>
    private async Task ThreeAnswersAsync()
    {
        var third = await AddPlayerAsync("TS-TEST-03", "user-third");

        await AnswerAsync(_factory.PlayerId, "TS-TEST-01", StartCompassFactory.PlayerUserId, value: 4);
        await AnswerAsync(_factory.OtherPlayerId, "TS-TEST-02", StartCompassFactory.OtherPlayerUserId, value: 3);
        await AnswerAsync(third, "TS-TEST-03", "user-third", value: 5);
    }
}
