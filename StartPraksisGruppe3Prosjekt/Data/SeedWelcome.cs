using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Oppdiktede fornavn til velkomsten, for Development.
///
/// OPPDIKTET, som resten av seedingen: fornavnene er valgt fra en liste med vanlige navn, og
/// ingen av dem står i trenernes arbeidsbok eller er navnet på noen i prosjektgruppa. De
/// knyttes til de oppdiktede spillerkodene, så de er ikke noen.
///
/// Bare fornavn, og ingen bilder. Ekte bilder av spillerne legges inn av admin i appen, på
/// /Admin/Players, og aldri i repoet. Uten bilde viser velkomsten bare navnet -- vil man se
/// bildet i utvikling, er det én opplasting på den siden.
///
/// Bare spillere med konto: velkomsten vises når man logger inn, og uten konto logger ingen inn.
/// Idempotent per spiller -- et navn som finnes, er ikke seedingens å endre.
/// </summary>
internal static class SeedWelcome
{
    private static readonly string[] FirstNames =
    {
        "Alex", "Sam", "Robin", "Kim", "Andrea", "Chris", "Sigurd", "Ola", "Håvard", "Vetle",
        "Sindre", "Torstein", "Aksel", "Isak", "Mikkel", "Adrian", "Oliver", "Olav", "Kristoffer",
        "Jørgen", "Erlend", "Sverre", "Trym", "Eskil", "Ivar", "Petter", "Ruben", "Simen", "Viljar",
        "Amund"
    };

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
                FirstName = FirstNames[SeedData.StableSeed($"{player.Code}|first-name") % FirstNames.Length],
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

        logger.LogInformation("La til oppdiktede fornavn for {Count} spillere med konto.", added);
    }
}
