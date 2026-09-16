using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The help page: how to read the numbers.
///
/// The explanations used to be paragraphs on the 5C pages themselves, and coaches found the
/// pages were mostly text. They moved here, and the pages link to a section each -- so what
/// is worth holding is that the sections those links point at exist, that the thresholds in
/// the text are the ones the rules use, and that the page asks for a sign-in like every page
/// it describes.
/// </summary>
public sealed class HelpPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Not on HomeController, which is [AllowAnonymous]: nothing here is secret, but it
    /// describes pages only a signed-in user can reach, and it asks for the same.
    /// </summary>
    [Fact]
    public async Task The_help_page_asks_for_a_sign_in()
    {
        var response = await _factory.AnonymousClient().GetAsync("/Help");

        Assert.False(response.IsSuccessStatusCode, "The help page should require sign-in.");
    }

    /// <summary>
    /// Every anchor a page links to. Renaming one here without its link leaves a coach on
    /// the top of a long page instead of at the answer.
    /// </summary>
    [Theory]
    [InlineData("scores")]
    [InlineData("team-page")]
    [InlineData("player-overview")]
    [InlineData("follow-up")]
    [InlineData("player-page")]
    public async Task Every_section_a_page_links_to_is_there(string anchor)
    {
        var html = await HelpPageAsync();

        Assert.Contains($"id=\"{anchor}\"", html);
    }

    /// <summary>
    /// The question the page was asked for in the first place: a player marked for follow-up
    /// with a difference of 0.0. The answer names the threshold from the rules.
    /// </summary>
    [Fact]
    public async Task It_explains_why_follow_up_and_a_zero_difference_can_go_together()
    {
        var html = await HelpPageAsync();

        Assert.Contains("Why can a player need follow-up when the difference is 0?", html);
        Assert.Contains($"below {FiveCRules.FollowUpThreshold:0.0}", html);
        Assert.Contains($"at least {FiveCRules.MinimumAnswersForFollowUp} answered statements", html);
    }

    /// <summary>The method that used to sit over the team table.</summary>
    [Fact]
    public async Task It_explains_how_a_team_average_is_built_and_when_it_is_withheld()
    {
        var html = await HelpPageAsync();

        Assert.Contains("It is an average of players, not of answers.", html);
        Assert.Contains(
            $"at least {CanViewTeamAggregateRequirement.MinimumResponses} people of that role answered",
            html);
    }

    private async Task<string> HelpPageAsync()
    {
        var response = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync("/Help");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }
}
