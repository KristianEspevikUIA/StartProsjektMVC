using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The succession pages through the whole pipeline: who may open them, who may rate, what a
/// saved rating does, and what the pages then say.
///
/// Coaches and administrators only, and only a coach rates. These are the staff's working
/// judgements of players, most of them minors, so the negative cases -- a player, a guardian,
/// nobody, an administrator trying to rate -- matter as much as the positive ones.
/// </summary>
public sealed class SuccessionPageTests : IAsyncLifetime
{
    /// <summary>A second coach, so there is somebody to disagree with.</summary>
    private const string SecondCoachUserId = "user-coach-2";

    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Coach() => _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach);

    private HttpClient Admin() => _factory.ClientAs(StartCompassFactory.AdminUserId, Roles.Admin);

    private string PlayerPage => $"/Succession/Player/{_factory.PlayerId}";

    private string RatePage => $"/Succession/Rate/{_factory.PlayerId}";

    // -----------------------------------------------------------------------------------
    // Who
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_requests_are_refused()
    {
        foreach (var url in new[] { "/Succession", "/Succession/Formation", PlayerPage, RatePage })
        {
            var response = await _factory.AnonymousClient().GetAsync(url);

            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect,
                $"{url}: anonymous access should be refused, got {(int)response.StatusCode}.");
        }
    }

    [Theory]
    [InlineData(StartCompassFactory.PlayerUserId, Roles.Player)]
    [InlineData(StartCompassFactory.GuardianUserId, Roles.Guardian)]
    public async Task Players_and_guardians_are_forbidden_even_their_own(string userId, string role)
    {
        // PlayerId is this player's own record, and the guardian's child. Neither may see what
        // the coaches have written about them here -- it is not feedback.
        var client = _factory.ClientAs(userId, role);

        foreach (var url in new[] { "/Succession", "/Succession/Formation", PlayerPage, RatePage })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Coaches_and_administrators_can_open_the_pages()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7);

        foreach (var client in new[] { Coach(), Admin() })
        {
            await _factory.AssertOkAsync(await client.GetAsync("/Succession"));
            await _factory.AssertOkAsync(await client.GetAsync("/Succession/Formation"));
            await _factory.AssertOkAsync(await client.GetAsync("/Succession/Formation?formation=3-5-2"));
            await _factory.AssertOkAsync(await client.GetAsync(PlayerPage));
        }
    }

    [Fact]
    public async Task The_menu_links_to_it_for_coaches_and_administrators_only()
    {
        const string link = "href=\"/Succession\"";

        Assert.Contains(link, await Coach().GetStringAsync("/"));
        Assert.Contains(link, await Admin().GetStringAsync("/"));
        Assert.DoesNotContain(link, await _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player).GetStringAsync("/"));
        Assert.DoesNotContain(link, await _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian).GetStringAsync("/"));
    }

    [Fact]
    public async Task The_help_explains_it_to_coaches_only()
    {
        // The method behind a ranking of players is staff reading. A player opening Help sees
        // the 5C sections and nothing about how the coaches rate them.
        Assert.Contains("id=\"succession\"", await Coach().GetStringAsync("/Help"));
        Assert.DoesNotContain("id=\"succession\"", await _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player).GetStringAsync("/Help"));
    }

    [Fact]
    public async Task An_administrator_sees_but_does_not_rate()
    {
        // A view on a player's ability is a coach's to give. An admin who is also a coach has
        // the coach role for it.
        Assert.Equal(HttpStatusCode.Forbidden, (await Admin().GetAsync(RatePage)).StatusCode);
        Assert.DoesNotContain("Rate this player", await Admin().GetStringAsync(PlayerPage));
    }

    [Fact]
    public async Task Unknown_things_are_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Coach().GetAsync("/Succession/Player/99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Coach().GetAsync("/Succession/Formation?formation=2-3-5")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Coach().GetAsync("/Succession?team=99999")).StatusCode);
    }

    // -----------------------------------------------------------------------------------
    // Rating
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_coach_rating_is_saved_for_the_current_cycle_under_their_own_name()
    {
        var response = await PostRatingAsync(Coach(), Rating(physical: 7, position: "RB"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var catalog = services.GetRequiredService<ISuccessionCatalog>();

            var saved = await db.SuccessionAssessments.Include(a => a.Ratings).SingleAsync();

            Assert.Equal(_factory.PlayerId, saved.PlayerId);
            Assert.Equal(StartCompassFactory.CoachUserId, saved.RaterUserId);
            Assert.Equal(catalog.CycleOf(DateOnly.FromDateTime(DateTime.UtcNow)).StartsOn, saved.CycleStartsOn);
            Assert.Equal(6, saved.Ratings.Count);
            Assert.Equal("RB", saved.FirstPosition);
            Assert.Equal(catalog.Settings.Version, saved.CatalogVersion);
        });
    }

    [Fact]
    public async Task Rating_again_in_the_same_cycle_is_a_correction_not_a_second_row()
    {
        await PostRatingAsync(Coach(), Rating(physical: 4, notes: "First go."));
        var again = await PostRatingAsync(Coach(), Rating(physical: 9));

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var saved = await db.SuccessionAssessments.Include(a => a.Ratings).SingleAsync();

            Assert.Equal(9, saved.Ratings.Single(r => r.RatingKey == "physical").Value);
            Assert.Equal(6, await db.SuccessionRatings.CountAsync());

            // A replace, not a merge: the note left out the second time is gone.
            Assert.Null(saved.Notes);
        });
    }

    [Fact]
    public async Task A_rating_left_out_is_refused_and_nothing_is_saved()
    {
        var form = Rating(physical: 7);
        form.RemoveAll(f => f.Key == "Ratings[availability]");

        var response = await PostRatingAsync(Coach(), form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Give Availability a rating", html);
        await AssertNothingSavedAsync();
    }

    [Theory]
    [InlineData("Ratings[physical]", "11")]
    [InlineData("FirstPosition", "SW")]
    [InlineData("AbilityCategory", "superstar")]
    [InlineData("SuccessionRisk", "purple")]
    [InlineData("Ratings[speed]", "5")]
    public async Task A_value_the_form_does_not_offer_is_refused(string field, string value)
    {
        var form = Rating(physical: 7);
        form.RemoveAll(f => f.Key == field);
        form.Add(new(field, value));

        var response = await PostRatingAsync(Coach(), form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task The_same_position_twice_is_refused()
    {
        var form = Rating(physical: 7, position: "RB");
        form.Add(new("SecondPosition", "RB"));

        var response = await PostRatingAsync(Coach(), form);

        Assert.Contains("Name each position once", await response.Content.ReadAsStringAsync());
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task A_rating_without_an_antiforgery_token_is_refused()
    {
        var response = await Coach().PostAsync(RatePage, new FormUrlEncodedContent(Rating(physical: 7)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingSavedAsync();
    }

    [Fact]
    public async Task The_form_starts_from_the_coachs_last_cycle()
    {
        // Rated eight weeks ago and not yet this cycle: the form is filled in from that, and says so.
        await SeedAsync(StartCompassFactory.CoachUserId, 6, cyclesAgo: 1, position: "LCB");

        var html = await Coach().GetStringAsync(RatePage);

        Assert.Contains("Filled in from your assessment in", html);
        Assert.Contains("value=\"LCB\" selected=\"selected\"", html);
    }

    // -----------------------------------------------------------------------------------
    // What the pages say
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_player_page_puts_the_coaches_side_by_side_and_marks_where_they_are_apart()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 4);
        await SeedAsync(SecondCoachUserId, 8);

        var html = await Coach().GetStringAsync(PlayerPage);

        // The signed-in coach's own column is "You", the other is named, and together is the
        // average of the two: (4 + 8) / 2 on every rating, four points apart.
        Assert.Contains("You", html);
        Assert.Contains("Former coach", html); // no Identity row for the fixture's second coach
        Assert.Contains("Together", html);
        Assert.Contains("sc-badge sc-badge--alert\">4<", html);
    }

    [Fact]
    public async Task The_board_has_a_column_each_for_the_1st_2nd_and_3rd_position()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7, position: "RB", second: "RWB", third: "RCB");

        var html = await Coach().GetStringAsync("/Succession");

        Assert.Contains(">1st</th>", html);
        Assert.Contains(">2nd</th>", html);
        Assert.Contains(">3rd</th>", html);

        // In that order, one per column, each with its full name on hover.
        var first = html.IndexOf("title=\"Right-back\">RB</abbr>", StringComparison.Ordinal);
        var second = html.IndexOf("title=\"Right wing-back\">RWB</abbr>", StringComparison.Ordinal);
        var third = html.IndexOf("title=\"Right centre-back\">RCB</abbr>", StringComparison.Ordinal);

        Assert.True(first >= 0 && first < second && second < third, "Expected RB, RWB, RCB in that order.");
    }

    [Fact]
    public async Task Coaches_who_wrote_different_1st_positions_are_shown_as_a_split()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7, position: "RB");
        await SeedAsync(SecondCoachUserId, 7, position: "LB");

        var html = await Coach().GetStringAsync("/Succession");

        Assert.Contains("sc-position sc-position--split", html);
    }

    [Fact]
    public async Task The_coach_count_opens_into_who_the_coaches_are()
    {
        // A real account for the second coach, so the page has a name to show for them.
        await _factory.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();

            var created = await users.CreateAsync(new IdentityUser
            {
                Id = SecondCoachUserId,
                UserName = "second.coach@example.test",
                Email = "second.coach@example.test"
            });

            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        });

        await SeedAsync(StartCompassFactory.CoachUserId, 6);
        await SeedAsync(SecondCoachUserId, 8);

        var html = await Coach().GetStringAsync("/Succession");

        Assert.Contains("<details class=\"sc-raters\">", html);
        Assert.Contains("<summary>2 coaches</summary>", html);

        // The signed-in coach by "You", the other by the part of their address before the @ --
        // never the whole address, and never the Identity id.
        Assert.Contains("<span class=\"sc-raters__name\">You</span>", html);
        Assert.Contains("<span class=\"sc-raters__name\">second.coach</span>", html);
        Assert.DoesNotContain("second.coach@example.test", html);
        Assert.DoesNotContain(SecondCoachUserId, html);
    }

    [Fact]
    public async Task A_row_from_an_earlier_cycle_lists_that_cycles_coaches_and_says_so()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7, cyclesAgo: 1);

        var html = await Coach().GetStringAsync("/Succession");

        Assert.Contains("<summary>1 coach</summary>", html);
        Assert.Contains("<p class=\"sc-raters__note\">In ", html);
    }

    [Fact]
    public async Task Opening_a_player_is_written_to_the_audit_log()
    {
        await _factory.AssertOkAsync(await Coach().GetAsync(PlayerPage));

        await AssertLoggedAsync("Succession/Player");
    }

    [Fact]
    public async Task The_board_logs_the_players_it_shows_ratings_for()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7);

        await _factory.AssertOkAsync(await Coach().GetAsync("/Succession"));

        await AssertLoggedAsync("Succession/Overview");

        // The other player has no ratings and no contract, so only their code was on the page.
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.False(await db.PlayerAccessEvents.AnyAsync(a =>
                a.PlayerId == _factory.OtherPlayerId && a.Context == "Succession/Overview"));
        });
    }

    [Fact]
    public async Task The_best_eleven_puts_a_rated_player_in_their_position()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");

        var html = await Coach().GetStringAsync("/Succession/Formation");

        Assert.Contains("TS-TEST-01", html);
        Assert.Contains("10 positions have nobody named for it", html);
        Assert.Contains("Nobody rated in this cycle has CF among their three positions", html);
    }

    [Fact]
    public async Task A_player_not_rated_this_cycle_shows_the_last_cycle_marked_as_such()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 7, cyclesAgo: 1);

        var html = await Coach().GetStringAsync("/Succession");

        Assert.Contains("sc-row--stale", html);
        Assert.Contains("From ", html);
    }

    [Fact]
    public async Task Club_details_are_saved_and_a_contract_type_outside_the_list_is_refused()
    {
        var client = Admin();
        var token = AntiforgeryToken(await client.GetStringAsync(PlayerPage));

        var saved = await client.PostAsync($"/Succession/Profile/{_factory.PlayerId}", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Profile.ContractType", "pro"),
            new KeyValuePair<string, string>("Profile.ContractEndsOn", "2027-06-30"),
            new KeyValuePair<string, string>("Profile.TrainingGroup", "u19")
        }));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var refused = await client.PostAsync($"/Succession/Profile/{_factory.PlayerId}", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Profile.ContractType", "lifetime")
        }));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var profile = await db.PlayerSuccessionProfiles.SingleAsync();

            Assert.Equal("pro", profile.ContractType);
            Assert.Equal(new DateOnly(2027, 6, 30), profile.ContractEndsOn);
            Assert.Equal("u19", profile.TrainingGroup);
        });
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>A complete rating form: every rating at 6, physical as given.</summary>
    private static List<KeyValuePair<string, string>> Rating(int physical, string? position = null, string? notes = null)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("Ratings[physical]", physical.ToString()),
            new("Ratings[technical]", "6"),
            new("Ratings[tactical]", "6"),
            new("Ratings[mental]", "6"),
            new("Ratings[professionalism]", "6"),
            new("Ratings[availability]", "6"),
            new("AbilityCategory", "potential"),
            new("SuccessionRisk", "amber")
        };

        if (position is not null)
        {
            form.Add(new("FirstPosition", position));
        }

        if (notes is not null)
        {
            form.Add(new("Notes", notes));
        }

        return form;
    }

    /// <summary>Opens the form the way a coach does, for the token, then posts it back.</summary>
    private async Task<HttpResponseMessage> PostRatingAsync(HttpClient client, List<KeyValuePair<string, string>> form)
    {
        var page = await client.GetAsync(RatePage);
        await _factory.AssertOkAsync(page);

        var fields = new List<KeyValuePair<string, string>>(form)
        {
            new("__RequestVerificationToken", AntiforgeryToken(await page.Content.ReadAsStringAsync()))
        };

        return await client.PostAsync(RatePage, new FormUrlEncodedContent(fields));
    }

    /// <summary>An assessment through the real service, every rating at one value.</summary>
    private Task SeedAsync(
        string raterUserId,
        int value,
        int cyclesAgo = 0,
        string position = "RB",
        string? second = null,
        string? third = null) =>
        _factory.WithServicesAsync(async services =>
        {
            var planning = services.GetRequiredService<ISuccessionPlanningService>();
            var catalog = services.GetRequiredService<ISuccessionCatalog>();

            var cycle = catalog.CycleOf(DateOnly.FromDateTime(DateTime.UtcNow));
            for (var i = 0; i < cyclesAgo; i++)
            {
                cycle = cycle.Previous(catalog.Settings.Cycle);
            }

            var assessment = SuccessionMathTests.Assessment(raterUserId, value, value, value, value, value, value);
            assessment.FirstPosition = position;
            assessment.SecondPosition = second;
            assessment.ThirdPosition = third;

            await planning.SaveAsync(_factory.PlayerId, raterUserId, cycle, assessment);
        });

    private Task AssertNothingSavedAsync() =>
        _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.False(await db.SuccessionAssessments.AnyAsync());
        });

    private Task AssertLoggedAsync(string context) =>
        _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var entry = await db.PlayerAccessEvents
                .AsNoTracking()
                .Where(a => a.PlayerId == _factory.PlayerId && a.Context == context)
                .SingleOrDefaultAsync();

            Assert.NotNull(entry);
            Assert.Equal(StartCompassFactory.CoachUserId, entry!.ViewedByUserId);
            Assert.Equal(Roles.Coach, entry.ViewedByRole);
        });

    private static string AntiforgeryToken(string html)
    {
        const string field = "name=\"__RequestVerificationToken\"";
        const string value = "value=\"";

        var atField = html.IndexOf(field, StringComparison.Ordinal);
        Assert.True(atField >= 0, "The page rendered no antiforgery token.");

        var start = html.IndexOf(value, atField, StringComparison.Ordinal) + value.Length;
        var end = html.IndexOf('"', start);

        return html[start..end];
    }
}
