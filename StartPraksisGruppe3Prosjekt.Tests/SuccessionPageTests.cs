using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services;
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
        Assert.Contains("Nobody rated in this cycle has LST among their three positions", html);
    }

    // -----------------------------------------------------------------------------------
    // A team in a formation, and moving players about
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_team_opens_in_a_formation_written_with_the_goalkeeper()
    {
        // "Click into a team -- say G17 -- and see the players in a 1-3-5-2."
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");

        var html = await Coach().GetStringAsync($"/Succession/Formation?team={_factory.TeamId}&formation=1-3-5-2");

        Assert.Contains("<h1>3-5-2</h1>", html);
        Assert.Contains("1-3-5-2 with the goalkeeper", html);
        Assert.Contains("TS-TEST-01", html);

        // The team's own link is the current one, and keeps the formation.
        Assert.Contains($"aria-current=\"page\" href=\"/Succession/Formation?formation=3-5-2&amp;team={_factory.TeamId}\"", html);
    }

    [Fact]
    public async Task Every_team_is_a_link_into_the_best_eleven()
    {
        var html = await Coach().GetStringAsync("/Succession/Formation?formation=3-5-2");

        Assert.Contains($"href=\"/Succession/Formation?formation=3-5-2&amp;team={_factory.TeamId}\">Test team</a>", html);

        // And from the coach's own list of teams.
        Assert.Contains($"href=\"/Succession/Formation?team={_factory.TeamId}\"", await Coach().GetStringAsync("/Coach"));
    }

    [Fact]
    public async Task Moving_between_the_board_and_the_eleven_keeps_the_team()
    {
        var board = await Coach().GetStringAsync($"/Succession?team={_factory.TeamId}");
        var eleven = await Coach().GetStringAsync($"/Succession/Formation?team={_factory.TeamId}");

        Assert.Contains($"href=\"/Succession/Formation?team={_factory.TeamId}\">Best eleven</a>", board);
        Assert.Contains($"href=\"/Succession?team={_factory.TeamId}\">Squad board</a>", eleven);
    }

    [Fact]
    public async Task The_rest_of_the_squad_is_on_the_bench_and_in_the_data_the_pitch_is_moved_with()
    {
        // Two goalkeepers: one starts, the other is the bench a coach can bring on.
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");
        await SeedAsync(StartCompassFactory.CoachUserId, 6, position: "GK", playerId: _factory.OtherPlayerId);

        var html = await Coach().GetStringAsync("/Succession/Formation");

        Assert.Contains("Substitutes", html);
        Assert.Contains($"<a class=\"sc-sub\" href=\"/Succession/Player/{_factory.OtherPlayerId}", html);
        Assert.Contains("<script src=\"/js/lineup.js", html);

        var data = LineupData(html);
        var slots = data.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("position").GetString()).ToList();
        var pick = data.GetProperty("pick").EnumerateArray()
            .Select(p => p.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : p.GetInt32())
            .ToList();
        var players = data.GetProperty("players").EnumerateArray().ToList();

        Assert.Equal(11, slots.Count);
        Assert.Equal(_factory.PlayerId, pick[slots.IndexOf("GK")]);
        Assert.Equal(1, pick.Count(p => p is not null));

        // Strongest first, both of them, with what the script needs to say where they fit.
        Assert.Equal(new[] { "TS-TEST-01", "TS-TEST-02" }, players.Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(1, players[1].GetProperty("positions").GetProperty("GK").GetInt32());
        Assert.Equal(6.0, players[1].GetProperty("overall").GetDouble());
        Assert.Equal("TS-TEST-02", players[1].GetProperty("name").GetString());
        Assert.Equal(2.0, data.GetProperty("outOfPositionPenalty").GetDouble());
        Assert.Equal("Goalkeeper", data.GetProperty("slots")[slots.IndexOf("GK")].GetProperty("unit").GetString());
        Assert.Equal($"/Succession/Player/{_factory.OtherPlayerId}?cycle=", players[1].GetProperty("url").GetString()![..^10]);

        // The bench shows a number for the substitute too, so opening the page logs them.
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.True(await db.PlayerAccessEvents.AnyAsync(a =>
                a.PlayerId == _factory.OtherPlayerId && a.Context == "Succession/Formation"));
        });
    }

    [Fact]
    public async Task The_best_eleven_shows_first_names_and_the_team_rating()
    {
        // "Less on the players: name, position." The first name the club entered for the
        // welcome -- the one staff page that shows it -- and the code where there is none.
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");
        await SeedAsync(StartCompassFactory.CoachUserId, 6, position: "GK", playerId: _factory.OtherPlayerId);
        await FirstNameAsync(_factory.PlayerId, "Alex");

        var html = await Coach().GetStringAsync("/Succession/Formation");

        Assert.Contains("<span class=\"sc-token__name\">Alex</span>", html);
        Assert.Contains("<span class=\"sc-sub__name\">TS-TEST-02</span>", html);
        Assert.Equal("Alex", LineupData(html).GetProperty("players")[0].GetProperty("name").GetString());

        // The keeper in their 1st position, alone on the pitch: the team rating is theirs.
        Assert.Contains("data-lineup-stat=\"rating\">8.0</span>", html);
        Assert.Contains("data-lineup-unit=\"Goalkeeper\"", html);

        // Still only here: the board keeps to codes.
        Assert.DoesNotContain("Alex", await Coach().GetStringAsync("/Succession"));
    }

    [Fact]
    public async Task Two_players_with_the_same_first_name_are_told_apart_by_code()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");
        await SeedAsync(StartCompassFactory.CoachUserId, 6, position: "GK", playerId: _factory.OtherPlayerId);
        await FirstNameAsync(_factory.PlayerId, "Alex");
        await FirstNameAsync(_factory.OtherPlayerId, "alex");

        var html = await Coach().GetStringAsync("/Succession/Formation");
        var players = LineupData(html).GetProperty("players");

        Assert.Equal("Alex", players[0].GetProperty("name").GetString());
        Assert.Equal("TS-TEST-01", players[0].GetProperty("tag").GetString());
        Assert.Equal("TS-TEST-02", players[1].GetProperty("tag").GetString());
        Assert.Contains("<span class=\"sc-token__name\">Alex<span class=\"sc-token__tag\">TS-TEST-01</span></span>", html);
    }

    [Fact]
    public async Task Every_position_has_a_spot_on_the_pitch_and_the_pitch_is_drawn()
    {
        // Players stand on the spot for their position, a class per position in the stylesheet.
        // A position added to the file without one would be drawn in the middle of the pitch.
        var client = _factory.AnonymousClient();
        var css = await client.GetStringAsync("/css/startcompass.css");

        await _factory.WithServicesAsync(services =>
        {
            var catalog = services.GetRequiredService<ISuccessionCatalog>();

            Assert.All(catalog.Settings.Positions, position =>
                Assert.Matches($@"\.sc-spot--{position.Key}\s*\{{[^}}]*left:[^}}]*top:", css));

            return Task.CompletedTask;
        });

        var pitch = await client.GetAsync("/img/pitch.svg");
        Assert.Equal(HttpStatusCode.OK, pitch.StatusCode);
        Assert.Equal("image/svg+xml", pitch.Content.Headers.ContentType?.MediaType);
        Assert.Contains("url(\"../img/pitch.svg\")", css);

        // A browser draws nothing at all from an SVG that is not well-formed XML -- a "--" in
        // a comment is enough -- and the pitch is then a plain green box.
        System.Xml.Linq.XDocument.Parse(await pitch.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Each_player_on_the_pitch_is_placed_by_their_position()
    {
        await SeedAsync(StartCompassFactory.CoachUserId, 8, position: "GK");

        var html = await Coach().GetStringAsync("/Succession/Formation");

        Assert.Contains("class=\"sc-token sc-spot--GK\"", html);
        Assert.Contains("class=\"sc-token sc-token--empty sc-spot--ACM\"", html);
    }

    [Fact]
    public void The_data_block_cannot_be_closed_by_what_is_in_it()
    {
        var model = new ViewModels.Succession.SuccessionFormationViewModel
        {
            Filter = null!,
            Formation = new FormationDefinition(),
            Editor = new ViewModels.Succession.LineupEditorData(
                Array.Empty<ViewModels.Succession.LineupSlot>(),
                Array.Empty<int>(),
                new[]
                {
                    new ViewModels.Succession.LineupPlayer(
                        1, "TS-X", "</script><script>alert(1)</script>", "<b>", null, 7, "Developing",
                        new Dictionary<string, int>(), false, "/x")
                },
                Array.Empty<int?>(),
                Array.Empty<double>(),
                2,
                8,
                6)
        };

        Assert.DoesNotContain("<", model.EditorJson);
        Assert.DoesNotContain(">", model.EditorJson);
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
    private static System.Text.Json.JsonElement LineupData(string html)
    {
        const string open = "<script type=\"application/json\" id=\"lineup-data\">";

        var start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "The page has no lineup data block.");
        start += open.Length;

        var json = html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)];

        return System.Text.Json.JsonDocument.Parse(json).RootElement;
    }

    private Task FirstNameAsync(int playerId, string firstName) =>
        _factory.WithServicesAsync(services =>
            services.GetRequiredService<IPlayerWelcomeService>().SaveAsync(
                playerId, firstName, null, null, removePhoto: false, StartCompassFactory.AdminUserId));

    private Task SeedAsync(
        string raterUserId,
        int value,
        int cyclesAgo = 0,
        string position = "RB",
        string? second = null,
        string? third = null,
        int? playerId = null) =>
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

            await planning.SaveAsync(playerId ?? _factory.PlayerId, raterUserId, cycle, assessment);
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
