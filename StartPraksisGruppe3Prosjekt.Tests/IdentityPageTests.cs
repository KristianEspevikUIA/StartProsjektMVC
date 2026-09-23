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

    /// <summary>Every Identity page. Key Insights names players, but the gate is the same on all.</summary>
    public static TheoryData<string> Pages => new()
    {
        "/Identity", "/Identity/Insights", "/Identity/Development", "/Identity/GoldStandard"
    };

    public static TheoryData<string, string, string> PagesForPlayersAndGuardians()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var url in Pages)
        {
            data.Add(url, StartCompassFactory.PlayerUserId, Roles.Player);
            data.Add(url, StartCompassFactory.GuardianUserId, Roles.Guardian);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Anonymous_request_is_refused(string url)
    {
        var response = await _factory.AnonymousClient().GetAsync(url);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect,
            $"Anonymous access should be refused, got {(int)response.StatusCode}.");
    }

    [Theory]
    [MemberData(nameof(PagesForPlayersAndGuardians))]
    public async Task Players_and_guardians_are_forbidden(string url, string userId, string role)
    {
        var response = await _factory.ClientAs(userId, role).GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(StartCompassFactory.CoachUserId, Roles.Coach)]
    [InlineData(StartCompassFactory.AdminUserId, Roles.Admin)]
    public async Task Coaches_and_administrators_can_open_every_page(string userId, string role)
    {
        var client = _factory.ClientAs(userId, role);

        foreach (var url in Pages)
        {
            await _factory.AssertOkAsync(await client.GetAsync(url));
        }
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

    /// <summary>The club's markers that no StatsBomb report can answer, each replaced by a substitute.</summary>
    private static readonly string[] ReplacedClubMarkers =
    {
        "Progressive Passes", "Match Tempo", "PPDA (Intensity)", "Opp. Half Recoveries", "Total Duels Won", "Total Regains"
    };

    /// <summary>The first word of each footnote under the two tables: the club's four terms.</summary>
    private static readonly string[] ClubFootnoteTerms = { "Accuracy", "Dribbles", "PPDA", "Interceptions" };

    /// <summary>The first word of every item in the footnote lists under the tables.</summary>
    private static string[] UnderTheTables(string html) =>
        Regex.Matches(html, "<ul class=\"sc-id-footnotes\">(.*?)</ul>", RegexOptions.Singleline)
            .SelectMany(list => Regex.Matches(list.Groups[1].Value, "<li[^>]*>(.*?)</li>", RegexOptions.Singleline))
            .Select(item => Regex.Replace(item.Groups[1].Value, "<[^>]+>", string.Empty).Trim().Split(' ')[0])
            .ToArray();

    [Fact]
    public async Task The_page_shows_all_ten_markers_each_with_a_value_and_its_source()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity?team=U14&match=2026-04-01-home-testby-u14"));

        var markers = Regex.Matches(html, "class=\"sc-id-table__marker\">([^<]*)")
            .Select(m => m.Groups[1].Value.Trim())
            .ToArray();

        Assert.Equal(new[]
        {
            "Possession %", "Final Third Passes", "Total Passes", "Pass Accuracy", "Successful Dribbles",
            "Pressures", "Counterpresses", "Tackle Success %", "Pressure Regains", "Interceptions"
        }, markers);

        Assert.Equal(10, Regex.Matches(html, "<span class=\"sc-id-value\">").Count);
        Assert.DoesNotContain("Not measured", html);

        // Values, targets, statuses and the file and page they came from.
        Assert.Contains("<span class=\"sc-id-value\">60%</span>", html);
        Assert.Contains("58–70%", html);
        Assert.Contains("sc-id-status--developing\">Developing<", html);
        Assert.Contains("Testserien G14 2026 Start U14 Testby U14.pdf</span>, page 23", html);
        Assert.Contains("Testserien G14 2026 Start U14 Testby U14.pdf</span>, page 15", html);   // counterpresses

        // Under the tables, the club's own footnotes and nothing else. Why a substitute stands in,
        // and where its provisional range comes from, opens from its "Replaces" line instead.
        Assert.Contains("rewarding overall technical security.", html);
        Assert.Equal(ClubFootnoteTerms, UnderTheTables(html));
        Assert.Equal(6, Regex.Matches(html, "<details class=\"sc-id-replaces\">").Count);
        Assert.Contains("The report has no PPDA, but it counts the pressing itself", html);
        Assert.Contains("Provisional range: Start's opponents in 20 StatsBomb reports", html);
    }

    [Theory]
    [InlineData("/Identity", "sc-id-table", "Key Tactical Insights", "sc-id-trend")]
    [InlineData("/Identity/Insights", "Key Tactical Insights", "sc-id-table", "sc-id-trend")]
    [InlineData("/Identity/Insights", "Player Highlight", "sc-id-table", "sc-id-trend")]
    [InlineData("/Identity/Development", "sc-id-trend", "sc-id-table", "Key Tactical Insights")]
    public async Task Each_part_is_a_page_of_its_own(string page, string shows, string notA, string norB)
    {
        const string match = "2026-04-01-home-testby-u14";
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync($"{page}?team=U14&match={match}"));

        Assert.Contains(shows, html);
        Assert.DoesNotContain(notA, html);
        Assert.DoesNotContain(norB, html);

        // Buttons to all three, keeping the team and the match; the page shown is the current one.
        var buttons = Regex.Matches(
                Regex.Match(html, "<nav class=\"sc-id-sections\".*?</nav>", RegexOptions.Singleline).Value,
                "<a ([^>]*)>([^<]*)</a>")
            .Select(m => (
                Label: m.Groups[2].Value,
                Href: Regex.Match(m.Groups[1].Value, "href=\"([^\"]*)\"").Groups[1].Value,
                Current: m.Groups[1].Value.Contains("aria-current=\"page\"")))
            .ToArray();

        Assert.Equal(new[]
        {
            ("Overview", $"/Identity?team=U14&match={match}", page == "/Identity"),
            ("Key Insights", $"/Identity/Insights?team=U14&match={match}", page == "/Identity/Insights"),
            ("Development over time", $"/Identity/Development?team=U14&match={match}", page == "/Identity/Development")
        }, buttons);
    }

    [Fact]
    public async Task Picking_a_team_or_a_match_stays_on_the_page()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity/Development?team=U14"));

        Assert.Contains("href=\"/Identity/Development?team=U15\"", html);
        Assert.Contains("action=\"/Identity/Development\"", html);

        // And a match in a chart's "Match by match" list opens on the charts, ringed.
        Assert.Contains("href=\"/Identity/Development?team=U14&match=2026-05-01-away-provestad-u14\"", html);
    }

    [Fact]
    public async Task Across_all_matches_the_buttons_leave_the_match_out()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity/Insights?team=U14"));

        Assert.Contains("href=\"/Identity?team=U14\"", html);
        Assert.Contains("href=\"/Identity/Development?team=U14\"", html);
    }

    [Fact]
    public async Task A_replaced_club_marker_is_never_a_row_or_a_number_under_its_own_name()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity?team=U14"));

        var rows = Regex.Matches(html, "<tr role=\"row\">(.*?)</tr>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(row => row.Contains("sc-id-table__marker"))
            .ToList();

        // Each of the club's six appears once: named in its substitute's row, where it opens to
        // the reason, and the row says its range is provisional.
        foreach (var replaced in ReplacedClubMarkers)
        {
            var row = Assert.Single(rows, r => r.Contains($"<summary>Replaces {replaced}</summary>"));
            Assert.DoesNotContain($"sc-id-table__marker\">{replaced}", row);
            Assert.Contains("<details class=\"sc-id-replaces\">", row);
            Assert.Contains("<span class=\"sc-id-provisional\">Provisional</span>", row);
        }

        // The club's own four are held to the club's ranges.
        var clubRows = rows.Where(r => !r.Contains("Replaces ")).ToList();
        Assert.Equal(4, clubRows.Count);
        Assert.All(clubRows, row => Assert.DoesNotContain("sc-id-provisional", row));
    }

    [Fact]
    public async Task Development_over_time_has_a_part_for_each_phase()
    {
        var html = WebUtility.HtmlDecode(await Coach().GetStringAsync("/Identity/Development?team=U14"));

        var development = html[html.IndexOf("id=\"identity-development\"", StringComparison.Ordinal)..];
        var inPossession = development.IndexOf("id=\"identity-development-in-possession\">In Possession</h3>", StringComparison.Ordinal);
        var outOfPossession = development.IndexOf("id=\"identity-development-out-of-possession\">Out of Possession</h3>", StringComparison.Ordinal);

        Assert.True(inPossession >= 0 && outOfPossession > inPossession, "Expected In Possession, then Out of Possession.");

        string[] Charts(string part) =>
            Regex.Matches(part, "class=\"sc-id-trend__title\">([^<]*)</h4>").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(
            new[] { "Possession %", "Final Third Passes", "Total Passes", "Pass Accuracy", "Successful Dribbles" },
            Charts(development[inPossession..outOfPossession]));
        Assert.Equal(
            new[] { "Pressures", "Counterpresses", "Tackle Success %", "Pressure Regains", "Interceptions" },
            Charts(development[outOfPossession..]));
    }

    [Theory]
    [InlineData("/Identity?team=U15")]
    [InlineData("/Identity/Insights?team=U15")]
    [InlineData("/Identity/Development?team=U15")]
    public async Task A_team_without_match_data_says_so(string url)
    {
        var response = await Coach().GetAsync(url);
        await _factory.AssertOkAsync(response);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No match data for U15", html);
        Assert.DoesNotContain("Not measured", html);
        Assert.DoesNotContain("sc-id-sections", html);   // no buttons to three pages of the same message
    }

    [Theory]
    [InlineData("/Identity?team=U99")]
    [InlineData("/Identity?team=U14&match=2020-01-01-home-nobody")]
    [InlineData("/Identity/Insights?team=U99")]
    [InlineData("/Identity/Development?team=U14&match=2020-01-01-home-nobody")]
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
        Assert.Contains("Lamine Yamal, Barcelona (23)", html);
        Assert.Contains("Why these are the gold standards", html);

        // The substitutes are explained once, under the club's intro; under the tables there are
        // only the club's footnotes. Every one of the club's six is still quoted -- name, range
        // and best at it -- where its substitute's "Replaces" line opens.
        Assert.Contains("<strong>Substitute markers.</strong>", html);
        Assert.Contains("68 – 140 / match", html);
        Assert.Contains("Stabæk U14 (peak 140)", html);
        Assert.Equal(6, Regex.Matches(html, "<span class=\"sc-id-provisional\">Provisional</span>").Count);
        Assert.Equal(ClubFootnoteTerms, UnderTheTables(html));

        foreach (var replaced in ReplacedClubMarkers)
        {
            Assert.Contains($"<summary>Replaces {replaced}</summary>", html);
        }
        Assert.Contains("The club's range: 15.5 – 19.8 p/min, best at it Bayern München (peak 19.79).", html);
    }

    [Theory]
    [InlineData("/Identity?team=U14")]
    [InlineData("/Identity/Insights?team=U14")]
    [InlineData("/Identity/Development?team=U14")]
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
