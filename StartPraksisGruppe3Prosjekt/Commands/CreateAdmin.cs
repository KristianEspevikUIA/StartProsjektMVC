using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;

namespace StartPraksisGruppe3Prosjekt.Commands;

/// <summary>
/// Lager den første administratoren:
///
///     dotnet StartPraksisGruppe3Prosjekt.dll create-admin --email navn@klubben.no
///
/// Uten denne er en ny database i drift låst: selvregistrering er stengt, appen sender ikke
/// e-post, og siden der en administrator oppretter kontoer, finnes ikke ennå -- og krever
/// uansett en administrator for å åpnes.
///
/// BARE DEN FØRSTE. Finnes det en administrator fra før, nekter kommandoen. Den er en vei inn
/// i en tom database, ikke en bakdør forbi appens egen brukeradministrasjon: den som kan kjøre
/// kommandoer på serveren, skal ikke kunne gi seg selv tilgang til spillerdata i en database
/// som allerede er i bruk.
///
/// PASSORDET LESES FRA STANDARD INPUT, aldri fra argumentlista. Et argument blir liggende i
/// shell-historikken og er synlig i prosesslista mens kommandoen kjører. Det følger de samme
/// reglene som alle andre passord i appen (Identity, satt opp i Program.cs), og skrives ikke
/// til loggen.
/// </summary>
internal static class CreateAdmin
{
    public const string Command = "create-admin";

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string? email,
        ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // Bare mot en database som er tatt i bruk, og som hører til miljøet kommandoen kjøres i.
        await DatabaseGuard.RequireMarkedAsAsync(db, DatabaseGuard.KindOf(environment));

        email = email?.Trim();

        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException(
                "Oppgi e-postadressen administratoren skal logge inn med: create-admin --email navn@klubben.no");
        }

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            throw new InvalidOperationException(
                "Rollen Admin finnes ikke i databasen. Start appen én gang først; den legger inn rollene.");
        }

        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            throw new InvalidOperationException(
                "Det finnes allerede en administrator. create-admin lager bare den første; flere kontoer " +
                "opprettes av en administrator i appen.");
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new InvalidOperationException("Det finnes allerede en konto med den e-postadressen.");
        }

        var password = ReadPassword();

        // Kontoen og rollen sammen: en konto som ble stående uten rollen, ville stoppet neste
        // forsøk med «adressen finnes allerede», uten at noen var blitt administrator.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,

            // Appen sender ikke e-post, så det finnes ingen bekreftelse å vente på.
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            // Identity sier hvilken regel som ble brutt. Meldingene inneholder ikke passordet.
            throw new InvalidOperationException(
                "Kontoen ble ikke opprettet: " + string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var granted = await userManager.AddToRoleAsync(user, Roles.Admin);

        if (!granted.Succeeded)
        {
            throw new InvalidOperationException(
                "Kontoen ble ikke opprettet: " + string.Join(" ", granted.Errors.Select(e => e.Description)));
        }

        await transaction.CommitAsync();

        logger.LogWarning(
            "Den første administratoren er opprettet med create-admin (bruker-ID {UserId}). " +
            "Passordet er ikke logget, og kan byttes under «My account» etter innlogging.",
            user.Id);

        return 0;
    }

    /// <summary>
    /// Én linje fra standard input. I en terminal spørres det to ganger, uten at tegnene vises;
    /// når input er en pipe eller en fil, leses linja som den er.
    /// </summary>
    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine() ?? string.Empty;
        }

        Console.Error.Write("Passord for administratoren: ");
        var password = ReadHidden();

        Console.Error.Write("Gjenta passordet: ");
        var repeated = ReadHidden();

        if (!string.Equals(password, repeated, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("De to passordene er ikke like. Ingenting er opprettet.");
        }

        return password;
    }

    private static string ReadHidden()
    {
        var typed = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }
}
