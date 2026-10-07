using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Vernet mellom utvikling og drift: appen starter bare mot en database som hører til miljøet
/// den kjører i.
///
/// Hva som skal hindres, er konkret. Development migrerer ved oppstart og legger inn
/// demokontoer med et passord som står i kildekoden; det skal aldri skje i databasen med de
/// ekte spillerne. Production skal aldri kjøre mot en utviklingsbase, der de samme kontoene
/// allerede ligger.
///
/// Databasen sier selv hva den er, i <see cref="DatabaseMarker"/>:
///
///   * En TOM database markeres av den første appen som starter mot den, med miljøet den
///     appen kjører i.
///   * En MARKERT database slipper bare inn sitt eget miljø.
///   * En UMARKERT database som har data, slipper ikke inn noen. Det er enten en database
///     fra før denne ordningen (den gamle Supabase-databasen), eller en markering noen har
///     fjernet -- og i begge tilfeller er det et menneske som må si hva den er.
///
/// «Development» er miljøet Development. Alt annet -- Production, Staging -- er drift.
///
/// Sjekken har to steg fordi det første må skje FØR noe migreres: en utviklingsmaskin som
/// peker på driftsdatabasen, skal stoppes før den har endret skjemaet der.
/// </summary>
internal static class DatabaseGuard
{
    /// <summary>Hva en database som hører til dette miljøet, er markert som.</summary>
    public static string KindOf(IHostEnvironment environment) =>
        environment.IsDevelopment() ? DatabaseMarker.Development : DatabaseMarker.Production;

    /// <summary>
    /// Steg 1, før migrering og før noe skrives. Stopper en database som er markert for det
    /// andre miljøet, og en som har tabeller uten å være markert.
    /// </summary>
    public static async Task EnsureBelongsHereAsync(AppDbContext db, IHostEnvironment environment)
    {
        var expected = KindOf(environment);

        if (await MarkerTableExistsAsync(db))
        {
            if (await ReadAsync(db) is { } marker)
            {
                EnsureMatches(marker, expected, environment);
            }

            // Tabellen finnes, men er tom: skjemaet er lagt inn og appen har ikke startet mot
            // det ennå. MarkIfNewAsync tar det derfra.
            return;
        }

        var tables = await CountTablesAsync(db);

        if (tables > 0)
        {
            throw new DatabaseGuardException(
                $"Databasen har {tables} tabeller, men ingen markering av om den er til utvikling eller " +
                "drift. Den er altså ikke laget av denne versjonen av appen -- det kan være den gamle " +
                "Supabase-databasen, eller en database som brukes til noe annet. Appen rører den ikke. " +
                "Sjekk hvor tilkoblingsstrengen peker (user-secrets, miljøvariabler). Skal du begynne " +
                "på nytt i utvikling, lag en ny, tom database. Se docs/database.md.");
        }
    }

