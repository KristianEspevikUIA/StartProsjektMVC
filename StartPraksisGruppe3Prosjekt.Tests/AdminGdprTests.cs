using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The two GDPR duties in AdminController, through the whole pipeline: handing out
/// everything held about one player, and erasing it.
///
/// These are the operations with the least room to be wrong. An export that quietly leaves a
/// table out is a data subject access request that was not answered; a deletion that misses a
/// table is data the club promised to erase and did not. Neither failure is visible from the
/// screen, which is exactly why they are asserted here.
///
/// The deletion tests read the database directly afterwards rather than trusting the redirect.
/// The cascade is declared in AppDbContext and carried out by the database, so the only honest
/// way to check it is to look.
/// </summary>
public sealed class AdminGdprTests : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_export_holds_every_table_that_is_about_the_player()
    {
        await SeedOneOfEverythingAsync();

        var json = await ExportAsync();

        // The seven sections the README promises. A missing one is an unanswered request.
        Assert.Contains("\"Player\"", json);
        Assert.Contains("\"Guardianships\"", json);
        Assert.Contains("\"Responses\"", json);
        Assert.Contains("\"FiveCSubmissions\"", json);
        Assert.Contains("\"ConsentEvents\"", json);
        Assert.Contains("\"AccessEvents\"", json);
        Assert.Contains("\"FeedbackReleases\"", json);

        // Present is not the same as populated: an empty array would satisfy every assertion
        // above. These are the contents.
        Assert.Contains("TS-TEST-01", json);                 // the player
        Assert.Contains("\"Answers\"", json);                // inside Responses and submissions
        Assert.Contains("\"QuestionKey\": \"commitment-1\"", json);
        Assert.Contains("\"Context\": \"Coach/FiveCPlayer\"", json);

        // The gap is never stored, so it cannot be exported. Said out loud because "it is
        // missing" and "it does not exist" look identical in a JSON file.
        Assert.DoesNotContain("\"Gap\"", json);
        Assert.DoesNotContain("\"Difference\"", json);
    }

    [Fact]
    public async Task Exporting_is_written_to_the_audit_log()
    {
        // An admin reading a minor's complete file is the clearest example of the lookup the
        // audit log exists for. If this stops being written, nothing records that it happened.
        var response = await _factory
            .ClientAs(StartCompassFactory.AdminUserId, Roles.Admin)
            .GetAsync($"/Admin/Export/{_factory.PlayerId}");

        await _factory.AssertOkAsync(response);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var entry = await db.PlayerAccessEvents
                .AsNoTracking()
                .Where(a => a.PlayerId == _factory.PlayerId)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            Assert.NotNull(entry);
            Assert.Equal(StartCompassFactory.AdminUserId, entry!.ViewedByUserId);
            Assert.Equal(Roles.Admin, entry.ViewedByRole);
            Assert.Equal("Admin/Export", entry.Context);
        });
    }

    [Fact]
    public async Task The_export_names_other_people_by_role_and_not_by_identity_id()
    {
        await SeedOneOfEverythingAsync();

        var json = await ExportAsync();

        // The document is about one player. The guardians, coaches and admins around them are
        // other data subjects, and their Identity ids are keys into the user table.
        Assert.DoesNotContain(StartCompassFactory.GuardianUserId, json);
        Assert.DoesNotContain(StartCompassFactory.CoachUserId, json);

        // Replaced rather than dropped: "Coach 1 opened you five times" is part of the answer,
        // and blanking the field would flatten five lookups into five strangers.
        Assert.Contains("Guardian 1", json);
        Assert.Contains("Coach 1", json);

        // The player's own id stays. It is their own data, and this is their own file.
        Assert.Contains(StartCompassFactory.PlayerUserId, json);
        Assert.Contains("The player themselves", json);
    }

    [Fact]
    public async Task Only_an_admin_can_export()
    {
        await AssertRefusedForEveryoneElseAsync($"/Admin/Export/{_factory.PlayerId}");
    }

    // -----------------------------------------------------------------------------------
    // Delete
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_takes_the_answers_the_consent_log_the_guardians_and_the_audit_rows()
    {
        var seeded = await SeedOneOfEverythingAsync();
        var playerId = _factory.PlayerId;

        // Every assertion below would also hold against an empty database. This is what makes
        // the ones after the deletion mean something.
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.True(await db.Responses.AnyAsync(r => r.PlayerId == playerId));
            Assert.True(await db.Answers.AnyAsync(a => a.ResponseId == seeded.ResponseId));
            Assert.True(await db.FiveCSubmissions.AnyAsync(s => s.PlayerId == playerId));
            Assert.True(await db.ConsentEvents.AnyAsync(c => c.PlayerId == playerId));
            Assert.True(await db.Guardianships.AnyAsync(g => g.PlayerId == playerId));
            Assert.True(await db.PlayerAccessEvents.AnyAsync(a => a.PlayerId == playerId));
            Assert.True(await db.FeedbackReleases.AnyAsync(f => f.PlayerId == playerId));
        });

        var response = await DeleteAsync("TS-TEST-01");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.False(await db.Players.AnyAsync(p => p.Id == playerId));
            Assert.False(await db.Responses.AnyAsync(r => r.PlayerId == playerId));
            Assert.False(await db.Answers.AnyAsync(a => a.ResponseId == seeded.ResponseId));
            Assert.False(await db.FiveCSubmissions.AnyAsync(s => s.PlayerId == playerId));
            Assert.False(await db.ConsentEvents.AnyAsync(c => c.PlayerId == playerId));
            Assert.False(await db.Guardianships.AnyAsync(g => g.PlayerId == playerId));
            Assert.False(await db.PlayerAccessEvents.AnyAsync(a => a.PlayerId == playerId));
            Assert.False(await db.FeedbackReleases.AnyAsync(f => f.PlayerId == playerId));

            // The other player is the control. A cascade that took the whole table would
            // satisfy every assertion above.
            Assert.True(await db.Players.AnyAsync(p => p.Id == _factory.OtherPlayerId));
        });
    }

    [Fact]
    public async Task Deleting_removes_the_sign_in_account()
    {
        // The fixture has no Identity rows -- it signs requests in through TestAuthHandler.
        // The account has to exist for this test to be about anything.
        await _factory.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();

            // No password: the test scheme never asks for one, and a literal here would be a
            // password in the repository for no gain.
            var created = await users.CreateAsync(new IdentityUser
            {
                Id = StartCompassFactory.PlayerUserId,
                UserName = "player@example.test",
                Email = "player@example.test"
            });

            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        });

        var response = await DeleteAsync("TS-TEST-01");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await _factory.WithServicesAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();

            // The Identity account sits outside the player's foreign keys, so no cascade
            // reaches it. It is deleted by hand, and this is the assertion that says so.
            Assert.Null(await users.FindByIdAsync(StartCompassFactory.PlayerUserId));
        });
    }

    [Fact]
    public async Task A_deletion_leaves_a_trace_that_outlives_the_player()
    {
        var playerId = _factory.PlayerId;

        await DeleteAsync("TS-TEST-01");

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var trace = await db.PlayerDeletionEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.PlayerId == playerId);

            // The point of the separate table. Every audit row that pointed at this player
            // has just cascaded away; without this one, nothing would record that anybody
            // deleted anything.
            Assert.NotNull(trace);
            Assert.Equal(StartCompassFactory.AdminUserId, trace!.DeletedByUserId);
            Assert.NotEqual(default, trace.OccurredAt);
        });
    }

    [Fact]
    public async Task Deleting_without_typing_the_player_code_does_nothing()
    {
        var playerId = _factory.PlayerId;

        var response = await DeleteAsync("not-the-code");

        // Back to the form rather than on to the redirect, and the player is still there.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Type the player code", await response.Content.ReadAsStringAsync());

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.True(await db.Players.AnyAsync(p => p.Id == playerId));
            Assert.False(await db.PlayerDeletionEvents.AnyAsync(d => d.PlayerId == playerId));
        });
    }

    [Fact]
    public async Task Only_an_admin_can_open_the_delete_page()
    {
        await AssertRefusedForEveryoneElseAsync($"/Admin/Delete/{_factory.PlayerId}");
    }

    [Fact]
    public async Task Only_an_admin_can_run_the_deletion()
    {
        var playerId = _factory.PlayerId;

        // Nobody below can reach the confirmation page to be issued a token, so these post
        // without one. Authorisation runs in middleware, ahead of the antiforgery filter, so
        // what comes back is the refusal and not a 400 about the token.
        foreach (var (client, expected) in RefusedClients())
        {
            var response = await client.PostAsync(
                $"/Admin/Delete/{playerId}",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("confirmCode", "TS-TEST-01")
                }));

            Assert.Equal(expected, response.StatusCode);
        }

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            Assert.True(await db.Players.AnyAsync(p => p.Id == playerId));
        });
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private HttpClient AdminClient() =>
        _factory.ClientAs(StartCompassFactory.AdminUserId, Roles.Admin);

    private async Task<string> ExportAsync()
    {
        var response = await AdminClient().GetAsync($"/Admin/Export/{_factory.PlayerId}");

        await _factory.AssertOkAsync(response);

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Walks the confirmation page the way an admin does: open it, copy the code, post it
    /// back. The antiforgery token only exists on that page, so this also proves the form
    /// renders one.
    /// </summary>
    private async Task<HttpResponseMessage> DeleteAsync(string confirmCode)
    {
        var client = AdminClient();

        var page = await client.GetAsync($"/Admin/Delete/{_factory.PlayerId}");
        await _factory.AssertOkAsync(page);

        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        return await client.PostAsync(
            $"/Admin/Delete/{_factory.PlayerId}",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("confirmCode", confirmCode)
            }));
    }

    private static string AntiforgeryToken(string html)
    {
        const string field = "name=\"__RequestVerificationToken\"";
        const string value = "value=\"";

        var atField = html.IndexOf(field, StringComparison.Ordinal);
        Assert.True(atField >= 0, "The delete form rendered no antiforgery token.");

        var start = html.IndexOf(value, atField, StringComparison.Ordinal) + value.Length;
        var end = html.IndexOf('"', start);

        return html[start..end];
    }

    /// <summary>The three ways of not being an admin, with what each should get back.</summary>
    private (HttpClient Client, HttpStatusCode Expected)[] RefusedClients() =>
    [
        (_factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach), HttpStatusCode.Forbidden),
        (_factory.ClientAs(StartCompassFactory.PlayerUserId, Roles.Player), HttpStatusCode.Forbidden),
        (_factory.AnonymousClient(), HttpStatusCode.Unauthorized)
    ];

    private async Task AssertRefusedForEveryoneElseAsync(string url)
    {
        foreach (var (client, expected) in RefusedClients())
        {
            var response = await client.GetAsync(url);

            Assert.Equal(expected, response.StatusCode);
        }
    }

    /// <summary>
    /// One row in every table the export and the deletion touch. Small on purpose: what is
    /// being checked is that each table is reached at all, not how much it holds.
    /// </summary>
    private async Task<(int ResponseId, int ItemId)> SeedOneOfEverythingAsync()
    {
        var responseId = 0;
        var itemId = 0;

        // A 5C submission from the player, and one from the coach, through the real store.
        await _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            await store.SaveAsync(Submissions.Filled(
                catalog, _factory.RoundId, _factory.PlayerId, "TS-TEST-01",
                RespondentType.Player, StartCompassFactory.PlayerUserId, value: 4));

            await store.SaveAsync(Submissions.Filled(
                catalog, _factory.RoundId, _factory.PlayerId, "TS-TEST-01",
                RespondentType.Coach, StartCompassFactory.CoachUserId, value: 2));
        });

        // Consent, recorded through the service so the append-only rule is the real one.
        await _factory.WithServicesAsync(async services =>
        {
            var consent = services.GetRequiredService<IConsentService>();

            await consent.RecordAsync(
                _factory.PlayerId, ConsentLevel.Full, StartCompassFactory.GuardianUserId);
        });

        // A release from the coach.
        await _factory.WithServicesAsync(async services =>
        {
            var releases = services.GetRequiredService<IFeedbackReleaseService>();

            await releases.ReleaseAsync(
                _factory.RoundId, _factory.PlayerId, StartCompassFactory.CoachUserId);
        });

        // The older 25-statement Response/Answer pair, which has no service in front of it.
        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var item = new Item
            {
                Number = 1,
                Text = "I know what is expected of me.",
                Construct = "Role clarity"
            };

            db.Items.Add(item);
            await db.SaveChangesAsync();

            var answered = new Response
            {
                RoundId = _factory.RoundId,
                PlayerId = _factory.PlayerId,
                Respondent = RespondentType.Guardian,
                RespondentUserId = StartCompassFactory.GuardianUserId,
                SubmittedAt = DateTimeOffset.UtcNow,
                Answers = { new Answer { ItemId = item.Id, Value = 4 } }
            };

            db.Responses.Add(answered);
            await db.SaveChangesAsync();

            responseId = answered.Id;
            itemId = item.Id;
        });

        // An audit row, written by a coach actually opening the player rather than by hand.
        var visit = await _factory
            .ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCPlayer/{_factory.PlayerId}");

        await _factory.AssertOkAsync(visit);

        return (responseId, itemId);
    }
}
