using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The default store, against a real database. The rule that matters most here is the one
/// the schema enforces rather than the code: one submission per person, per player, per
/// round -- answering again is a correction, not a second opinion.
/// </summary>
public class SurveySubmissionStoreTests
{
    [Fact]
    public async Task Answering_again_replaces_the_previous_answers()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Player, "player-1", value: 2));
        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Player, "player-1", value: 5));

        var stored = await store.GetForPlayerAsync(round.Id, player.Id);

        var submission = Assert.Single(stored);
        Assert.All(submission.Answers, answer => Assert.Equal(5, answer.Value));

        // A correction must not leave the first attempt's rows behind.
        await using var assert = database.NewContext();
        Assert.Equal(
            catalog.Questions.AllQuestions.Count(),
            await assert.FiveCAnswers.CountAsync());
    }

    [Fact]
    public async Task Three_respondents_about_one_player_are_three_submissions()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Player, "player-1"));
        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Coach, "coach-1"));
        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Guardian, "guardian-1"));

        Assert.Equal(3, (await store.GetForPlayerAsync(round.Id, player.Id)).Count);
    }

    [Fact]
    public async Task Finding_a_submission_is_scoped_to_the_respondent()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, player.Id, player.Code, RespondentType.Coach, "coach-1"));

        Assert.NotNull(await store.FindAsync(round.Id, player.Id, "coach-1"));
        Assert.Null(await store.FindAsync(round.Id, player.Id, "coach-2"));
    }

    [Fact]
    public async Task A_squad_is_read_in_one_go()
    {
        using var database = new TestDatabase();
        var first = await database.AddPlayerAsync(code: "TS-08-16");
        var second = await database.AddPlayerAsync(code: "TS-09-02");
        var absent = await database.AddPlayerAsync(code: "TS-10-11");
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, first.Id, first.Code, RespondentType.Player, "player-1"));
        await store.SaveAsync(Submissions.Filled(
            catalog, round.Id, second.Id, second.Code, RespondentType.Player, "player-2"));

        var squad = await store.GetForPlayersAsync(
            round.Id,
            new[] { first.Id, second.Id, absent.Id });

        Assert.Equal(2, squad.Count);
        Assert.All(squad, submission => Assert.NotEmpty(submission.Answers));
    }

    [Fact]
    public async Task Counting_by_round_answers_for_every_round_asked_about()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var withAnswers = await database.AddOpenRoundAsync("Answered");
        var withoutAnswers = await database.AddRoundAsync(
            "Empty", DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-30));
        var catalog = TestCatalog.Load();

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(Submissions.Filled(
            catalog, withAnswers.Id, player.Id, player.Code, RespondentType.Player, "player-1"));
        await store.SaveAsync(Submissions.Filled(
            catalog, withAnswers.Id, player.Id, player.Code, RespondentType.Coach, "coach-1"));

        var counts = await store.CountByRoundAsync(new[] { withAnswers.Id, withoutAnswers.Id, 999 });

        Assert.Equal(2, counts[withAnswers.Id]);
        Assert.Equal(0, counts[withoutAnswers.Id]);

        // A round nobody has heard of still gets an entry rather than making the caller
        // guess what a missing key means.
        Assert.Equal(0, counts[999]);
    }

    [Fact]
    public async Task Counting_nothing_asks_nothing()
    {
        using var database = new TestDatabase();
        await using var context = database.NewContext();

        Assert.Empty(await new EfSurveySubmissionStore(context)
            .CountByRoundAsync(Array.Empty<int>()));
    }

    [Fact]
    public async Task An_unanswered_question_stays_null_rather_than_becoming_a_middling_opinion()
    {
        using var database = new TestDatabase();
        var player = await database.AddPlayerAsync();
        var round = await database.AddOpenRoundAsync();
        var catalog = TestCatalog.Load();

        var question = catalog.Questions.Categories[0].Questions[0];

        await using var context = database.NewContext();
        var store = new EfSurveySubmissionStore(context);

        await store.SaveAsync(new SurveySubmission
        {
            RoundId = round.Id,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = SurveySubmission.Roles.From(RespondentType.Player),
            RespondentUserId = "player-1",
            QuestionSetVersion = catalog.Questions.Version,
            SubmittedAt = DateTimeOffset.UtcNow,
            Answers = new[]
            {
                new SurveyAnswer
                {
                    QuestionKey = question.Key,
                    CategoryKey = catalog.Questions.Categories[0].Key,
                    Value = null
                }
            }
        });

        var stored = Assert.Single(await store.GetForPlayerAsync(round.Id, player.Id));

        Assert.Null(Assert.Single(stored.Answers).Value);
    }
}
