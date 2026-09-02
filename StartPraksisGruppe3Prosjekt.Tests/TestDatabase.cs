using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// AppDbContext with one thing changed for SQLite: DateTimeOffset is stored as a binary
/// value rather than as text.
///
/// SQLite has no DateTimeOffset, and EF's default text mapping cannot be ordered in SQL --
/// which every one of the append-only logs does, because "the current state is the newest
/// row" is how they are read. Postgres has timestamptz and needs none of this, so the
/// conversion lives here in the tests instead of in the application's model.
/// </summary>
internal sealed class SqliteAppDbContext : AppDbContext
{
    public SqliteAppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}

/// <summary>
/// A throwaway database for one test.
///
/// SQLite in memory rather than the EF in-memory provider, because the tests here are about
/// things a fake provider does not have: unique indexes, foreign keys, cascade deletes and
/// real SQL translation. Production runs on Postgres, so this is not a full substitute --
/// but a query that cannot be translated at all fails here, which is the failure worth
/// catching before it reaches Supabase.
///
/// The connection is held open for the lifetime of the object: a SQLite in-memory database
/// disappears with its last connection, schema and all.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ServiceProvider _provider;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        // The same connection behind a real container, so anything that resolves an
        // AppDbContext from a scope -- PlayerAccessLog does -- sees these rows.
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<AppDbContext>(_ => NewContext());
        _provider = services.BuildServiceProvider();

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// A context of its own. Tests use one to arrange and another to assert, so a passing
    /// assertion means the row is in the database rather than in a change tracker.
    /// </summary>
    public AppDbContext NewContext() => new SqliteAppDbContext(_options);

    /// <summary>For services that open their own scope. See PlayerAccessLog.</summary>
    public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    /// <summary>A team with one player on it, which is what most of these tests need.</summary>
    public async Task<Player> AddPlayerAsync(
        string code = "TS-08-16",
        string? userId = null,
        string teamName = "Senior")
    {
        await using var context = NewContext();

        var team = await context.Teams.FirstOrDefaultAsync(t => t.Name == teamName);
        if (team is null)
        {
            team = new Team { Name = teamName };
            context.Teams.Add(team);
            await context.SaveChangesAsync();
        }

        var player = new Player
        {
            Code = code,
            UserId = userId,
            TeamId = team.Id,
            BirthDate = new DateOnly(2008, 5, 16)
        };

        context.Players.Add(player);
        await context.SaveChangesAsync();

        return player;
    }

    public async Task<SurveyRound> AddRoundAsync(
        string name,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt)
    {
        await using var context = NewContext();

        var round = new SurveyRound
        {
            Name = name,
            OpensAt = opensAt,
            ClosesAt = closesAt
        };

        context.SurveyRounds.Add(round);
        await context.SaveChangesAsync();

        return round;
    }

    /// <summary>A round that is open right now, for the common case.</summary>
    public Task<SurveyRound> AddOpenRoundAsync(string name = "Autumn 2026") =>
        AddRoundAsync(
            name,
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow.AddDays(7));

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
