using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Grunnoppsettet: det en database må ha for at appen skal virke i det hele tatt, i alle
/// miljøer. Rollene i <see cref="Roles.All"/>, og lagene G14, G15, G17 og G19.
///
/// Uten rollene kan ingen gis tilgang til noe, og uten lagene har en spiller ingen plass å
/// stå. Ingen av delene kan lages i appen ennå, så de kommer herfra.
///
/// Idempotent, og oppretter bare det som mangler. Ingenting her endrer eller sletter noe som
/// finnes: et lag som har fått et annet navn, eller en rolle noen har lagt til, blir stående.
///
/// INGEN PERIODER, INGEN KONTOER OG INGEN SPILLERE. Perioder lager en administrator på
/// Admin/Periods, den første administratoren lages med create-admin, og spillerne kommer inn
/// med import-players. Demodataene til utvikling er <see cref="SeedData"/> sin sak.
/// </summary>
internal static class BaseSetup
{
    /// <summary>
    /// Klubbens akademilag, med klubbens egne navn (G for gutter). G14, G15 og G17 er de tre
    /// prosjektet gjelder; G19 kom til med de ekte troppene fra ikstart.no.
    /// </summary>
    internal static readonly string[] TeamNames = { "G14", "G15", "G17", "G19" };

    public static async Task EnsureAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        var roles = 0;

        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(role));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Klarte ikke å opprette rollen {role}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            roles++;
        }

        var existing = await db.Teams.Select(t => t.Name).ToListAsync();
        var teams = 0;

        foreach (var name in TeamNames.Except(existing))
        {
            db.Teams.Add(new Team { Name = name });
            teams++;
        }

        await db.SaveChangesAsync();

        if (roles + teams > 0)
        {
            logger.LogInformation("Grunnoppsett: {Roles} roller og {Teams} lag opprettet.", roles, teams);
        }
    }
}
