using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Fornavn til velkomsten, for Development.
///
/// Fornavnet er første del av navnet spilleren har i SeedData -- oppdiktet, som resten av
/// seedingen, bortsett fra prosjektgruppas egne. Ingenting her står i trenernes arbeidsbok.
///
/// Bare fornavn, og ingen bilder. Ekte bilder av spillerne legges inn av admin i appen, på
/// /Admin/Players, og aldri i repoet. Uten bilde viser velkomsten bare navnet -- vil man se
/// bildet i utvikling, er det én opplasting på den siden.
///
/// Bare spillere med konto: velkomsten vises når man logger inn, og uten konto logger ingen inn.
/// Idempotent per spiller -- et navn som finnes, er ikke seedingens å endre. Unntaket er
/// omdøpingen fra kode til navn i SeedData, som bytter ut fornavnet som ble trukket tilfeldig
/// den gang spillerne het koder.
/// </summary>
internal static class SeedWelcome
{
    public static async Task SeedAsync(AppDbContext db, UserManager<IdentityUser> userManager, ILogger logger)
    {
        var admin = await userManager.FindByEmailAsync("admin@ikstart.example");
        if (admin is null)
        {
            return;
        }

        var have = (await db.PlayerPersonalDetails.Select(d => d.PlayerId).ToListAsync()).ToHashSet();

        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.UserId != null)
            .OrderBy(p => p.Id)
            .ToListAsync();

        var added = 0;

        foreach (var player in players.Where(p => !have.Contains(p.Id)))
        {
            db.PlayerPersonalDetails.Add(new PlayerPersonalDetails
            {
                PlayerId = player.Id,
                FirstName = SeedData.FirstNameOf(player.Code),
                UpdatedByUserId = admin.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            added++;
        }

        if (added == 0)
        {
            return;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("La til fornavn for {Count} spillere med konto.", added);
    }
}
