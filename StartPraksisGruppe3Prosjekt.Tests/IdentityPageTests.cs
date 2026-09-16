using System.Net;
using System.Text.RegularExpressions;
using StartPraksisGruppe3Prosjekt.Authorization;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The Identity pages through the whole pipeline: who may open them, and what they say.
///
/// Coaches and administrators only. The page names players, so the negative cases -- a player,
/// a guardian, nobody -- matter as much as the positive ones, and so does the menu link.
/// Match data is the fictional squad (see StartCompassFactory.UseFictionalIdentityData).
/// </summary>
public sealed class IdentityPageTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Coach() => _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach);

    [Theory]
    [InlineData("/Identity")]
    [InlineData("/Identity/GoldStandard")]
    public async Task Anonymous_request_is_refused(string url)
    {
        var response = await _factory.AnonymousClient().GetAsync(url);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect,
            $"Anonymous access should be refused, got {(int)response.StatusCode}.");
    }

    [Theory]
    [InlineData("/Identity", StartCompassFactory.PlayerUserId, Roles.Player)]
    [InlineData("/Identity", StartCompassFactory.GuardianUserId, Roles.Guardian)]
    [InlineData("/Identity/GoldStandard", StartCompassFactory.PlayerUserId, Roles.Player)]
    [InlineData("/Identity/GoldStandard", StartCompassFactory.GuardianUserId, Roles.Guardian)]
    public async Task Players_and_guardians_are_forbidden(string url, string userId, string role)
    {
        var response = await _factory.ClientAs(userId, role).GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(StartCompassFactory.CoachUserId, Roles.Coach)]
    [InlineData(StartCompassFactory.AdminUserId, Roles.Admin)]
    public async Task Coaches_and_administrators_can_open_both_pages(string userId, string role)
    {
        var client = _factory.ClientAs(userId, role);

        await _factory.AssertOkAsync(await client.GetAsync("/Identity"));
        await _factory.AssertOkAsync(await client.GetAsync("/Identity/GoldStandard"));
    }

    [Fact]
    public async Task The_menu_links_to_the_page_for_coaches_and_administrators_only()
    {
        const string link = "href=\"/Identity\"";

        Assert.Contains(link, await Coach().GetStringAsync("/"));
        Assert.Contains(link, await _factory.ClientAs(StartCompassFactory.AdminUserId, Roles.Admin).GetStringAsync("/"));
        Assert.DoesNotContain(link, await _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player).GetStringAsync("/"));
        Assert.DoesNotContain(link, await _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian).GetStringAsync("/"));
    }

    [Fact]
    public async Task The_page_shows_all_ten_markers_and_says_not_measured_for_the_six_without_data()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity?team=U14&match=2026-04-01-home-testby-u14"));

        foreach (var marker in new[]
                 {
                     "Possession %", "Progressive Passes", "Match Tempo", "Pass Accuracy", "Successful Dribbles",
                     "PPDA (Intensity)", "Opp. Half Recoveries", "Total Duels Won", "Total Regains", "Interceptions"
                 })
        {
            Assert.Contains($">{marker}</th>", html);
        }

        Assert.Equal(6, Regex.Matches(html, "sc-id-value--none\">Not measured<").Count);

        // Values, targets, statuses and the file and page they came from.
        Assert.Contains("<span class=\"sc-id-value\">60%</span>", html);
        Assert.Contains("58–70%", html);
        Assert.Contains("sc-id-status--developing\">Developing<", html);
        Assert.Contains("Testserien G14 2026 Start U14 Testby U14.pdf</span>, page 23", html);

        // The Gold Standard footnotes and the not-measured reasons.
        Assert.Contains("rewarding overall technical security.", html);
        Assert.Contains("The StatsBomb match report has no PPDA.", html);

        Assert.Contains("Key Tactical Insights", html);
        Assert.Contains("Player Highlight", html);
        Assert.Contains("Development over time", html);
    }

    [Fact]
    public async Task No_number_stands_in_for_a_marker_that_is_not_measured()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity?team=U14"));

        // Every row that says Not measured has a dash for status, never a badge.
        var rows = Regex.Matches(html, "<tr role=\"row\">(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(row => row.Contains("Not measured"))
            .ToList();

        Assert.Equal(6, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain("sc-id-status", row);
            Assert.DoesNotContain("sc-id-value\">", row);
        });
    }

    [Fact]
    public async Task A_team_without_match_data_says_so()
    {
        var response = await Coach().GetAsync("/Identity?team=U15");
        await _factory.AssertOkAsync(response);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No match data for U15", html);
        Assert.DoesNotContain("Not measured", html);
    }

    [Theory]
    [InlineData("/Identity?team=U99")]
    [InlineData("/Identity?team=U14&match=2020-01-01-home-nobody")]
    public async Task An_unknown_team_or_match_is_not_found(string url)
    {
        var response = await Coach().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_gold_standard_reference_lists_every_marker_with_range_and_best_at_it()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity/GoldStandard"));

        Assert.Contains("Gold Standard – The 10 Identity Markers", html);
        Assert.Contains("<strong>Barcelona, Bayern München, PSG, Como, Racing Santander & Malmö FF</strong>", html);
        Assert.Contains("15.5 – 19.8 p/min", html);
        Assert.Contains("Lamine Yamal, Barcelona (23)", html);
        Assert.Contains("Why these are the gold standards", html);
    }

    [Theory]
    [InlineData("/Identity?team=U14")]
    [InlineData("/Identity/GoldStandard")]
    public async Task The_pages_keep_to_the_content_security_policy(string url)
    {
        // No unsafe-inline: no style attribute and no inline script anywhere on these pages.
        var response = await Coach().GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\ssrc=)[^>]*>", RegexOptions.IgnoreCase), html);
    }
}