    /// <summary>
    /// Mellom de to stegene, utenfor utvikling: skjemaet skal være det denne versjonen av appen
    /// er laget for. Appen migrerer ikke selv i drift -- rollen den kobler til med, får ikke
    /// endre skjemaet -- så en migrasjon som mangler, er noe databaseeieren må kjøre.
    /// </summary>
    public static async Task EnsureMigratedAsync(AppDbContext db, ILogger logger)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count > 0)
        {
            throw new DatabaseGuardException(
                $"Databasen mangler {pending.Count} migrasjon(er) som denne versjonen av appen trenger " +
                $"({string.Join(", ", pending)}). Utenfor Development endrer ikke appen skjemaet selv: " +
                "databaseeieren kjører migrasjonsskriptet fra samme versjon som appen, og så startes appen. " +
                "Se «Kjøre migrasjonene» i docs/database.md.");
        }

        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var unknown = (await db.Database.GetAppliedMigrationsAsync()).Where(id => !known.Contains(id)).ToList();

        if (unknown.Count > 0)
        {
            logger.LogWarning(
                "Databasen har {Count} migrasjon(er) denne versjonen av appen ikke kjenner ({Migrations}). " +
                "Skjemaet er nyere enn appen: sjekk at det er riktig versjon som er startet.",
                unknown.Count,
                string.Join(", ", unknown));
        }
    }

    /// <summary>
    /// Steg 2, når skjemaet er på plass. Markerer en tom database med miljøet appen kjører i.
    /// True når markeringen ble skrevet nå.
    /// </summary>
    public static async Task<bool> MarkIfNewAsync(AppDbContext db, IHostEnvironment environment, ILogger logger)
    {
        var expected = KindOf(environment);

        if (await ReadAsync(db) is { } existing)
        {
            EnsureMatches(existing, expected, environment);
            return false;
        }

        if (await FirstTableWithRowsAsync(db) is { } table)
        {
            throw new DatabaseGuardException(
                $"Databasen har data (tabellen {table} har rader), men ingen markering av om den er til " +
                "utvikling eller drift. Appen markerer bare en tom database selv, for den kan ikke vite " +
                "hva slags data dette er. Er du sikker på hva databasen er, markerer databaseeieren den " +
                $"for hånd: insert into database_marker (id, environment, marked_at) values " +
                $"({DatabaseMarker.SingleRowId}, '{DatabaseMarker.Development}' eller '{DatabaseMarker.Production}', now()). " +
                "Se docs/database.md.");
        }

        db.DatabaseMarker.Add(new DatabaseMarker
        {
            Environment = expected,
            MarkedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // En annen instans av appen startet samtidig og kom først. Da gjelder dens
            // markering, og den sjekkes som enhver annen.
            db.ChangeTracker.Clear();

            var winner = await ReadAsync(db)
                ?? throw new DatabaseGuardException("Databasen kunne ikke markeres, og har heller ingen markering.");

            EnsureMatches(winner, expected, environment);
            return false;
        }

        logger.LogWarning(
            "Databasen var tom og er nå markert som {Kind}. Fra nå av starter appen bare mot den i {Environments}.",
            Describe(expected),
            expected == DatabaseMarker.Development ? "Development" : "andre miljøer enn Development");

        return true;
    }

    /// <summary>
    /// For kode som bare skal kjøre mot én slags database, uansett hvem som kaller: seedingen
    /// av demodata, og engangskommandoene som skriver. Kaster når databasen er umarkert eller
    /// markert som noe annet.
    /// </summary>
    public static async Task RequireMarkedAsAsync(AppDbContext db, string expected)
    {
        var marker = await MarkerTableExistsAsync(db) ? await ReadAsync(db) : null;

        if (marker is null)
        {
            throw new DatabaseGuardException(
                "Databasen er ikke tatt i bruk ennå: den har ingen markering av om den er til utvikling " +
                "eller drift. Start appen én gang mot den først. Da markeres den, og roller og lag legges inn.");
        }

        if (marker.Environment != expected)
        {
            throw new DatabaseGuardException(
                $"Databasen er markert som {Describe(marker.Environment)}, og dette skal bare kjøres mot en " +
                $"database for {Describe(expected)}. Sjekk hvor tilkoblingsstrengen peker.");
        }
    }

    private static void EnsureMatches(DatabaseMarker marker, string expected, IHostEnvironment environment)
    {
        if (marker.Environment == expected)
        {
            return;
        }

        var since = marker.MarkedAt.ToString("d. MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("nb-NO"));

        throw new DatabaseGuardException(expected == DatabaseMarker.Development
            ? $"Denne databasen er markert som DRIFT ({since}), og appen kjører i {environment.EnvironmentName}. " +
              "Development migrerer ved oppstart og legger inn demokontoer med et passord som står i " +
              "kildekoden. Ingen av delene skal skje i databasen med ekte spillerdata, så appen starter " +
              "ikke. Sjekk hvor tilkoblingsstrengen peker (user-secrets, miljøvariabler): utvikling skal " +
              "gå mot din egen lokale database. Se docs/database.md."
            : $"Denne databasen er markert som UTVIKLING ({since}), og appen kjører i {environment.EnvironmentName}. " +
              "En utviklingsbase har oppdiktede data og demokontoer med et passord som står i kildekoden, " +
              "og skal aldri stå bak appen i drift. Appen starter ikke. Sjekk " +
              "ConnectionStrings__DefaultConnection i miljøet. Se docs/database.md.");
    }

    private static string Describe(string kind) => kind == DatabaseMarker.Development ? "utvikling" : "drift";

    private static Task<DatabaseMarker?> ReadAsync(AppDbContext db) =>
        db.DatabaseMarker.AsNoTracking().SingleOrDefaultAsync();

    private static async Task<bool> MarkerTableExistsAsync(AppDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(DatabaseMarker))!;
        var name = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());

        // to_regclass gir null i stedet for en feil når tabellen ikke finnes, og følger
        // search_path på samme måte som spørringene appen selv sender.
        return await ScalarAsync<bool>(db, "select to_regclass(@name) is not null", ("name", name));
    }

    /// <summary>
    /// Tabeller i skjemaet appen bruker, utenom EFs egen historikktabell. Andre skjemaer telles
    /// ikke: en forvaltet PostgreSQL har gjerne sine egne ved siden av.
    /// </summary>
    private static Task<long> CountTablesAsync(AppDbContext db) =>
        ScalarAsync<long>(
            db,
            "select count(*) from pg_catalog.pg_tables where schemaname = current_schema() and tablename <> @history",
            ("history", HistoryTable));

    /// <summary>
    /// Den første tabellen i modellen som har rader, eller null når alle er tomme. Markeringen
    /// selv er ikke med, og heller ikke EFs historikktabell: den har rader i en database som
    /// nettopp er migrert, og er ikke data.
    /// </summary>
    private static async Task<string?> FirstTableWithRowsAsync(AppDbContext db)
    {
        var helper = db.GetService<ISqlGenerationHelper>();

        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.ClrType != typeof(DatabaseMarker) && entity.GetTableName() is not null)
            .Select(entity => (Name: entity.GetTableName()!, Schema: entity.GetSchema()))
            .Distinct()
            .OrderBy(table => table.Name, StringComparer.Ordinal);

        foreach (var (name, schema) in tables)
        {
            // Navnet kommer fra modellen, ikke utenfra, og er sitert av EF selv.
            if (await ScalarAsync<bool>(db, $"select exists (select 1 from {helper.DelimitIdentifier(name, schema)})"))
            {
                return name;
            }
        }

        return null;
    }

    private const string HistoryTable = "__EFMigrationsHistory";

    private static async Task<T> ScalarAsync<T>(AppDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync();
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}

/// <summary>
/// Vernet har nektet. Meldingen er hele forklaringen, og er skrevet for den som står med en app
/// som ikke starter.
/// </summary>
internal sealed class DatabaseGuardException : InvalidOperationException
{
    public DatabaseGuardException(string message) : base(message)
    {
    }
}
