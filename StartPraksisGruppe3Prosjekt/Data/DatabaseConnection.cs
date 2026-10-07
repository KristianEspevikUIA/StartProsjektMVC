using Npgsql;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Hvor tilkoblingsstrengen kommer fra, og hva den må oppfylle før appen bruker den.
///
/// Strengen står ikke i appsettings.json. I utvikling peker appsettings.Development.json på en
/// lokal database, uten passord, og passordet ligger for seg i user-secrets
/// (<c>Database:Password</c>) -- en utvikler skal ikke måtte kopiere en hel streng for å sette
/// ett passord. I drift kommer hele strengen fra miljøvariabelen
/// <c>ConnectionStrings__DefaultConnection</c>. Se docs/database.md.
/// </summary>
internal static class DatabaseConnection
{
    /// <summary>
    /// Strengen appen kobler til med: <c>ConnectionStrings:DefaultConnection</c>, med
    /// <c>Database:Password</c> lagt til når strengen selv ikke har et passord. Tom når ingen
    /// streng er satt; <see cref="Problems"/> sier da hva som mangler.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(configured))
        {
            return string.Empty;
        }

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException)
        {
            // En streng som ikke lar seg lese, sendes videre som den er. Problems forklarer
            // den etter builder.Build(), der en feil ikke stopper «dotnet ef».
            return configured;
        }

        if (string.IsNullOrEmpty(builder.Password)
            && configuration["Database:Password"] is { Length: > 0 } password)
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Det som er galt med tilkoblingen, som meldinger til den som skal rette det. Tom liste
    /// når alt er i orden. Kobler ikke til noe.
    /// </summary>
    public static IReadOnlyList<string> Problems(string connectionString, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new[]
            {
                environment.IsDevelopment()
                    ? "Tilkoblingsstrengen mangler. Den skal stå i appsettings.Development.json " +
                      "(ConnectionStrings:DefaultConnection) og peke på den lokale databasen."
                    : "Tilkoblingsstrengen mangler. Utenfor Development står den ikke i noen fil: sett " +
                      "miljøvariabelen ConnectionStrings__DefaultConnection."
            };
        }

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            return new[] { $"Tilkoblingsstrengen kan ikke leses: {ex.Message}" };
        }

        var problems = new List<string>();

        if (string.IsNullOrEmpty(builder.Password))
        {
            problems.Add(environment.IsDevelopment()
                ? "Databasepassordet mangler. Det er en hemmelighet og står ikke i repoet. Legg passordet " +
                  "til den lokale databasen (det samme som i .env) i user-secrets:\n\n" +
                  "    dotnet user-secrets set \"Database:Password\" \"<passordet>\" --project StartPraksisGruppe3Prosjekt\n"
                : "Databasepassordet mangler. ConnectionStrings__DefaultConnection må ha Password=... " +
                  "for app-rollen.");
        }

        // Utenfor utvikling er det ekte spillerdata på linja. «Require» krypterer, men
        // kontrollerer ikke hvem som er i den andre enden; bare VerifyFull gjør det (sertifikat
        // og vertsnavn). Unntaket er en database på samme maskin, der det ikke er noe nett
        // å stå i veien på.
        if (!environment.IsDevelopment() && builder.SslMode != SslMode.VerifyFull)
        {
            var remote = Hosts(builder.Host).Where(host => !IsLocal(host)).ToList();

            if (remote.Count > 0)
            {
                problems.Add(
                    $"Tilkoblingen til {string.Join(", ", remote)} verifiserer ikke serversertifikatet " +
                    $"(SSL Mode={builder.SslMode}). Utenfor Development kreves «SSL Mode=VerifyFull» når " +
                    "databasen ikke er på samme maskin. Er sertifikatet signert av en CA som ikke ligger i " +
                    "maskinens rotlager, legg til «Root Certificate=<sti til CA-fila>».");
            }
        }

        return problems;
    }

    /// <summary>Vertene i <c>Host=</c>: én, eller flere skilt med komma, hver med eller uten port.</summary>
    private static IEnumerable<string> Hosts(string? host)
    {
        foreach (var part in (host ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith('['))
            {
                // [::1]:5432
                var end = part.IndexOf(']');
                yield return end > 0 ? part[1..end] : part;
            }
            else if (part.Count(character => character == ':') == 1)
            {
                // vert:port
                yield return part[..part.IndexOf(':')];
            }
            else
            {
                yield return part;
            }
        }
    }

    /// <summary>
    /// Samme maskin: loopback, eller en mappe med en Unix-socket (PostgreSQL på Linux uten
    /// nettverk i det hele tatt).
    /// </summary>
    private static bool IsLocal(string host) =>
        host.StartsWith('/')
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
}
