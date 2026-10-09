using Npgsql;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Tilkoblingsstrengen fra konfigurasjonen, gjort klar for Npgsql -- og en forklaring når den
/// ikke kan brukes, i stedet for «Exception while performing SSL handshake» med en
/// DirectoryNotFoundException tre lag ned.
///
/// Det som har stoppet folk:
///   * Plassholderne fra README-en, DITT_BRUKERNAVN og DITT_PASSORD, kopiert inn som de står.
///   * %APPDATA% eller ~ i «Root Certificate». Npgsql utvider dem ikke, og leser stien som en
///     mappe som heter «%APPDATA%». Her utvides de.
///   * En sti til CA-fila som ikke finnes. Ligger fila der README-en ber deg legge den
///     (<see cref="DefaultCertificatePath"/>), brukes den fila, og loggen sier fra.
///   * «max clients reached in session mode»: Supabase-pooleren har 15 klienter for hele
///     prosjektet. Mot den får appen en liten pool (5), med mindre strengen sier noe annet.
///
/// Kalles før builder.Build() og kaster aldri: `dotnet ef` stopper appen der, og skal kunne lage
/// migrasjoner uten en database. Problemet meldes etter Build, i Program.cs.
/// </summary>
internal static class DatabaseConnection
{
    private const string CertificateFileName = "prod-ca-2021.crt";

    /// <summary>
    /// Der README-en ber deg legge CA-fila: %APPDATA%\Supabase\prod-ca-2021.crt på Windows,
    /// ~/.config/Supabase/prod-ca-2021.crt på Mac og Linux.
    /// </summary>
    internal static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Supabase",
        CertificateFileName);

    /// <param name="configured">Strengen fra appsettings.json eller user-secrets.</param>
    /// <param name="problem">Hvorfor strengen ikke kan brukes, eller null.</param>
    /// <param name="notice">Noe appen rettet selv, til loggen, eller null.</param>
    internal static string Resolve(string configured, out string? problem, out string? notice)
    {
        problem = null;
        notice = null;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException ex)
        {
            problem = $"Tilkoblingsstrengen kan ikke leses: {ex.Message}";
            return configured;
        }

        if (IsPlaceholder(builder.Password))
        {
            problem =
                "Passordet i tilkoblingsstrengen er plassholderen DITT_PASSORD fra README-en. Hent " +
                "«Database password» i Supabase (Project Settings -> Database) og sett strengen på nytt " +
                "med dotnet user-secrets, med det ekte passordet.";
        }

        var certificate = builder.RootCertificate;

        if (!string.IsNullOrWhiteSpace(certificate))
        {
            var expanded = Expand(certificate);

            if (File.Exists(expanded))
            {
                builder.RootCertificate = expanded;
            }
            else if (File.Exists(DefaultCertificatePath))
            {
                builder.RootCertificate = DefaultCertificatePath;
                notice =
                    $"Root Certificate peker på «{certificate}», som ikke finnes. Bruker {DefaultCertificatePath} " +
                    "i stedet. Rett stien i user-secrets når det passer.";
            }
            else
            {
                var placeholder = IsPlaceholder(certificate)
                    ? " Stien har fortsatt plassholderen DITT_BRUKERNAVN fra README-en."
                    : string.Empty;

                problem ??=
                    $"Fant ikke CA-sertifikatet til Supabase på «{expanded}».{placeholder} Last ned " +
                    "«prod-ca-2021.crt» i Supabase (Project Settings -> Database -> SSL Configuration -> " +
                    $"Download certificate) og legg den i {DefaultCertificatePath}; der finner appen den " +
                    "selv. Eller rett «Root Certificate=» i user-secrets til der fila ligger -- " +
                    "%APPDATA% kan brukes i stien.";
            }
        }
        else if (builder.SslMode is SslMode.VerifyFull or SslMode.VerifyCA && File.Exists(DefaultCertificatePath))
        {
            // Supabase-CA-en ligger ikke i maskinens rotlager. Uten en sti her ville VerifyFull
            // avvist serveren, selv med fila på plass der README-en sier.
            builder.RootCertificate = DefaultCertificatePath;
        }

        // Supabase's session pooler lets the whole project have 15 clients at once, shared by
        // everybody running the app against it. Npgsql's own pool allows 100, and the best eleven
        // asks for a photo per player -- some 85 requests, each with a connection, at once:
        // "EMAXCONNSESSION max clients reached". A small pool makes them wait their turn instead,
        // and idle connections go back quickly so the others on the project get theirs.
        if (builder.Host?.EndsWith(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (builder.MaxPoolSize == DefaultMaxPoolSize)
            {
                builder.MaxPoolSize = SupabaseMaxPoolSize;
            }

            if (builder.ConnectionIdleLifetime == DefaultIdleLifetimeSeconds)
            {
                builder.ConnectionIdleLifetime = SupabaseIdleLifetimeSeconds;
            }
        }

        return builder.ConnectionString;
    }

    // Npgsql's defaults. Only those are changed: a value someone set in the string is theirs.
    private const int DefaultMaxPoolSize = 100;
    private const int DefaultIdleLifetimeSeconds = 300;

    /// <summary>Five per running app leaves room for two more on the project within the 15.</summary>
    private const int SupabaseMaxPoolSize = 5;
    private const int SupabaseIdleLifetimeSeconds = 30;

    private static bool IsPlaceholder(string? value) =>
        value is not null && value.Contains("DITT_", StringComparison.OrdinalIgnoreCase);

    private static string Expand(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        if (expanded.StartsWith('~'))
        {
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + expanded[1..];
        }

        return expanded;
    }
}
