using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The end-of-period reflection, submitted through the application rather than through the
/// store.
///
/// What is worth proving here is the part the store cannot see: the browser decides what it
/// posts, and the question set decides what is stored. A C that is not in the file, a
/// paragraph longer than the question allows and a field name nobody rendered all arrive as
/// an ordinary POST, and all three have to come back as a form the respondent can fix --
/// with nothing written to the database.
/// </summary>
public sealed class SurveyReflectionTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task What_the_player_writes_is_saved_with_the_form()
    {
        var response = await SubmitAsync(new Dictionary<string, string>
        {
            ["reflection-strength"] = "confidence",
            ["reflection-strength-example"] = "I took the last penalty at 2-2.",
            ["reflection-focus"] = "concentration"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var stored = await db.FiveCReflectionAnswers
                .AsNoTracking()
                .ToDictionaryAsync(a => a.QuestionKey, a => a.Value);

            // Three answered, two left alone -- and a question left alone leaves no row.
            Assert.Equal(3, stored.Count);
            Assert.Equal("confidence", stored["reflection-strength"]);
            Assert.Equal("I took the last penalty at 2-2.", stored["reflection-strength-example"]);
            Assert.DoesNotContain("reflection-support", stored.Keys);
        });
    }

    [Fact]
    public async Task The_statements_still_save_when_the_reflection_is_left_alone()
    {
        var response = await SubmitAsync(new Dictionary<string, string>());

        // None of it is compulsory. A respondent who answers the 25 and stops has filled the
        // form in, and telling them otherwise is the fastest way to get five full stops back
        // instead of five sentences.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.Equal(1, await db.FiveCSubmissions.CountAsync());
            Assert.Equal(0, await db.FiveCReflectionAnswers.CountAsync());
        });
    }

    [Fact]
    public async Task A_C_that_is_not_in_the_question_set_is_refused()
    {
        var response = await SubmitAsync(new Dictionary<string, string>
        {
            ["reflection-strength"] = "resilience"
        });

        // Back to the form, not a 400: whoever meets this is either on a form that is out of
        // step with the file or editing the page, and the first of those deserves a page
        // they can fix.
        await _factory.AssertOkAsync(response);
        Assert.Contains("Choose one of the five", await response.Content.ReadAsStringAsync());

        await NothingWasStoredAsync();
    }

    [Fact]
    public async Task An_answer_longer_than_the_question_allows_is_refused_and_kept()
    {
        var tooLong = new string('a', 501);

        var response = await SubmitAsync(new Dictionary<string, string>
        {
            ["reflection-strength-example"] = tooLong
        });

        await _factory.AssertOkAsync(response);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("500 characters or fewer", html);

        // And what was written comes back in the field. A rejected save that empties the
        // textarea costs the respondent their answer twice.
        Assert.Contains(tooLong, html);

        await NothingWasStoredAsync();
    }

    [Fact]
    public async Task A_field_name_that_is_not_in_the_question_set_is_dropped()
    {
        var response = await SubmitAsync(
            new Dictionary<string, string> { ["reflection-strength"] = "control" },
            extra: new Dictionary<string, string>
            {
                ["ReflectionAnswers[9].QuestionKey"] = "reflection-invented",
                ["ReflectionAnswers[9].Value"] = "Something nobody asked about."
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var keys = await db.FiveCReflectionAnswers
                .AsNoTracking()
                .Select(a => a.QuestionKey)
                .ToListAsync();

            Assert.Equal(new[] { "reflection-strength" }, keys);
        });
    }

    /// <summary>
    /// The point of the "Not answered" option: a C that was chosen can be un-chosen.
    ///
    /// A radio cannot be unchecked by clicking it again, so an optional question without a
    /// way out is optional only until the respondent touches it. What the option posts is an
    /// empty value -- the same thing a question nobody looked at posts -- and the row has to
    /// go, not sit there holding the answer that was corrected away.
    /// </summary>
    [Fact]
    public async Task A_C_that_was_chosen_can_be_cleared_and_its_row_goes()
    {
        var chosen = await SubmitAsync(new Dictionary<string, string>
        {
            ["reflection-strength"] = "control",
            ["reflection-strength-example"] = "I stayed calm after the second yellow.",
            ["reflection-focus"] = "communication"
        });

        Assert.Equal(HttpStatusCode.Redirect, chosen.StatusCode);

        // What the page posts when "Not answered" is the selected option in the group.
        var cleared = await SubmitAsync(new Dictionary<string, string>
        {
            ["reflection-strength"] = string.Empty,
            ["reflection-strength-example"] = "I stayed calm after the second yellow.",
            ["reflection-focus"] = "communication"
        });

        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var stored = await db.FiveCReflectionAnswers
                .AsNoTracking()
                .ToDictionaryAsync(a => a.QuestionKey, a => a.Value);

            // Gone entirely, not kept as an empty string: "not answered" is the absence of a
            // row here, and a blank row would count as an answer everywhere that reads them.
            Assert.DoesNotContain("reflection-strength", stored.Keys);

            // And only that one. Clearing a choice is not a way to lose the rest of the
            // reflection, which is the failure a wholesale replace could quietly produce.
            Assert.Equal("I stayed calm after the second yellow.", stored["reflection-strength-example"]);
            Assert.Equal("communication", stored["reflection-focus"]);
            Assert.Equal(2, stored.Count);

            // The statements are untouched by any of it.
            Assert.Equal(25, await db.FiveCAnswers.CountAsync());
        });
    }

    /// <summary>
    /// The option is on the page, and it comes from the question set rather than from the
    /// view -- the same rule every other word on this form follows.
    /// </summary>
    [Fact]
    public async Task Every_optional_C_question_offers_the_way_back_out()
    {
        var client = _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player);

        var page = await client.GetAsync(
            $"/Survey/Fill?roundId={_factory.RoundId}&playerId={_factory.PlayerId}");

        await _factory.AssertOkAsync(page);

        var html = await page.Content.ReadAsStringAsync();
        var catalog = Catalog();

        var optionalChoices = catalog.ReflectionQuestions
            .Count(q => q.IsCategoryChoice && !q.Required);

        Assert.True(optionalChoices > 0, "The shipped question set has no optional C choice to clear.");

        // One clear option per optional choice, marked so the tab counter can tell it apart
        // from an answer -- see countIn in survey.js.
        Assert.Equal(
            optionalChoices,
            html.Split("data-reflection-clear").Length - 1);

        Assert.Contains(catalog.Reflection!.NoAnswerLabel, html);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Fills the form in the way the page does -- every statement answered, one field per
    /// reflection question whether or not it was answered -- and posts it as the player.
    /// </summary>
    private async Task<HttpResponseMessage> SubmitAsync(
        IReadOnlyDictionary<string, string> reflection,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var client = _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player);
        var url = $"/Survey/Fill?roundId={_factory.RoundId}&playerId={_factory.PlayerId}";

        var page = await client.GetAsync(url);
        await _factory.AssertOkAsync(page);

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken(await page.Content.ReadAsStringAsync()),
            ["RoundId"] = _factory.RoundId.ToString(),
            ["PlayerId"] = _factory.PlayerId.ToString(),
            ["Respondent"] = nameof(RespondentType.Player)
        };

        var questions = Catalog();
        var index = 0;

        foreach (var category in questions.Categories)
        {
            foreach (var question in category.Questions)
            {
                fields[$"Answers[{index}].QuestionKey"] = question.Key;
                fields[$"Answers[{index}].Value"] = "4";
                index++;
            }
        }

        var reflectionIndex = 0;

        foreach (var question in questions.ReflectionQuestions)
        {
            fields[$"ReflectionAnswers[{reflectionIndex}].QuestionKey"] = question.Key;
            fields[$"ReflectionAnswers[{reflectionIndex}].Value"] =
                reflection.TryGetValue(question.Key, out var value) ? value : string.Empty;
            reflectionIndex++;
        }

        foreach (var pair in extra ?? new Dictionary<string, string>())
        {
            fields[pair.Key] = pair.Value;
        }

        return await client.PostAsync("/Survey/Fill", new FormUrlEncodedContent(fields));
    }

    private QuestionSet Catalog() =>
        _factory.Services.GetRequiredService<IQuestionCatalog>().Questions;

    private async Task NothingWasStoredAsync() =>
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.Equal(0, await db.FiveCSubmissions.CountAsync());
            Assert.Equal(0, await db.FiveCReflectionAnswers.CountAsync());
        });

    private static string AntiforgeryToken(string html)
    {
        const string field = "name=\"__RequestVerificationToken\"";
        const string value = "value=\"";

        var atField = html.IndexOf(field, StringComparison.Ordinal);
        Assert.True(atField >= 0, "The form rendered no antiforgery token.");

        var start = html.IndexOf(value, atField, StringComparison.Ordinal) + value.Length;
        var end = html.IndexOf('"', start);

        return html[start..end];
    }
}
