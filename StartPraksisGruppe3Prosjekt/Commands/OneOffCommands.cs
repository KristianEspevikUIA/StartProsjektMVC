using Npgsql;
using StartPraksisGruppe3Prosjekt.Data;

namespace StartPraksisGruppe3Prosjekt.Commands;

/// <summary>
/// Engangskommandoer: ting som gjøres mot databasen fra kommandolinja, og ikke fra en side.
///
///     dotnet run --project StartPraksisGruppe3Prosjekt -- export-players --out &lt;mappe&gt;
///     dotnet run --project StartPraksisGruppe3Prosjekt -- import-players --from &lt;mappe&gt;
///
/// En kommando er det første argumentet som er et av navnene under. Program.cs spør her før
/// oppstartssteget: en kommando verken migrerer eller seeder, gjør det ene den er til for, og
/// avslutter uten at webserveren starter.
/// </summary>
internal static class OneOffCommands
{
    private static readonly string[] Names =
    {
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
            return command.ToLowerInvariant() switch
            {
                PlayerTransfer.ExportCommand =>
                    await PlayerTransfer.ExportAsync(db, app.Environment, Option(args, "--out"), logger),

                PlayerTransfer.ImportCommand =>
                    await PlayerTransfer.ImportAsync(db, app.Environment, Option(args, "--from"), logger),

                _ => throw new InvalidOperationException($"Ukjent kommando: {command}.")
            };
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
                "{Command} er stoppet: databasen har ikke tabellene appen trenger. Kommandoen migrerer " +
                "ikke selv. Kjør migrasjonene først (dotnet ef database update), og prøv igjen.",
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
