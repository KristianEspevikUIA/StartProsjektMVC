using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The squad aggregate on the coach's team page: across every statement, per category, and
/// per statement.
///
/// Three things here are easy to get wrong in a way that nothing on the page would look
/// wrong for:
///
///   * WHAT IS BEING AVERAGED. A team average is the average PLAYER, not the average
///     ANSWER. Pooling every answer instead lets whoever filled the form in most completely
///     quietly weigh the most, and the two only differ once somebody skips something --
///     which is exactly when nobody is checking.
///   * THE MINIMUM. An average over one or two people is those people's answers with a
///     different label on it. The threshold has to hold, and "withheld" has to stay
///     distinguishable from "nobody answered".
///   * REVERSAL. The per-statement numbers are scored, so a negatively worded statement has
///     to point the same way as everything beside it.
/// </summary>
public sealed class TeamAggregateTests
{
    private const string CoachUser = "user-coach";

    [Fact]
    public async Task The_team_average_is_the_mean_of_the_players_not_of_their_answers()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var round = await database.AddOpenRoundAsync();

        // Two players answer the whole form at 2. The third answers 5, but only in one
        // category -- five statements out of twenty-five.
        var first = await AnswerAsync(database, catalog, round, "TS-01-01", score: 5, onlyFirstCategory: true);
        await AnswerAsync(database, catalog, round, "TS-01-02", score: 2);
        await AnswerAsync(database, catalog, round, "TS-01-03", score: 2);

        var aggregate = await AggregateAsync(database, catalog, round, first.TeamId);

