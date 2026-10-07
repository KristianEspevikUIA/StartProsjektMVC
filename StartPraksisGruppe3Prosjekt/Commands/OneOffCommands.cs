using Npgsql;
using StartPraksisGruppe3Prosjekt.Data;

namespace StartPraksisGruppe3Prosjekt.Commands;

/// <summary>
/// Engangskommandoer: ting som gjøres mot databasen fra kommandolinja, og ikke fra en side.
///
///     dotnet StartPraksisGruppe3Prosjekt.dll create-admin --email navn@klubben.no
///     dotnet StartPraksisGruppe3Prosjekt.dll import-players --from &lt;mappe&gt;
///     dotnet StartPraksisGruppe3Prosjekt.dll export-players --out &lt;mappe&gt;
///
/// (Fra kildekoden: dotnet run --project StartPraksisGruppe3Prosjekt -- create-admin ...)
///
/// En kommando er det første argumentet som er et av navnene under. Program.cs spør her før
/// oppstartssteget: en kommando verken migrerer eller seeder, gjør det ene den er til for, og
/// avslutter uten at webserveren starter.
///
/// De to som skriver -- create-admin og import-players -- kjører bare mot en database som er
/// tatt i bruk og markert for miljøet de kjøres i (se <see cref="DatabaseGuard"/>). Eksporten
/// leser bare, og kan kjøres mot hvilken som helst database med denne versjonens tabeller.
/// </summary>
internal static class OneOffCommands
{
    private static readonly string[] Names =
    {
        CreateAdmin.Command,
        PlayerTransfer.ExportCommand,
        PlayerTransfer.ImportCommand
    };

    /// <summary>
    /// Kjører kommandoen i <paramref name="args"/>, om det står en der. Returnerer exit-koden,
    /// eller null når det ikke er noen kommando og appen skal starte som vanlig.
    /// </summary>
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        var command = args.FirstOrDefault(arg => Names.Contains(arg, StringComparer.OrdinalIgnoreCase));

        if (command is null)
        {
            return null;
        }

        using var scope = app.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = app.Logger;

        try
        {
            switch (command.ToLowerInvariant())
            {
                case CreateAdmin.Command:
                    return await CreateAdmin.RunAsync(scope.ServiceProvider, app.Environment, Option(args, "--email"), logger);

                case PlayerTransfer.ExportCommand:
                    return await PlayerTransfer.ExportAsync(db, app.Environment, Option(args, "--out"), logger);

                case PlayerTransfer.ImportCommand:
                    await DatabaseGuard.RequireMarkedAsAsync(db, DatabaseGuard.KindOf(app.Environment));
                    return await PlayerTransfer.ImportAsync(db, app.Environment, Option(args, "--from"), logger);

                default:
                    throw new InvalidOperationException($"Ukjent kommando: {command}.");
            }
        }
        catch (InvalidOperationException ex)
        {
            // Kommandoenes egne avslag er InvalidOperationException, og der er meldingen hele
            // forklaringen. Stacktracen ligger på Debug for den som trenger den.
            logger.LogCritical("{Command} er stoppet. {Reason}", command, ex.Message);
            logger.LogDebug(ex, "{Command} stoppet her.", command);

            return 1;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogCritical(
                "{Command} er stoppet: databasen har ikke tabellene denne versjonen av appen bruker. " +
                "Kommandoen migrerer ikke selv. Er dette en ny database, kjør migrasjonene først (se " +
                "docs/database.md). Er det den gamle Supabase-databasen, har den en annen struktur: " +
                "export-players mot den kjøres med versjonen av appen fra før den nye databasen.",
                command);

            return 1;
        }
    }

    /// <summary>Verdien til <c>--navn verdi</c> eller <c>--navn=verdi</c>. Null når den ikke er oppgitt.</summary>
    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length ? args[index + 1] : null;
            }

            if (args[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[index][(name.Length + 1)..];
            }
        }

        return null;
    }
}
