using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Eier: Brage.
///
/// ALLE DATA HER ER OPPDIKTET. Ekte spillerdata skal ikke inn i dette repoet før
/// prosjektet er meldt til Sikt. Ingen navn, telefonnumre eller e-postadresser til
/// virkelige personer — bruk koder og example-domener.
///
/// Seedingen er idempotent: hvert steg hopper over seg selv hvis dataene finnes.
/// Brukerkontoer opprettes bare i Development.
/// </summary>
public static class SeedData
{
    /// <summary>Passord for demokontoene. Overstyres med Seed:DevPassword i user-secrets.</summary>
    private const string DefaultDevPassword = "Dev!passord1";

    /// <summary>Dato all alder regnes ut fra i seedingen.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeedData));

        await db.Database.MigrateAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        await SeedRolesAsync(roleManager);

        await SeedItemsAsync(db);
        var teams = await SeedTeamsAsync(db);
        await SeedRoundsAsync(db);

        if (!environment.IsDevelopment())
        {
            logger.LogInformation(
                "Hopper over demobrukere og oppdiktede spillere: miljøet er ikke Development.");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var password = configuration["Seed:DevPassword"] ?? DefaultDevPassword;

        await SeedUsersAndPlayersAsync(db, userManager, teams, password, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>
    /// De ti påstandene. Påstand 5 er negativt formulert og er den eneste med
    /// IsReversed = true — den skåres som (6 - verdi).
    /// Spilleren svarer på disse om seg selv; treneren svarer på hva hen tror
    /// spilleren har svart. Ordlyden i skjemaet snus i visningen, ikke i basen.
    /// </summary>
    private static async Task SeedItemsAsync(AppDbContext db)
    {
        if (await db.Items.AnyAsync())
        {
            return;
        }

        const string roleClarity = "Rolleforståelse";
        const string safety = "Trygghet";
        const string mastery = "Mestring";

        db.Items.AddRange(
            new Item { Number = 1, Construct = roleClarity, Text = "Jeg vet hva som forventes av meg i rollen min på laget." },
            new Item { Number = 2, Construct = roleClarity, Text = "Jeg forstår hvorfor jeg får de oppgavene jeg får på trening og i kamp." },
            new Item { Number = 3, Construct = roleClarity, Text = "Jeg vet hva jeg må jobbe med for å bli bedre." },
            new Item { Number = 4, Construct = safety, Text = "Jeg tør å prøve nye ting på trening selv om jeg kan mislykkes." },
            new Item { Number = 5, Construct = safety, IsReversed = true, Text = "Jeg er redd for å gjøre feil foran de andre på laget." },
            new Item { Number = 6, Construct = safety, Text = "Jeg kan si ifra til treneren hvis noe er vanskelig." },
            new Item { Number = 7, Construct = safety, Text = "Jeg føler meg som en del av laget." },
            new Item { Number = 8, Construct = mastery, Text = "Jeg opplever at jeg mestrer oppgavene jeg får på trening." },
            new Item { Number = 9, Construct = mastery, Text = "Jeg får tilbakemeldinger som hjelper meg å bli bedre." },
            new Item { Number = 10, Construct = mastery, Text = "Jeg har blitt bedre som fotballspiller de siste månedene." });

        await db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyDictionary<string, Team>> SeedTeamsAsync(AppDbContext db)
    {
        var names = new[] { "A-laget", "G19", "G16" };

        foreach (var name in names)
        {
            if (!await db.Teams.AnyAsync(t => t.Name == name))
            {
                db.Teams.Add(new Team { Name = name });
            }
        }

        await db.SaveChangesAsync();

        return await db.Teams.ToDictionaryAsync(t => t.Name, t => t);
    }

    private static async Task SeedRoundsAsync(AppDbContext db)
    {
        if (await db.SurveyRounds.AnyAsync())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        db.SurveyRounds.AddRange(
            new SurveyRound
            {
                Name = $"Vår {now.Year}",
                OpensAt = new DateTimeOffset(now.Year, 3, 1, 0, 0, 0, TimeSpan.Zero),
                ClosesAt = new DateTimeOffset(now.Year, 3, 31, 23, 59, 59, TimeSpan.Zero)
            },
            new SurveyRound
            {
                Name = $"Høst {now.Year}",
                OpensAt = now.AddDays(-7),
                ClosesAt = now.AddDays(21)
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Oppdiktede brukere, spillere, koblinger og samtykkehendelser.
    /// Regelen som håndheves til slutt: hver spiller under
    /// <see cref="PlayerRules.GuardianRequiredBelowAge"/> år må ha minst én foresatt.
    /// </summary>
    private static async Task SeedUsersAndPlayersAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IReadOnlyDictionary<string, Team> teams,
        string password,
        ILogger logger)
    {
        if (await db.Players.AnyAsync())
        {
            return;
        }

        var adminId = await EnsureUserAsync(userManager, "admin@ikstart.example", password, Roles.Admin);

        var coachSeniorId = await EnsureUserAsync(userManager, "trener.senior@ikstart.example", password, Roles.Coach);
        var coachYouthId = await EnsureUserAsync(userManager, "trener.ungdom@ikstart.example", password, Roles.Coach);

        db.CoachTeams.AddRange(
            new CoachTeam { CoachUserId = coachSeniorId, TeamId = teams["A-laget"].Id },
            new CoachTeam { CoachUserId = coachSeniorId, TeamId = teams["G19"].Id },
            new CoachTeam { CoachUserId = coachYouthId, TeamId = teams["G16"].Id });

        // Spillerkontoer: bare noen av spillerne har fått konto ennå. Resten har UserId = null.
        var playerUser1 = await EnsureUserAsync(userManager, "spiller.ts0816@ikstart.example", password, Roles.Player);
        var playerUser2 = await EnsureUserAsync(userManager, "spiller.ts0714@ikstart.example", password, Roles.Player);
        var playerUser3 = await EnsureUserAsync(userManager, "spiller.ts1002@ikstart.example", password, Roles.Player);
        var playerUser4 = await EnsureUserAsync(userManager, "spiller.ts0509@ikstart.example", password, Roles.Player);

        // Foresatte. Foresatt 3 er registrert på to søsken — det skal fungere.
        var guardian1 = await EnsureUserAsync(userManager, "foresatt1@example.test", password, Roles.Guardian);
        var guardian2 = await EnsureUserAsync(userManager, "foresatt2@example.test", password, Roles.Guardian);
        var guardian3 = await EnsureUserAsync(userManager, "foresatt3@example.test", password, Roles.Guardian);
        var guardian4 = await EnsureUserAsync(userManager, "foresatt4@example.test", password, Roles.Guardian);
        var guardian5 = await EnsureUserAsync(userManager, "foresatt5@example.test", password, Roles.Guardian);
        var guardian6 = await EnsureUserAsync(userManager, "foresatt6@example.test", password, Roles.Guardian);
        var guardian7 = await EnsureUserAsync(userManager, "foresatt7@example.test", password, Roles.Guardian);

        // Spillerne er identifisert med kode, ikke navn. Fødselsdatoene er valgt slik at
        // vi får både myndige og mindreårige på samme lag.
        var seeds = new List<PlayerSeed>
        {
            // A-laget
            new("TS-98-07", "A-laget", new DateOnly(1998, 3, 11), "Keeper", null, ConsentLevel.Full, Array.Empty<string>()),
            new("TS-01-22", "A-laget", new DateOnly(2001, 11, 2), "Midtstopper", null, ConsentLevel.Aggregated, Array.Empty<string>()),
            new("TS-05-09", "A-laget", new DateOnly(2005, 6, 19), "Kantspiller", playerUser4, ConsentLevel.Full, Array.Empty<string>()),
            new("TS-08-16", "A-laget", new DateOnly(2008, 9, 30), "Spiss", playerUser1, ConsentLevel.Full, new[] { guardian1 }),

            // G19
            new("TS-07-03", "G19", new DateOnly(2007, 4, 5), "Midtbane", null, ConsentLevel.Aggregated, Array.Empty<string>()),
            new("TS-07-14", "G19", new DateOnly(2007, 12, 1), "Back", playerUser2, ConsentLevel.Full, new[] { guardian2 }),
            new("TS-08-05", "G19", new DateOnly(2008, 2, 17), "Midtstopper", null, ConsentLevel.None, new[] { guardian3 }),
            new("TS-08-11", "G19", new DateOnly(2008, 8, 22), "Spiss", null, ConsentLevel.Aggregated, new[] { guardian4 }),

            // G16
            new("TS-10-02", "G16", new DateOnly(2010, 1, 14), "Keeper", playerUser3, ConsentLevel.Full, new[] { guardian3 }),
            new("TS-10-08", "G16", new DateOnly(2010, 5, 27), "Midtbane", null, ConsentLevel.Aggregated, new[] { guardian5 }),
            new("TS-11-04", "G16", new DateOnly(2011, 3, 9), "Kantspiller", null, ConsentLevel.None, new[] { guardian6 }),

            // Denne har ingen ConsentEvent i det hele tatt — gjeldende nivå blir None.
            // Det er en egen tilstand fra "noen har aktivt satt None", og begge skal virke.
            new("TS-11-12", "G16", new DateOnly(2011, 10, 21), "Spiss", null, null, new[] { guardian7 })
        };

        foreach (var seed in seeds)
        {
            var player = new Player
            {
                Code = seed.Code,
                TeamId = teams[seed.TeamName].Id,
                BirthDate = seed.BirthDate,
                Position = seed.Position,
                UserId = seed.UserId
            };

            db.Players.Add(player);
            await db.SaveChangesAsync();

            foreach (var guardianUserId in seed.GuardianUserIds)
            {
                db.Guardianships.Add(new Guardianship
                {
                    PlayerId = player.Id,
                    GuardianUserId = guardianUserId
                });
            }

            if (seed.Consent is { } level)
            {
                // Samtykket settes av en foresatt der det finnes en, ellers av spilleren selv.
                var changedBy = seed.GuardianUserIds.FirstOrDefault() ?? seed.UserId ?? adminId;

                db.ConsentEvents.Add(new ConsentEvent
                {
                    PlayerId = player.Id,
                    Level = level,
                    ChangedByUserId = changedBy,
                    OccurredAt = DateTimeOffset.UtcNow.AddDays(-30)
                });
            }
        }

        await db.SaveChangesAsync();

        await SeedWithdrawnConsentAsync(db);
        await AssertGuardianRuleAsync(db, logger);
    }

    /// <summary>
    /// Én spiller får en historikk der samtykket først var Full og senere ble trukket ned
    /// til Aggregated. Den gamle raden blir stående — det er hele poenget med loggen.
    /// </summary>
    private static async Task SeedWithdrawnConsentAsync(AppDbContext db)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Code == "TS-08-11");
        if (player is null)
        {
            return;
        }

        var guardianUserId = await db.Guardianships
            .Where(g => g.PlayerId == player.Id)
            .Select(g => g.GuardianUserId)
            .FirstOrDefaultAsync();

        if (guardianUserId is null)
        {
            return;
        }

        db.ConsentEvents.Add(new ConsentEvent
        {
            PlayerId = player.Id,
            Level = ConsentLevel.Full,
            ChangedByUserId = guardianUserId,
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-90)
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Kontroll etter seeding: ingen spiller under aldersgrensen skal stå uten foresatt.
    /// Kaster hvis regelen er brutt — da er seed-dataene feil, og det skal merkes med en gang.
    /// </summary>
    private static async Task AssertGuardianRuleAsync(AppDbContext db, ILogger logger)
    {
        var players = await db.Players
            .Include(p => p.Guardianships)
            .AsNoTracking()
            .ToListAsync();

        var missing = players
            .Where(p => p.AgeAt(Today) < PlayerRules.GuardianRequiredBelowAge)
            .Where(p => p.Guardianships.Count == 0)
            .Select(p => p.Code)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Seed-data bryter regelen om foresatt for mindreårige. Mangler foresatt: " +
                string.Join(", ", missing));
        }

        logger.LogInformation("Seeding fullført: {PlayerCount} oppdiktede spillere.", players.Count);
    }

    private static async Task<string> EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        string email,
        string password,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Klarte ikke å opprette demobrukeren {email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return user.Id;
    }

    private sealed record PlayerSeed(
        string Code,
        string TeamName,
        DateOnly BirthDate,
        string Position,
        string? UserId,
        ConsentLevel? Consent,
        IReadOnlyList<string> GuardianUserIds);
}
