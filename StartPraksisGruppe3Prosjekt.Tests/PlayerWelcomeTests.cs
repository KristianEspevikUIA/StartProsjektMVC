using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The welcome: a player the club has entered a first name and photo for is greeted with them
/// on the front page -- and nobody else ever sees them.
///
/// The name and photo are the one place the system holds a player's name and face, most of
/// them minors, so most of these tests are about where they must NOT turn up: another player,
/// a guardian, a coach page, anybody who is not signed in.
/// </summary>
public sealed class PlayerWelcomeTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Player() => _factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player);

    private HttpClient Admin() => _factory.ClientAs(StartCompassFactory.AdminUserId, Roles.Admin);

    // -----------------------------------------------------------------------------------
    // The welcome
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_player_is_welcomed_by_name_with_their_photo()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        var html = await Player().GetStringAsync("/");

        Assert.Contains("<h1>Welcome, Alex</h1>", html);
        Assert.Contains("class=\"sc-welcome__photo\"", html);
        Assert.Contains("src=\"/Player/Photo?v=", html);
    }

    [Fact]
    public async Task A_player_with_nothing_entered_sees_the_front_page_as_before()
    {
        var html = await Player().GetStringAsync("/");

        Assert.Contains("<h1>StartCompass</h1>", html);
        Assert.DoesNotContain("Welcome,", html);
        Assert.DoesNotContain("/Player/Photo", html);
    }

    [Fact]
    public async Task A_name_without_a_photo_is_still_a_welcome()
    {
        await SeedAsync("Alex", photo: null);

        var html = await Player().GetStringAsync("/");

        Assert.Contains("<h1>Welcome, Alex</h1>", html);
        Assert.DoesNotContain("sc-welcome__photo", html);
    }

    [Fact]
    public async Task Nobody_else_is_welcomed_with_the_players_name()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        foreach (var client in new[]
                 {
                     _factory.ClientAs(StartCompassFactory.OtherPlayerUserId, Roles.Player),
                     _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian),
                     _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach),
                     Admin(),
                     _factory.AnonymousClient()
                 })
        {
            Assert.DoesNotContain("Alex", await client.GetStringAsync("/"));
        }
    }

    [Fact]
    public async Task The_name_stays_off_the_coach_pages()
    {
        // Everywhere else a player is their code. A coach page that picked up the name would
        // undo that without anybody deciding to.
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        var coach = _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach);

        foreach (var url in new[] { $"/Coach/FiveCTeam/{_factory.TeamId}", $"/Coach/FiveCPlayer/{_factory.PlayerId}", "/Succession" })
        {
            var html = await coach.GetStringAsync(url);

            Assert.DoesNotContain("Alex", html);
            Assert.DoesNotContain("/Player/Photo", html);
        }
    }

    // -----------------------------------------------------------------------------------
    // The photo
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_photo_address_serves_the_signed_in_players_own_photo_privately()
    {
        var png = PlayerPhotoRulesTests.Png();
        await SeedAsync("Alex", png);

        var response = await Player().GetAsync("/Player/Photo");

        await _factory.AssertOkAsync(response);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());

        // Private: never kept in a cache between the server and the phone.
        Assert.True(response.Headers.CacheControl?.Private);
    }

    [Fact]
    public async Task There_is_no_address_for_somebody_elses_photo()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        // The other player asks for "their" photo and has none: the address takes no id, so there
        // is nothing to change to reach the first player's.
        var other = await _factory.ClientAs(StartCompassFactory.OtherPlayerUserId, Roles.Player).GetAsync("/Player/Photo");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        var withId = await _factory.ClientAs(StartCompassFactory.OtherPlayerUserId, Roles.Player)
            .GetAsync($"/Player/Photo/{_factory.PlayerId}");
        Assert.NotEqual(HttpStatusCode.OK, withId.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian).GetAsync("/Player/Photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach).GetAsync("/Player/Photo")).StatusCode);

        var anonymous = await _factory.AnonymousClient().GetAsync("/Player/Photo");
        Assert.True(anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    // -----------------------------------------------------------------------------------
    // Entering it
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Only_an_administrator_can_enter_names_and_photos()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        foreach (var client in new[]
                 {
                     _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach),
                     Player(),
                     _factory.ClientAs(StartCompassFactory.GuardianUserId, Roles.Guardian)
                 })
        {
            foreach (var url in new[] { "/Admin/Players", $"/Admin/PlayerDetails/{_factory.PlayerId}", $"/Admin/PlayerPhoto/{_factory.PlayerId}" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
            }
        }

        await _factory.AssertOkAsync(await Admin().GetAsync("/Admin/Players"));
    }

    [Fact]
    public async Task An_uploaded_photo_is_stored_without_its_metadata()
    {
        var response = await PostDetailsAsync("  Alex  ", PlayerPhotoRulesTests.Jpeg(withMetadata: true), "photo.jpg");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var saved = await db.PlayerPersonalDetails.SingleAsync(d => d.PlayerId == _factory.PlayerId);

            Assert.Equal("Alex", saved.FirstName);
            Assert.Equal("image/jpeg", saved.PhotoContentType);
            Assert.Equal("ikstart.no, squad photos", saved.PhotoSource);
            Assert.DoesNotContain("GPSLatitude", Encoding.ASCII.GetString(saved.Photo!));
            Assert.Equal(StartCompassFactory.AdminUserId, saved.UpdatedByUserId);
        });
    }

    [Fact]
    public async Task A_file_that_is_not_a_photo_is_refused_and_nothing_is_saved()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var response = await PostDetailsAsync("Alex", svg, "photo.jpg", contentType: "image/jpeg");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Use a JPEG, PNG or WebP photo.", html);

        // Not the name either: half a save would leave the admin thinking the welcome is ready.
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.False(await db.PlayerPersonalDetails.AnyAsync());
        });
    }

    [Fact]
    public async Task A_photo_can_be_removed_and_the_name_kept()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        var response = await PostDetailsAsync("Alex", photo: null, fileName: null, removePhoto: true);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var saved = await db.PlayerPersonalDetails.SingleAsync();

            Assert.Null(saved.Photo);
            Assert.Null(saved.PhotoContentType);
            Assert.Equal("Alex", saved.FirstName);
        });

        Assert.Equal(HttpStatusCode.NotFound, (await Player().GetAsync("/Player/Photo")).StatusCode);
    }

    [Fact]
    public async Task Opening_a_players_name_and_photo_is_written_to_the_audit_log()
    {
        await _factory.AssertOkAsync(await Admin().GetAsync($"/Admin/PlayerDetails/{_factory.PlayerId}"));

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.True(await db.PlayerAccessEvents.AnyAsync(a =>
                a.PlayerId == _factory.PlayerId
                && a.ViewedByUserId == StartCompassFactory.AdminUserId
                && a.Context == "Admin/PlayerDetails"));
        });
    }

    [Fact]
    public async Task The_export_holds_the_name_and_the_photo_and_the_deletion_takes_them()
    {
        await SeedAsync("Alex", PlayerPhotoRulesTests.Png());

        var json = await Admin().GetStringAsync($"/Admin/Export/{_factory.PlayerId}");

        Assert.Contains("\"PersonalDetails\"", json);
        Assert.Contains("\"FirstName\": \"Alex\"", json);
        Assert.Contains(Convert.ToBase64String(PlayerPhotoRulesTests.Png()), json);
        Assert.DoesNotContain(StartCompassFactory.AdminUserId, json);

        var client = Admin();
        var page = await client.GetStringAsync($"/Admin/Delete/{_factory.PlayerId}");
        Assert.Contains("First name and photo", page);

        var deleted = await client.PostAsync($"/Admin/Delete/{_factory.PlayerId}", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", AntiforgeryToken(page)),
            new KeyValuePair<string, string>("confirmCode", "TS-TEST-01")
        }));

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.False(await db.PlayerPersonalDetails.AnyAsync());
        });
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private Task SeedAsync(string? firstName, byte[]? photo) =>
        _factory.WithServicesAsync(async services =>
        {
            var welcome = services.GetRequiredService<IPlayerWelcomeService>();

            await welcome.SaveAsync(
                _factory.PlayerId,
                firstName,
                "Test fixture",
                photo is null ? null : PlayerPhotoRules.Prepare(photo),
                removePhoto: false,
                StartCompassFactory.AdminUserId);
        });

    /// <summary>Opens the admin form for its token, then posts it back as multipart, the way the browser does.</summary>
    private async Task<HttpResponseMessage> PostDetailsAsync(
        string firstName,
        byte[]? photo,
        string? fileName,
        string contentType = "image/jpeg",
        bool removePhoto = false)
    {
        var client = Admin();
        var url = $"/Admin/PlayerDetails/{_factory.PlayerId}";

        var page = await client.GetAsync(url);
        await _factory.AssertOkAsync(page);

        var form = new MultipartFormDataContent
        {
            { new StringContent(AntiforgeryToken(await page.Content.ReadAsStringAsync())), "__RequestVerificationToken" },
            { new StringContent(firstName), "FirstName" },
            { new StringContent("ikstart.no, squad photos"), "PhotoSource" },
            { new StringContent(removePhoto ? "true" : "false"), "RemovePhoto" }
        };

        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "photo", fileName ?? "photo");
        }

        return await client.PostAsync(url, form);
    }

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
