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

    /// <summary>
    /// The signed-in address is behind one round button, not written across the menu bar.
    /// It used to be a link as wide as the navigation before it, in the same uppercase,
    /// letter-spaced type, with Log out beside it.
    /// </summary>
    [Fact]
    public async Task The_signed_in_address_is_in_the_account_menu_and_not_the_bar()
    {
        var html = await ChromeAsync();

        // The button says whose account it is to a screen reader, and shows one letter.
        Assert.Contains($"aria-label=\"Account: {StartCompassFactory.CoachUserId}\"", html);
        Assert.Contains("<span class=\"sc-account__avatar\" aria-hidden=\"true\">U</span>", html);

        // The address itself is inside the menu, after the button that opens it.
        var toggle = html.IndexOf("class=\"sc-account__toggle\"", StringComparison.Ordinal);
        var menu = html.IndexOf("sc-account__menu", StringComparison.Ordinal);
        var address = html.IndexOf(
            $"<span class=\"sc-account__who-name\">{StartCompassFactory.CoachUserId}</span>",
            StringComparison.Ordinal);

        Assert.True(toggle >= 0 && menu > toggle && address > menu,
            "The address should be inside the account menu.");

        // And Log out is one of the menu's items rather than a link of its own in the bar.
        Assert.Contains("<button type=\"submit\" class=\"dropdown-item\">Log out</button>", html);
    }

    /// <summary>
    /// No "Home" in the menu: the StartCompass mark is the link home, as it is on most sites,
    /// and the menu item said the same thing again.
    /// </summary>
    [Fact]
    public async Task The_menu_has_no_home_link_because_the_brand_is_one()
    {
        var html = await ChromeAsync();

        Assert.Contains("class=\"navbar-brand\"", html);
        Assert.DoesNotContain(">Home</a>", html);
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