        // Pooled over answers this would be about 2.3: the sparse 5s would be outvoted by
        // the fifty 2s beside them. One number per player, then averaged, is 3.0.
        Assert.Equal(3.0, aggregate.Overall.PlayerMean!.Value, precision: 2);
        Assert.Equal(3, aggregate.Overall.Player.Respondents);
    }

    [Fact]
    public async Task An_average_with_too_few_people_behind_it_is_withheld_and_says_so()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var round = await database.AddOpenRoundAsync();

        var first = await AnswerAsync(database, catalog, round, "TS-02-01", score: 4);
        await AnswerAsync(database, catalog, round, "TS-02-02", score: 4);

        var aggregate = await AggregateAsync(database, catalog, round, first.TeamId);

        // Two is below the minimum, so there is no number -- but the page still has to be
        // able to say why, which is what Withheld and Respondents are for.
        Assert.Null(aggregate.Overall.PlayerMean);
        Assert.True(aggregate.Overall.Player.Withheld);
        Assert.Equal(2, aggregate.Overall.Player.Respondents);

        // The third answer is what makes it publishable, and nothing else changes.
        await AnswerAsync(database, catalog, round, "TS-02-03", score: 4);

        var again = await AggregateAsync(database, catalog, round, first.TeamId);

        Assert.Equal(CanViewTeamAggregateRequirement.MinimumResponses, again.Overall.Player.Respondents);
        Assert.Equal(4.0, again.Overall.PlayerMean!.Value, precision: 2);
        Assert.False(again.Overall.Player.Withheld);
    }

    [Fact]
    public async Task Nobody_answering_is_not_the_same_state_as_an_average_being_withheld()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var round = await database.AddOpenRoundAsync();

        var first = await AnswerAsync(database, catalog, round, "TS-03-01", score: 3);
        await AnswerAsync(database, catalog, round, "TS-03-02", score: 3);
        await AnswerAsync(database, catalog, round, "TS-03-03", score: 3);

        var aggregate = await AggregateAsync(database, catalog, round, first.TeamId);

        // No guardian anywhere on this team has answered. That is an absence, not a
        // suppression, and the two must not print the same way.
        Assert.Null(aggregate.Overall.GuardianMean);
        Assert.False(aggregate.Overall.Guardian.Withheld);
        Assert.Equal(0, aggregate.Overall.Guardian.Respondents);
    }

    [Fact]
    public async Task Per_statement_averages_are_scored_so_a_reversed_statement_reads_like_the_rest()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var round = await database.AddOpenRoundAsync();

        var first = await AnswerAsync(database, catalog, round, "TS-04-01", score: 5);
        await AnswerAsync(database, catalog, round, "TS-04-02", score: 5);
        await AnswerAsync(database, catalog, round, "TS-04-03", score: 5);

        var aggregate = await AggregateAsync(database, catalog, round, first.TeamId);

        var statements = aggregate.Categories.SelectMany(c => c.Questions).ToList();

        Assert.Contains(statements, q => q.Reversed);

        foreach (var statement in statements)
        {
            // Everybody scored a 5 on everything. On a reversed statement they clicked a 1
            // to do it, and the average still has to read 5 -- otherwise the one column that
            // is turned round points the other way to the four beside it.
            Assert.Equal(5.0, statement.Means.PlayerMean!.Value, precision: 2);
        }
    }

    [Fact]
    public async Task The_line_over_time_follows_the_squad_and_not_whoever_answered_most()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var earlier = await database.AddRoundAsync(
            "Earlier period", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));

        var later = await database.AddRoundAsync(
            "Later period", DateTimeOffset.UtcNow.AddDays(-20), DateTimeOffset.UtcNow.AddDays(10));

        var codes = new[] { "TS-05-01", "TS-05-02", "TS-05-03" };

        foreach (var code in codes)
        {
            await AnswerAsync(database, catalog, earlier, code, score: 2);
        }

        foreach (var code in codes)
        {
            await AnswerAsync(database, catalog, later, code, score: 4);
        }

        var teamId = (await AnswerAsync(database, catalog, later, codes[0], score: 4)).TeamId;

        var trend = await TrendAsync(database, catalog, teamId, earlier, later);

        Assert.True(trend.HasComparablePeriods);
        Assert.Equal(new[] { "Earlier period", "Later period" }, trend.Periods.Select(p => p.Name));

        foreach (var category in trend.Categories)
        {
            Assert.NotNull(category.Change);
            Assert.True(
                category.Change > 0,
                $"{category.CategoryName} should have improved, but changed by {category.Change}.");
        }
    }

    [Fact]
    public async Task A_period_with_too_few_players_is_a_gap_in_the_line_and_is_named_as_one()
    {
        using var database = new TestDatabase();
        var catalog = TestCatalog.Load();

        var thin = await database.AddRoundAsync(
            "Thin period", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));

        var full = await database.AddRoundAsync(
            "Full period", DateTimeOffset.UtcNow.AddDays(-20), DateTimeOffset.UtcNow.AddDays(10));

        var codes = new[] { "TS-06-01", "TS-06-02", "TS-06-03" };

        // One player in the first period, everybody in the second.
        await AnswerAsync(database, catalog, thin, codes[0], score: 3);

        var teamId = 0;

        foreach (var code in codes)
        {
            teamId = (await AnswerAsync(database, catalog, full, code, score: 3)).TeamId;
        }

        var trend = await TrendAsync(database, catalog, teamId, thin, full);

        Assert.All(trend.Categories, category => Assert.Null(category.Means[0]));
        Assert.All(trend.Categories, category => Assert.NotNull(category.Means[1]));

        // Named, because an unexplained hole in a line reads as "nobody answered" -- and
        // somebody did.
        Assert.Equal(new[] { "Thin period" }, trend.ThinPeriods.Select(p => p.Name));
        Assert.Equal(new[] { 1, 3 }, trend.PlayersPerPeriod);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// One player answering a whole form -- or one category of it -- at a given SCORE.
    ///
    /// The score is what the analysis reads back, so a negatively worded statement is
    /// written as its mirror image: raw (6 - score). Writing the score straight through
    /// would make every reversed statement in the fixture mean the opposite of the rest,
    /// and the assertions would then be measuring the fixture's bug.
    /// </summary>
    private static async Task<Player> AnswerAsync(
        TestDatabase database,
        IQuestionCatalog catalog,
        SurveyRound round,
        string code,
        int score,
        bool onlyFirstCategory = false)
    {
        await using var context = database.NewContext();

        // Reused across periods rather than re-created: a squad's development is the same
        // players answering again, and a second row with the same code is not a thing the
        // database allows anyway.
        var player = context.Players.FirstOrDefault(p => p.Code == code)
                     ?? await database.AddPlayerAsync(code: code, userId: $"user-{code}");

        var categories = onlyFirstCategory
            ? catalog.Questions.Categories.Take(1)
            : catalog.Questions.Categories;

        var answers = categories
            .SelectMany(category => category.Questions.Select(question => new SurveyAnswer
            {
                QuestionKey = question.Key,
                CategoryKey = category.Key,
                Value = question.Reversed ? PlayerRules.ReverseScoreBase - score : score
            }))
            .ToList();

        await new EfSurveySubmissionStore(context).SaveAsync(new SurveySubmission
        {
            RoundId = round.Id,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = SurveySubmission.Roles.From(RespondentType.Player),
            RespondentUserId = player.UserId!,
            QuestionSetVersion = catalog.Questions.Version,
            SubmittedAt = DateTimeOffset.UtcNow,
            Answers = answers
        });

        return player;
    }

    private static async Task<TeamFiveCAggregate> AggregateAsync(
        TestDatabase database,
        IQuestionCatalog catalog,
        SurveyRound round,
        int teamId)
    {
        await using var context = database.NewContext();

        var playerIds = context.Players
            .Where(p => p.TeamId == teamId)
            .Select(p => p.Id)
            .ToList();

        return await new FiveCAnalysisService(new EfSurveySubmissionStore(context), catalog)
            .GetForTeamAsync(round.Id, teamId, "Senior", playerIds);
    }

    private static async Task<TeamTrend> TrendAsync(
        TestDatabase database,
        IQuestionCatalog catalog,
        int teamId,
        params SurveyRound[] rounds)
    {
        await using var context = database.NewContext();

        var playerIds = context.Players
            .Where(p => p.TeamId == teamId)
            .Select(p => p.Id)
            .ToList();

        return await new FiveCAnalysisService(new EfSurveySubmissionStore(context), catalog)
            .GetTeamTrendAsync(
                teamId,
                "Senior",
                playerIds,
                rounds.Select(r => new TrendPeriod(r.Id, r.Name, r.ClosesAt)).ToList());
    }
}
