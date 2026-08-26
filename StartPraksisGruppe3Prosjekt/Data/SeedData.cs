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

    /// <summary>The one coach account. Kept as-is so nobody has to relearn a login.</summary>
    private const string CoachEmail = "trener.senior@ikstart.example";

    /// <summary>The second coach account, folded into <see cref="CoachEmail"/>.</summary>
    private const string RetiredCoachEmail = "trener.ungdom@ikstart.example";

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

        // Runs every start, and deliberately outside SeedUsersAndPlayersAsync: that method
        // returns early once players exist, and the two coach accounts it needs to fold
        // together were seeded long before this step existed.
        await ConsolidateCoachAsync(db, userManager, logger);
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

    /// <summary>
    /// Team names and player positions are text that shows up on screen, so they are English
    /// like the rest of the interface. Existing rows are renamed in place rather than
    /// re-created: a new "Senior" team next to the old "A-laget" would leave every player on
    /// the old one, and the coach looking at an empty squad.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, Team>> SeedTeamsAsync(AppDbContext db)
    {
        await RenameTeamAsync(db, "A-laget", "Senior");
        await TranslatePositionsAsync(db);
        await db.SaveChangesAsync();

        var names = new[] { "Senior", "G19", "G16" };

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

    private static async Task RenameTeamAsync(AppDbContext db, string oldName, string newName)
    {
        if (await db.Teams.AnyAsync(t => t.Name == newName))
        {
            return;
        }

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Name == oldName);
        if (team is not null)
        {
            team.Name = newName;
        }
    }

    /// <summary>
    /// Norwegian positions on players seeded before the interface moved to English.
    /// Idempotent: a position already in English matches nothing and is left alone.
    /// </summary>
    private static async Task TranslatePositionsAsync(AppDbContext db)
    {
        var translations = new Dictionary<string, string>
        {
            ["Keeper"] = "Goalkeeper",
            ["Midtstopper"] = "Centre-back",
            ["Kantspiller"] = "Winger",
            ["Spiss"] = "Striker",
            ["Midtbane"] = "Midfielder",
            ["Back"] = "Full-back"
        };

        var players = await db.Players
            .Where(p => p.Position != null)
            .ToListAsync();

        foreach (var player in players)
        {
            if (player.Position is { } position && translations.TryGetValue(position, out var english))
            {
                player.Position = english;
            }
        }
    }

    /// <summary>
    /// The measurement periods. Idempotent PER ROUND rather than "skip everything if any
    /// round exists" -- otherwise a new period can never be added to a database that has
    /// already been seeded, which is exactly the situation a new period arrives in.
    ///
    /// Adding a period here is one of two supported ways. The other is the admin page,
    /// Admin/Periods, which does the same thing through <see cref="Services.IPeriodService"/>.
    /// Both go through the same validation, so neither is a special case.
    /// </summary>
    private static async Task SeedRoundsAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        // Rounds seeded before the interface moved to English carry Norwegian names, and a
        // round name is text a player reads. Renamed rather than re-added: adding would put
        // "Spring 2026" next to "Vår 2026" and split the answers across two periods.
        await RenameRoundAsync(db, $"Vår {now.Year}", $"Spring {now.Year}");
        await RenameRoundAsync(db, $"Høst {now.Year}", $"Autumn {now.Year}");
        await db.SaveChangesAsync();

        // ONE placeholder period while the club settles on what the real ones are. Autumn
        // is the one that stays; Spring and Winter were seeded earlier and are removed
        // below. Add more through Admin/Periods -- that is what it is for.
        await EnsureRoundAsync(
            db,
            $"Autumn {now.Year}",
            now.AddDays(-7),
            now.AddDays(21));

        await db.SaveChangesAsync();

        await RemoveEmptyRoundsExceptAsync(db, $"Autumn {now.Year}");
    }

    /// <summary>
    /// Removes every period except the one named, and only where it holds no answers.
    ///
    /// A period with submissions is left alone and logged. Deleting one cascades to the
    /// answers inside it, and quietly throwing away somebody's answers because a seed step
    /// wanted a tidier list is not a trade this should make on its own.
    /// </summary>
    private static async Task RemoveEmptyRoundsExceptAsync(AppDbContext db, string keepName)
    {
        var others = await db.SurveyRounds
            .Where(r => r.Name != keepName)
            .ToListAsync();

        foreach (var round in others)
        {
            var hasFiveCAnswers = await db.FiveCSubmissions.AnyAsync(s => s.RoundId == round.Id);
            var hasLegacyAnswers = await db.Responses.AnyAsync(r => r.RoundId == round.Id);

            if (hasFiveCAnswers || hasLegacyAnswers)
            {
                continue;
            }

            db.SurveyRounds.Remove(round);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Folds the second coach account into the first.
    ///
    /// Runs on every start, not only on a fresh database: the shared Supabase database was
    /// seeded with two coaches long before this ran, and the ordinary seeding steps skip
    /// themselves once players exist. A consolidation that only worked on an empty database
    /// would never have consolidated anything.
    ///
    /// Its teams move across before it goes, so no team is left without a coach.
    /// </summary>
    private static async Task ConsolidateCoachAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        ILogger logger)
    {
        var retired = await userManager.FindByEmailAsync(RetiredCoachEmail);
        if (retired is null)
        {
            return;
        }

        var survivor = await userManager.FindByEmailAsync(CoachEmail);
        if (survivor is null)
        {
            logger.LogWarning(
                "Skipping coach consolidation: {Survivor} does not exist, so removing {Retired} " +
                "would leave the club without a coach account.",
                CoachEmail,
                RetiredCoachEmail);
            return;
        }

        // Move the teams over, skipping any the surviving coach already has.
        var retiredTeams = await db.CoachTeams
            .Where(ct => ct.CoachUserId == retired.Id)
            .ToListAsync();

        var survivorTeamIds = await db.CoachTeams
            .Where(ct => ct.CoachUserId == survivor.Id)
            .Select(ct => ct.TeamId)
            .ToListAsync();

        foreach (var link in retiredTeams)
        {
            if (!survivorTeamIds.Contains(link.TeamId))
            {
                db.CoachTeams.Add(new CoachTeam
                {
                    CoachUserId = survivor.Id,
                    TeamId = link.TeamId
                });

                survivorTeamIds.Add(link.TeamId);
            }

            db.CoachTeams.Remove(link);
        }

        await db.SaveChangesAsync();

        // The append-only logs record user ids as plain strings, with no foreign key to
        // Identity. Deleting the account would not fail -- it would quietly turn every row
        // that names it into an id nobody can resolve. An audit log that cannot say who did
        // something is not an audit log, so in that case the account stays and is only
        // stripped of what it can do.
        var appearsInAuditTrail =
            await db.ConsentEvents.AnyAsync(c => c.ChangedByUserId == retired.Id)
            || await db.PlayerAccessEvents.AnyAsync(a => a.ViewedByUserId == retired.Id)
            || await db.FeedbackReleases.AnyAsync(f => f.CoachUserId == retired.Id);

        if (appearsInAuditTrail)
        {
            await userManager.RemoveFromRoleAsync(retired, Roles.Coach);
            await userManager.SetLockoutEnabledAsync(retired, true);
            await userManager.SetLockoutEndDateAsync(retired, DateTimeOffset.MaxValue);

            logger.LogInformation(
                "Coach {Retired} appears in the audit trail, so the account was disabled " +
                "rather than deleted. Its teams moved to {Survivor}.",
                RetiredCoachEmail,
                CoachEmail);

            return;
        }

        // Nothing references it. Its answers to the older ten-statement form go with it --
        // they were fabricated demo data and mean nothing without the account.
        var orphanedResponses = await db.Responses
            .Where(r => r.RespondentUserId == retired.Id)
            .ToListAsync();

        if (orphanedResponses.Count > 0)
        {
            db.Responses.RemoveRange(orphanedResponses);
            await db.SaveChangesAsync();
        }

        await userManager.DeleteAsync(retired);

        logger.LogInformation(
            "Coach {Retired} removed; its teams and duties are now {Survivor}'s.",
            RetiredCoachEmail,
            CoachEmail);
    }

    /// <summary>
    /// Renames a round in place, keeping its id and therefore every answer attached to it.
    /// Does nothing if the old name is gone, or if the new name is already taken.
    /// </summary>
    private static async Task RenameRoundAsync(AppDbContext db, string oldName, string newName)
    {
        if (await db.SurveyRounds.AnyAsync(r => r.Name == newName))
        {
            return;
        }

        var round = await db.SurveyRounds.FirstOrDefaultAsync(r => r.Name == oldName);
        if (round is not null)
        {
            round.Name = newName;
        }
    }

    /// <summary>Adds a round if no round by that name exists. Never edits an existing one.</summary>
    private static async Task EnsureRoundAsync(
        AppDbContext db,
        string name,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt)
    {
        if (await db.SurveyRounds.AnyAsync(r => r.Name == name))
        {
            return;
        }

        db.SurveyRounds.Add(new SurveyRound
        {
            Name = name,
            OpensAt = opensAt,
            ClosesAt = closesAt
        });
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

        // One coach, on every team. The coach role is not team-scoped any more -- CanViewPlayer
        // lets any coach see any player -- so a second account only added a login to remember.
        var coachId = await EnsureUserAsync(userManager, CoachEmail, password, Roles.Coach);

        foreach (var team in teams.Values)
        {
            db.CoachTeams.Add(new CoachTeam { CoachUserId = coachId, TeamId = team.Id });
        }

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
            // Senior
            new("TS-98-07", "Senior", new DateOnly(1998, 3, 11), "Goalkeeper", null, ConsentLevel.Full, Array.Empty<string>()),
            new("TS-01-22", "Senior", new DateOnly(2001, 11, 2), "Centre-back", null, ConsentLevel.Aggregated, Array.Empty<string>()),
            new("TS-05-09", "Senior", new DateOnly(2005, 6, 19), "Winger", playerUser4, ConsentLevel.Full, Array.Empty<string>()),
            new("TS-08-16", "Senior", new DateOnly(2008, 9, 30), "Striker", playerUser1, ConsentLevel.Full, new[] { guardian1 }),

            // G19
            new("TS-07-03", "G19", new DateOnly(2007, 4, 5), "Midfielder", null, ConsentLevel.Aggregated, Array.Empty<string>()),
            new("TS-07-14", "G19", new DateOnly(2007, 12, 1), "Full-back", playerUser2, ConsentLevel.Full, new[] { guardian2 }),
            new("TS-08-05", "G19", new DateOnly(2008, 2, 17), "Centre-back", null, ConsentLevel.None, new[] { guardian3 }),
            new("TS-08-11", "G19", new DateOnly(2008, 8, 22), "Striker", null, ConsentLevel.Aggregated, new[] { guardian4 }),

            // G16
            new("TS-10-02", "G16", new DateOnly(2010, 1, 14), "Goalkeeper", playerUser3, ConsentLevel.Full, new[] { guardian3 }),
            new("TS-10-08", "G16", new DateOnly(2010, 5, 27), "Midfielder", null, ConsentLevel.Aggregated, new[] { guardian5 }),
            new("TS-11-04", "G16", new DateOnly(2011, 3, 9), "Winger", null, ConsentLevel.None, new[] { guardian6 }),

            // Denne har ingen ConsentEvent i det hele tatt — gjeldende nivå blir None.
            // Det er en egen tilstand fra "noen har aktivt satt None", og begge skal virke.
            new("TS-11-12", "G16", new DateOnly(2011, 10, 21), "Striker", null, null, new[] { guardian7 })
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
