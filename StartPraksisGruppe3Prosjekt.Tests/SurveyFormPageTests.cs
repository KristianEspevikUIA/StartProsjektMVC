using StartPraksisGruppe3Prosjekt.Authorization;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The form, as it is served.
///
/// Twenty-five statements in one column was most of a phone screen per statement and five
/// screens of scrolling in all. It is now one C per panel, through the same component the
/// coach pages use -- so what is tested here is that the sections are MARKED, not that they
/// are hidden: survey.js does the hiding, and with JavaScript off the form has to stay
/// exactly what it was.
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
    public async Task Every_category_is_a_panel_and_the_save_button_is_not()
    {
        var html = await FormAsync();

        // One panel per C, labelled from the question catalog -- so replacing the question
        // set replaces the tabs, and nothing here has to be kept in step by hand.
        Assert.Contains("data-tab-label=\"Commitment\"", html);
        Assert.Contains("data-tab-label=\"Confidence\"", html);

        // Save has to be reachable from whichever C you are looking at. As a panel it
        // would be a sixth tab, and the form would make you walk to the end to submit.
        var save = html.IndexOf("Save answers", StringComparison.Ordinal);
        var lastPanel = html.LastIndexOf("data-tab-panel", StringComparison.Ordinal);
        Assert.True(save > lastPanel, "Save should sit after the last panel, outside them.");

        // Nothing hidden server side: with JavaScript off this is the form it always was.
        Assert.DoesNotContain("sc-tabs", html);
        Assert.DoesNotContain("sc-stepnav", html);
    }

    [Fact]
    public async Task Nothing_is_flagged_to_open_on_a_form_that_has_not_been_rejected()
    {
        var html = await FormAsync();

        // data-tab-open only means something after a save came back with unanswered
        // statements. On a fresh form the first C opens, as it would have read top-down.
        Assert.DoesNotContain("data-tab-open=\"true\"", html);
    }

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
