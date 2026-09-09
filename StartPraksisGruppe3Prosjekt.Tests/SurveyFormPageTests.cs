using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The form, as it is served.
///
/// Twenty-five statements in one column was most of a phone screen per statement and five
/// screens of scrolling in all. It is now a BLOCK OF FIVE per panel, through the same
/// component the coach pages use -- so what is tested here is that the sections are MARKED,
/// not that they are hidden: survey.js does the hiding, and with JavaScript off the form
/// has to stay exactly what it was.
///
/// A block is not a C. The statements are shuffled across the whole form -- see
/// IQuestionOrder -- so a panel normally holds statements from four or five categories.
/// Which C a statement belongs to is on the statement, as a coloured marker.
/// </summary>
public sealed class SurveyFormPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_block_is_a_panel_and_the_save_button_is_not()
    {
        var html = await FormAsync();

        // Decoded first: the default HtmlEncoder writes anything outside Basic Latin as a
        // numeric entity, so the en dash in "Statements 1-5" reaches the browser as
        // &#x2013;. Asserting on the entity would be asserting on the encoder.
        var text = WebUtility.HtmlDecode(html);

        // Panels are ranges of the shuffled sequence, not the five C's. The first and the
        // last are asserted by name because those two numbers never move.
        Assert.Contains("data-tab-label=\"Statements 1\u20135\"", text);
        Assert.Contains("data-tab-label=\"Statements 21\u201325\"", text);

        // The category names are gone from the panel labels and are on the statements
        // instead, as markers. Asserted as a label so this fails if a C ever creeps back
        // into the tab strip -- the whole point of the shuffle is that a panel is not a C.
        Assert.DoesNotContain("data-tab-label=\"Commitment\"", html);
        Assert.Contains("sc-qcolor", html);

        // Save has to be reachable from whichever block you are looking at. As a panel it
        // would be a sixth tab, and the form would make you walk to the end to submit.
        var save = html.IndexOf("Save answers", StringComparison.Ordinal);
        var lastPanel = html.LastIndexOf("data-tab-panel", StringComparison.Ordinal);
        Assert.True(save > lastPanel, "Save should sit after the last panel, outside them.");

        // Nothing hidden server side: with JavaScript off this is the form it always was.
        Assert.DoesNotContain("sc-tabs", html);
        Assert.DoesNotContain("sc-stepnav", html);
    }

    /// <summary>
    /// Shuffling is only safe if it loses nothing. Every question in the catalog has to be
    /// on the page exactly once -- a shuffle that dropped one would be a form that cannot
    /// be submitted, and the validation message would point at a statement nobody saw.
    /// </summary>
    [Fact]
    public async Task The_shuffled_form_holds_every_question_exactly_once()
    {
        var catalog = Catalog();
        var posted = QuestionKeysOf(await FormAsync());

        Assert.Equal(
            catalog.Questions.AllQuestions.Select(q => q.Key).OrderBy(k => k, StringComparer.Ordinal),
            posted.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// The order is stable for one player and one period.
    ///
    /// This is what makes a correction possible. Somebody who saves, comes back and changes
    /// one answer has to meet the same form: an order that was random per REQUEST would
    /// renumber the statements under them halfway through.
    /// </summary>
    [Fact]
    public async Task The_same_player_and_period_get_the_same_order_twice()
    {
        Assert.Equal(QuestionKeysOf(await FormAsync()), QuestionKeysOf(await FormAsync()));
    }

    /// <summary>
    /// And it is actually shuffled. Without this the whole feature can regress to catalog
    /// order and every other test here still passes.
    /// </summary>
    [Fact]
    public async Task The_form_is_not_in_catalog_order()
    {
        var catalogOrder = Catalog().Questions.AllQuestions.Select(q => q.Key).ToList();

        Assert.NotEqual(catalogOrder, QuestionKeysOf(await FormAsync()));
    }

    [Fact]
    public async Task Nothing_is_flagged_to_open_on_a_form_that_has_not_been_rejected()
    {
        var html = await FormAsync();

        // data-tab-open only means something after a save came back with unanswered
        // statements. On a fresh form the first C opens, as it would have read top-down.
        Assert.DoesNotContain("data-tab-open=\"true\"", html);
    }

    private IQuestionCatalog Catalog()
    {
        using var scope = _factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IQuestionCatalog>();
    }

    /// <summary>
    /// The question keys the form posts back, in the order they appear on the page. The
    /// hidden field is the one place the order is machine-readable, which is what makes it
    /// the right thing to assert on -- it is also exactly what the browser sends.
    /// </summary>
    private static IReadOnlyList<string> QuestionKeysOf(string html) =>
        // \s+ between the two attributes, not a space: Razor keeps the line break and the
        // indentation the view wrote them on.
        Regex.Matches(html, @"name=""Answers\[\d+\]\.QuestionKey""\s+value=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

    private async Task<string> FormAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.PlayerUserId, Roles.Player)
            .GetAsync($"/Survey/Fill?roundId={_factory.RoundId}"
                + $"&playerId={_factory.PlayerId}&respondent=Player");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }
}
