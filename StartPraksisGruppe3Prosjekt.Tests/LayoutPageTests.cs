using StartPraksisGruppe3Prosjekt.Authorization;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The shell every page is served in.
///
/// One thing is worth holding here that no view can hold for itself: the order in which
/// stylesheets reach the browser. startcompass.css is the design system, and it is the last
/// thing loaded on purpose -- a rule that arrives after it wins, whatever it says and
/// however carefully the rule it beats was written.
/// </summary>
public sealed class LayoutPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// A .cshtml.css compiles to "a[b-xxxxx]" selectors -- two classes' worth of specificity
    /// -- and the bundle is linked after startcompass.css, so a single-class .sc- rule loses
    /// to whatever it says. The template shipped "a { color: #0077cc }" in one, which beat
    /// the skip link and rendered it blue on black at 4.06:1.
    ///
    /// The file is gone, and so is the link. Asserted on the link rather than on the file
    /// because the link is what does the damage: a scoped stylesheet nothing loads is inert.
    /// </summary>
    [Fact]
    public async Task No_scoped_stylesheet_is_loaded_after_the_design_system()
    {
        var html = await ChromeAsync();

        Assert.DoesNotContain("StartPraksisGruppe3Prosjekt.styles.css", html);

        // And the design system is still the last stylesheet in.
        Assert.True(
            html.IndexOf("css/startcompass.css", StringComparison.Ordinal)
            > html.IndexOf("css/site.css", StringComparison.Ordinal),
            "startcompass.css should be loaded after site.css.");
    }

    /// <summary>
    /// WCAG 2.4.1, and first in the tab order. It reads yellow on ink, which is the pairing
    /// the rest of the chrome uses -- see .sc-skip, which is now one class like every other
    /// rule in that file rather than the a.sc-skip:link it needed to win an argument.
    /// </summary>
    [Fact]
    public async Task The_skip_link_is_the_first_thing_in_the_body()
    {
        var html = await ChromeAsync();

        Assert.Contains("class=\"sc-skip\" href=\"#main-content\"", html);

        var body = html.IndexOf("<body>", StringComparison.Ordinal);
        var skip = html.IndexOf("class=\"sc-skip\"", StringComparison.Ordinal);
        var header = html.IndexOf("class=\"sc-header\"", StringComparison.Ordinal);

        Assert.True(skip > body && skip < header, "The skip link should open the body.");

        // The target it jumps to, out of the tab order but able to take focus.
        Assert.Contains("id=\"main-content\" tabindex=\"-1\"", html);
    }

    private async Task<string> ChromeAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync("/");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }
}
