using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Boots the real application over HTTP for a test.
///
/// The tests around it are the ones that cannot be written against a service directly: what
/// a request actually returns, for a given signed-in user, through the whole pipeline --
/// routing, the authorisation policies, the controller and the rendered view. Everything
/// else in this project is better tested one class at a time; see <see cref="TestDatabase"/>.
///
/// Two substitutions, and no others. The authorisation handlers, the policies, the services
/// and the views are the ones that ship:
///
///   * <see cref="SqliteAppDbContext"/> instead of Supabase — the same SQLite setup
///     <see cref="TestDatabase"/> uses, so unique indexes and foreign keys are real here too,
///     and DateTimeOffset can be ordered.
///   * <see cref="TestAuthHandler"/> instead of the sign-in cookie, so a test can ask what a
///     guardian sees without handling a password.
///
/// The environment is "Testing", which keeps SeedData out of the way — it only runs in
/// Development. The fixture below is seeded instead, and it is small on purpose.
/// </summary>
public sealed class StartCompassFactory : WebApplicationFactory<Program>
{
    // Identity user ids. Plain strings rather than GUIDs so a failing test names somebody.
    public const string PlayerUserId = "user-player";
    public const string OtherPlayerUserId = "user-other-player";
    public const string GuardianUserId = "user-guardian";
    public const string CoachUserId = "user-coach";
    public const string AdminUserId = "user-admin";

    /// <summary>The player who is PlayerUserId, and who the guardian is registered on.</summary>
    public int PlayerId { get; private set; }

    /// <summary>A second player, with no link to the guardian. The negative case.</summary>
    public int OtherPlayerId { get; private set; }

    public int TeamId { get; private set; }

    public int RoundId { get; private set; }

    /// <summary>Errors the application logged during this factory's lifetime.</summary>
    public List<string> Errors { get; } = new();

    private SqliteConnection? _connection;

    /// <summary>
    /// Set as environment variables, not through ConfigureAppConfiguration.
    ///
    /// Program.cs reads its configuration during WebApplication.CreateBuilder, which has
    /// already finished by the time the test host's ConfigureAppConfiguration callbacks run.
    /// Environment variables are among the default sources and are read first, so this is
    /// what actually reaches the code under test.
    ///
    /// The connection string is never used to connect: it exists only to satisfy the startup
    /// guard, and the DbContext is replaced before anything opens it. Leaving the guard in
    /// force during tests is deliberate — it is a real safety net, and the test host should
    /// not be the thing that quietly disables it.
    /// </summary>
    static StartCompassFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // An error inside the application comes back as a bare 500, which makes a failing
        // test say only "expected OK, got InternalServerError". This keeps the exception so
        // AssertOkAsync can name the real cause.
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Errors)));

        builder.ConfigureServices(services =>
        {
            ReplaceDatabaseWithSqlite(services);
            ReplaceAuthenticationWithTestScheme(services);
        });
    }

    /// <summary>
    /// Fails with the application's own error text when a request did not succeed.
    /// A test that says "expected OK, got 500" costs an afternoon; one that says
    /// "expected OK, got 500: SQLite Error 1: no such table" costs a minute.
    /// </summary>
    public async Task AssertOkAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = Errors.Count > 0
            ? string.Join(Environment.NewLine, Errors)
            : await response.Content.ReadAsStringAsync();

        Assert.Fail($"Expected a successful response, got {(int)response.StatusCode}.{Environment.NewLine}{detail}");
    }

    private void ReplaceDatabaseWithSqlite(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<AppDbContext>();

        // Held open for the lifetime of the factory: an in-memory SQLite database disappears
        // with its last connection, schema and all.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Registered by hand rather than with AddDbContext<TService, TImplementation>: that
        // overload asks for DbContextOptions<SqliteAppDbContext>, and the subclass takes
        // DbContextOptions<AppDbContext>. Same arrangement as TestDatabase.
        services.AddSingleton(options);
        services.AddScoped<AppDbContext>(_ => new SqliteAppDbContext(options));
    }

    private static void ReplaceAuthenticationWithTestScheme(IServiceCollection services)
    {
        services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

        // Identity has already made its cookie the default. PostConfigure runs last.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestAuthHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        });
    }

    /// <summary>
    /// Creates the schema and the fixture. Call once per factory, before the first request.
    /// </summary>
    public async Task InitialiseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // EnsureCreated rather than Migrate: the migrations are Npgsql-shaped and would not
        // apply to SQLite. What is under test is the model, not the migration history.
        await db.Database.EnsureCreatedAsync();

        var team = new Team { Name = "Test team" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        TeamId = team.Id;

        var player = new Player
        {
            Code = "TS-TEST-01",
            TeamId = team.Id,
            UserId = PlayerUserId,
            BirthDate = new DateOnly(2010, 1, 1),
            Position = "Midfielder"
        };

        var other = new Player
        {
            Code = "TS-TEST-02",
            TeamId = team.Id,
            UserId = OtherPlayerUserId,
            BirthDate = new DateOnly(2010, 6, 1),
            Position = "Striker"
        };

        db.Players.AddRange(player, other);
        await db.SaveChangesAsync();

        PlayerId = player.Id;
        OtherPlayerId = other.Id;

        // The guardian is linked to ONE of the two. That link, not the role, is what
        // CanViewPlayer turns on, and the second player is how a test can prove it.
        db.Guardianships.Add(new Guardianship
        {
            PlayerId = player.Id,
            GuardianUserId = GuardianUserId
        });

        db.CoachTeams.Add(new CoachTeam { CoachUserId = CoachUserId, TeamId = team.Id });

        // Consent is deliberately left at None. A coach has to be able to open a player
        // without it — that is the rule the club asked for, and a fixture that seeded Full
        // consent would pass whether or not the rule still held.
        var now = DateTimeOffset.UtcNow;

        var round = new SurveyRound
        {
            Name = "Test period",
            OpensAt = now.AddDays(-1),
            ClosesAt = now.AddDays(30)
        };

        db.SurveyRounds.Add(round);
        await db.SaveChangesAsync();
        RoundId = round.Id;
    }

    /// <summary>A client signed in as the given user, in the given roles.</summary>
    public HttpClient ClientAs(string userId, params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        }

        return client;
    }

    /// <summary>A client with nobody signed in.</summary>
    public HttpClient AnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Runs something against the application's own services, e.g. to seed answers.</summary>
    public async Task WithServicesAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
