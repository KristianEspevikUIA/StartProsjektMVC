using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Eier: Brage.
///
/// BARE EKTE DATA. Appen har ingen oppdiktede spillere, foresatte, samtykker eller svar lenger:
/// spillerne er klubbens tropper fra ikstart.no (<see cref="SeedSquads"/>), og succession er
/// trenernes egne ark (<see cref="SeedSuccessionImport"/>). Begge ligger i git-ignorerte mapper,
/// fordi repoet er offentlig og nesten alle spillerne er mindreårige. Det som ble diktet opp før,
/// fjernes ved oppstart (<see cref="RemoveMadeUpDataAsync"/>).
///
/// Det som fortsatt seedes, er innhold og innlogging, ikke personer: rollene, lagene, de ti
/// eldre påstandene, én åpen periode, og kontoene admin og trener.senior i Development.
///
/// Seedingen er idempotent: hvert steg hopper over seg selv hvis dataene finnes.
/// </summary>
public static class SeedData
{
    /// <summary>Passord for demokontoene. Overstyres med Seed:DevPassword i user-secrets.</summary>
    private const string DefaultDevPassword = "Dev!passord1";

    /// <summary>The one coach account. Kept as-is so nobody has to relearn a login.</summary>
    internal const string CoachEmail = "trener.senior@ikstart.example";

    /// <summary>The second coach account, folded into <see cref="CoachEmail"/>.</summary>
    private const string RetiredCoachEmail = "trener.ungdom@ikstart.example";

    /// <summary>Dato all alder regnes ut fra i seedingen.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// How long the placeholder period stays open for, from the moment it is seeded or
    /// reopened.
    ///
    /// It was three weeks, which is a reasonable length for a real measurement period and
    /// the wrong length for a placeholder: it is meant to hold the door open until the
    /// club decides what the real periods are, and that decision takes longer than three
    /// weeks. A quarter is long enough that a database seeded once stays usable.
    /// </summary>
    private const int PlaceholderOpenDays = 90;

    /// <summary>The one period seeded in every environment, until the club sets its own.</summary>
    private static string PlaceholderRoundName => $"Autumn {DateTimeOffset.UtcNow.Year}";

    /// <summary>
    /// Alt seedingen diktet opp, ble skrevet før denne dagen: spillere, foresatte, samtykker,
    /// 5C-svar og frigivelser. Det som kommer etterpå, er ekte bruk og blir stående.
    /// </summary>
    internal static readonly DateTimeOffset MadeUpBefore = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

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
        var teams = await SeedTeamsAsync(db, logger);
        await SeedRoundsAsync(db, logger);

        if (!environment.IsDevelopment())
        {
            logger.LogInformation(
                "Hopper over demokontoer, troppene og importen: miljøet er ikke Development.");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var password = configuration["Seed:DevPassword"] ?? DefaultDevPassword;

        // De ekte troppene, når de er hentet. Null uten fila -- se SeedSquads.
        var squads = SeedSquads.Load(environment);

        // Trenernes egne succession-ark, når de er importert. Lest før noe skrives, som troppene:
        // en fil som ikke henger sammen, stopper oppstarten før den har endret noe.
        var successionCatalog = services.GetRequiredService<Services.Succession.ISuccessionCatalog>();
        var successionImport = SeedSuccessionImport.Load(environment, successionCatalog);

        // The staff's logins: the admin and the coach, on every team. No players -- they are the
        // club's own squads, below.
        await SeedStaffAccountsAsync(db, userManager, teams, password);

        // Runs every start: the two coach accounts it folds together were seeded long before
        // this step existed.
        await ConsolidateCoachAsync(db, userManager, logger);

        // Everything made up before the real data came, gone -- and on every start, so a shared
        // base seeded with it is cleaned up the next time anybody runs the app.
        await RemoveMadeUpDataAsync(db, userManager, logger);
        await SeedSuccessionImport.RemoveMadeUpAsync(db, userManager, logger);
        await RemoveEmptyRoundsExceptAsync(db, PlaceholderRoundName);

        if (squads is not null)
        {
            await SeedSquads.SeedAsync(db, userManager, environment, teams, squads, password, logger);
        }
        else
        {
            logger.LogWarning(
                "Ingen spillere å legge inn: Data/Squads/squads.json finnes ikke. Kjør " +
                "scripts/squads/fetch_squads.py -- appen har ingen oppdiktede spillere lenger.");
        }

        await ReportMissingGuardiansAsync(db, logger);

        // Succession planning: the coaches' own workbooks. See SeedSuccessionImport.
        if (successionImport is not null)
        {
            await SeedSuccessionImport.SeedAsync(db, userManager, successionCatalog, successionImport, logger);
        }
        else if (!await db.SuccessionAssessments.AnyAsync())
        {
            logger.LogWarning(
                "Succession er tom: {Path} finnes ikke, og basen har ingen vurderinger. Legg trenernes " +
                "importfil der og start appen på nytt. Se docs/succession-planning.md.",
                SeedSuccessionImport.PathIn(environment));
        }
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
    /// The three teams the project is about: G14, G15 and G17 -- the club's academy age groups,
    /// the same three the identity benchmarking has match data for (where they are still called
    /// U14, U15 and U17, as in the StatsBomb reports). And G19, which came with the real squads
    /// from ikstart.no (SeedSquads); the made-up squads have nobody on it.
    ///
    /// Existing rows are renamed in place rather than re-created: a new "G17" next to the old
    /// "U17" would leave every player on the old one, and the coach looking at an empty squad.
    /// The teams are named as the club names them, G for gutter; they were U14, U15, U17 and U19
    /// until 07.10.2026. Before that the demo teams were Senior, G19 and G16, until the club
    /// pointed out that they are not the teams this is for; strongest first, they became U17, U15
    /// and U14. That G19 was the made-up one, and is not renamed: G19 is now the club's real
    /// G19. "A-laget" is older still, from before the interface was English.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, Team>> SeedTeamsAsync(AppDbContext db, ILogger logger)
    {
        // One rename each, straight to the current name: the renames are saved together below,
        // so a chain (A-laget to Senior to U17 to G17) would find nothing to rename after its
        // first step.
        await RenameTeamAsync(db, "A-laget", "G17");
        await RenameTeamAsync(db, "Senior", "G17");
        await RenameTeamAsync(db, "U17", "G17");
        await RenameTeamAsync(db, "U15", "G15");
        await RenameTeamAsync(db, "U14", "G14");
        await RenameTeamAsync(db, "G16", "G14");
        await RenameTeamAsync(db, "U19", "G19");
        await TranslatePositionsAsync(db);
        await db.SaveChangesAsync();

        // The U names again, where they stand NEXT TO the G team rather than instead of it.
        await MergeTeamAsync(db, "U17", "G17", logger);
        await MergeTeamAsync(db, "U15", "G15", logger);
        await MergeTeamAsync(db, "U14", "G14", logger);
        await MergeTeamAsync(db, "U19", "G19", logger);

        // G19 kom til med de ekte troppene (SeedSquads). Uten dem står laget tomt.
        var names = new[] { "G14", "G15", "G17", "G19" };

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
    /// A team still under its old name when the new one exists as well. RenameTeamAsync leaves
    /// that alone -- there is nothing left to rename it to -- and so the coaches had "U17" to
    /// choose next to "G17", a second team with nobody on it: a build from before the rename,
    /// started against the shared database, had added the old names back.
    ///
    /// The old row goes. Anybody on it moves to the new team first, and a coach who had the old
    /// team has the new one, so nothing is lost if the old row was not empty after all.
    /// Runs every start, like the renames: an older build can add the names back again.
    /// </summary>
    private static async Task MergeTeamAsync(AppDbContext db, string oldName, string newName, ILogger logger)
    {
        var old = await db.Teams.FirstOrDefaultAsync(t => t.Name == oldName);
        var current = await db.Teams.FirstOrDefaultAsync(t => t.Name == newName);

        if (old is null || current is null)
        {
            return;
        }

        var players = await db.Players.Where(p => p.TeamId == old.Id).ToListAsync();
        foreach (var player in players)
        {
            player.TeamId = current.Id;
        }

        var links = await db.CoachTeams
            .Where(ct => ct.TeamId == old.Id || ct.TeamId == current.Id)
            .ToListAsync();

        var onCurrent = links.Where(ct => ct.TeamId == current.Id).Select(ct => ct.CoachUserId).ToHashSet();

        foreach (var link in links.Where(ct => ct.TeamId == old.Id))
        {
            // One row per coach and team: a coach already on the new team keeps that row.
            if (onCurrent.Add(link.CoachUserId))
            {
                link.TeamId = current.Id;
            }
            else
            {
                db.CoachTeams.Remove(link);
            }
        }

        // Saved before the team is removed: a team with players on it cannot be deleted.
        await db.SaveChangesAsync();

        db.Teams.Remove(old);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Laget {Old} sto ved siden av {New} og er fjernet. {Players} spillere flyttet til {New}.",
            oldName,
            newName,
            players.Count,
            newName);
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
    internal static async Task SeedRoundsAsync(AppDbContext db, ILogger logger)
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
            PlaceholderRoundName,
            now.AddDays(-7),
            now.AddDays(PlaceholderOpenDays));

        await db.SaveChangesAsync();

        await RemoveEmptyRoundsExceptAsync(db, PlaceholderRoundName);

        // Last, so it sees the list as it will actually be: the step above can take a
        // period away, and whether anything is left open is the whole question here.
        await KeepPlaceholderOpenAsync(db, PlaceholderRoundName, now, logger);
    }

    /// <summary>
    /// Keeps the placeholder period answerable.
    ///
    /// <see cref="EnsureRoundAsync"/> only ever creates. That is right for a period
    /// somebody defined -- a seed step has no business moving the window on a real
    /// measurement period -- but it left the placeholder to expire quietly. Seeded with a
    /// window of a few weeks, it closed itself a few weeks later, and nothing could reopen
    /// it: the name existed, so every later start skipped it. A database seeded in August
    /// had no open period in September, a form nobody could answer, and no way back short
    /// of the admin page or SQL.
    ///
    /// ONLY WHEN NOTHING ELSE IS OPEN. A club that has defined its own periods has
    /// finished with the placeholder, and a period closed from Admin/Periods was closed on
    /// purpose -- reopening it on the next start would undo that decision silently. With
    /// nothing open at all there is no decision to undo; there is only a form nobody can
    /// answer, which is the exact thing the placeholder exists to prevent.
    /// </summary>
    private static async Task KeepPlaceholderOpenAsync(
        AppDbContext db,
        string name,
        DateTimeOffset now,
        ILogger logger)
    {
        if (await db.SurveyRounds.AnyAsync(r => r.OpensAt <= now && r.ClosesAt >= now))
        {
            return;
        }

        var placeholder = await db.SurveyRounds.FirstOrDefaultAsync(r => r.Name == name);
        if (placeholder is null)
        {
            return;
        }

        var closedAt = placeholder.ClosesAt;

        // Both ends, not just the far one: a window that has not opened yet would stay
        // shut however far out its end was moved.
        if (placeholder.OpensAt > now)
        {
            placeholder.OpensAt = now;
        }

        placeholder.ClosesAt = now.AddDays(PlaceholderOpenDays);

        await db.SaveChangesAsync();

        logger.LogInformation(
            "No period was open, so the placeholder \"{Name}\" was reopened: it closed " +
            "{ClosedAt:d MMMM yyyy} and now closes {ClosesAt:d MMMM yyyy}. Define the real " +
            "periods in Admin/Periods and this stops happening.",
            placeholder.Name,
            closedAt,
            placeholder.ClosesAt);
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
    /// The admin and the one coach, with the coach on every team. Development logins, not
    /// people: the players come from the club's squads (<see cref="SeedSquads"/>).
    /// </summary>
    private static async Task SeedStaffAccountsAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IReadOnlyDictionary<string, Team> teams,
        string password)
    {
        await EnsureUserAsync(userManager, "admin@ikstart.example", password, Roles.Admin);

        // One coach, on every team. The coach role is not team-scoped any more -- CanViewPlayer
        // lets any coach see any player -- so a second account only added a login to remember.
        var coachId = await EnsureUserAsync(userManager, CoachEmail, password, Roles.Coach);

        foreach (var team in teams.Values)
        {
            if (!await db.CoachTeams.AnyAsync(ct => ct.CoachUserId == coachId && ct.TeamId == team.Id))
            {
                db.CoachTeams.Add(new CoachTeam { CoachUserId = coachId, TeamId = team.Id });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// DE OPPDIKTEDE TROPPENE appen hadde før klubbens egne, med navnene, kodene de het før det,
    /// og foresattkontoene. Ingenting her legges inn lenger. Lista står bare for å finne dem
    /// igjen og slette dem fra en base som ble seedet med dem (<see cref="RemoveMadeUpDataAsync"/>),
    /// og kan fjernes når den delte basen er ryddet.
    /// </summary>
    private static readonly IReadOnlyList<(string TeamName, IReadOnlyList<SquadMember> Squad)> Squads =
        new (string, IReadOnlyList<SquadMember>)[]
        {
            ("G17", new SquadMember[]
            {
                new("Kristian Espevik", "TS-98-07", new DateOnly(2009, 3, 11), ConsentLevel.Full),
                new("Victor Ziad", "TS-02-05", new DateOnly(2009, 5, 14), ConsentLevel.Full),
                new("Magnus Haugland", "TS-01-22", new DateOnly(2009, 11, 2), ConsentLevel.Aggregated),
                new("Henrik Tveit", "TS-99-18", new DateOnly(2009, 7, 23), ConsentLevel.Full),
                new("Elias Vatne", "TS-08-30", new DateOnly(2010, 3, 15), ConsentLevel.Full),
                new("Taavi-Topias Henell", "TS-00-13", new DateOnly(2009, 9, 17), ConsentLevel.Full),
                new("Jonas Aasland", "TS-03-06", new DateOnly(2010, 4, 30), ConsentLevel.Aggregated),
                new("Noah Berntsen", "TS-08-24", new DateOnly(2010, 5, 2), ConsentLevel.Full),
                new("Brage Kristoffersen", "TS-05-09", new DateOnly(2009, 6, 19), ConsentLevel.Full),
                new("Filip Salvesen", "TS-08-16", new DateOnly(2010, 9, 30), ConsentLevel.Full, GuardianEmail: "foresatt1@example.test"),
                new("Lucas Birkeland", "TS-09-21", new DateOnly(2010, 1, 27), ConsentLevel.Full)
            }),

            ("G15", new SquadMember[]
            {
                new("Sander Fjeld", "TS-07-21", new DateOnly(2011, 8, 9), ConsentLevel.Full),
                new("Mathias Lunde", "TS-07-14", new DateOnly(2011, 12, 1), ConsentLevel.Full, GuardianEmail: "foresatt2@example.test"),

                // Ingen egen konto, og samtykke None. Foresatt og trener har svart om hen;
                // spilleren selv kan ikke, og det skal se annerledes ut enn "har ikke svart".
                new("Tobias Moe", "TS-08-05", new DateOnly(2011, 2, 17), ConsentLevel.None, HasAccount: false,
                    GuardianEmail: "foresatt3@example.test"),

                new("Jakob Strand", "TS-08-27", new DateOnly(2011, 4, 25), ConsentLevel.Full),
                new("William Eide", "TS-07-09", new DateOnly(2011, 10, 30), ConsentLevel.Aggregated),
                new("Oskar Nygård", "TS-08-19", new DateOnly(2011, 7, 14), ConsentLevel.Full),
                new("Markus Dahl", "TS-07-03", new DateOnly(2011, 4, 5), ConsentLevel.Aggregated),
                new("Daniel Sørensen", "TS-08-02", new DateOnly(2012, 1, 19), ConsentLevel.Full),
                new("Martin Aune", "TS-07-26", new DateOnly(2011, 6, 22), ConsentLevel.Full),

                // Samtykket ble senere trukket ned fra Full til Aggregated i de oppdiktede dataene.
                new("Viljar Holm", "TS-08-11", new DateOnly(2011, 8, 22), ConsentLevel.Aggregated, GuardianEmail: "foresatt4@example.test"),

                new("Adrian Lie", "TS-08-14", new DateOnly(2012, 3, 8), ConsentLevel.Full)
            }),

            ("G14", new SquadMember[]
            {
                // Lillebroren til Tobias Moe på G15, med samme foresatte -- søsken i to lag
                // skal fungere.
                new("Emil Moe", "TS-10-02", new DateOnly(2012, 1, 14), ConsentLevel.Full, GuardianEmail: "foresatt3@example.test"),

                new("Isak Rønning", "TS-10-19", new DateOnly(2012, 4, 3), ConsentLevel.Full),
                new("Sebastian Olsen", "TS-10-25", new DateOnly(2012, 8, 11), ConsentLevel.Aggregated),
                new("Johannes Berg", "TS-11-07", new DateOnly(2012, 2, 26), ConsentLevel.Full),
                new("Aksel Vik", "TS-10-31", new DateOnly(2012, 11, 5), ConsentLevel.Full),
                new("Håkon Lien", "TS-11-16", new DateOnly(2012, 5, 19), ConsentLevel.Full),
                new("Theo Myhre", "TS-10-08", new DateOnly(2012, 5, 27), ConsentLevel.Aggregated, GuardianEmail: "foresatt5@example.test"),
                new("Leon Hagen", "TS-11-21", new DateOnly(2013, 7, 8), ConsentLevel.Full),
                new("Ludvig Bakke", "TS-11-04", new DateOnly(2012, 3, 9), ConsentLevel.None, GuardianEmail: "foresatt6@example.test"),

                // Ingen ConsentEvent i det hele tatt -- gjeldende nivå blir None. Det er en
                // egen tilstand fra "noen har aktivt satt None", og begge skal virke. Uten
                // konto, så det finnes heller ingen svar fra spilleren selv.
                new("Kasper Solberg", "TS-11-12", new DateOnly(2013, 10, 21), null, HasAccount: false,
                    GuardianEmail: "foresatt7@example.test"),

                new("Mikkel Tangen", "TS-10-14", new DateOnly(2012, 9, 30), ConsentLevel.Full)
            })
        };

    /// <summary>
    /// Koden hver seedet spiller het før navnene, slått opp på navnet. Se
    /// <see cref="SquadMember.FormerCode"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FormerCodeByName = Squads
        .SelectMany(team => team.Squad)
        .ToDictionary(member => member.Name, member => member.FormerCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Navnene i de oppdiktede troppene. <see cref="SeedSquads"/> sletter dem når de ekte er lest inn.</summary>
    internal static readonly IReadOnlySet<string> FictionalNames = Squads
        .SelectMany(team => team.Squad)
        .Select(member => member.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Kontoene de oppdiktede troppene har fått: spillerens egen, foresatt utledet av navnet, og
    /// de nummererte foresatt1..7.
    /// </summary>
    internal static IEnumerable<string> FictionalAccountEmails() => Squads
        .SelectMany(team => team.Squad)
        .SelectMany(member => new[] { PlayerEmail(member.Name), GuardianEmail(member.Name), member.GuardianEmail })
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Taavi-Topias Henell" blir "Taavi-Topias". Til velkomsten og draktene på beste elleve.</summary>
    internal static string FirstNameOf(string name) => name.Trim().Split(' ')[0];

    /// <summary>
    /// Spillerkontoen som hører til et navn: "Brage Kristoffersen" blir
    /// spiller.brage.kristoffersen@ikstart.example.
    /// </summary>
    internal static string PlayerEmail(string name) => $"spiller.{EmailPart(name)}@ikstart.example";

    /// <summary>
    /// Foresattkontoen som hører til et navn, for spillere uten en av de nummererte
    /// foresatt-kontoene. foresatt1..7@example.test er navngitt i README og i troppen over,
    /// og beholdes som de er.
    /// </summary>
    internal static string GuardianEmail(string name) => $"foresatt.{EmailPart(name)}@example.test";

    /// <summary>
    /// Et navn som delen foran @ i en adresse: små bokstaver, punktum mellom navnene, og æ, ø
    /// og å skrevet ut, så adressen kan tastes på et hvilket som helst tastatur. "Oskar
    /// Nygård" blir oskar.nygaard.
    /// </summary>
    private static string EmailPart(string name)
    {
        var part = new StringBuilder();

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            part.Append(character switch
            {
                'æ' => "ae",
                'ø' => "o",
                'å' => "aa",
                ' ' => ".",
                _ when char.IsAsciiLetterOrDigit(character) || character == '-' => character.ToString(),
                _ => string.Empty
            });
        }

        return part.ToString();
    }

    /// <summary>
    /// Én oppdiktet spiller: navnet, koden hen het før det, og en eventuell navngitt foresattkonto
    /// (foresatt1..7). Fødselsdato og samtykke står igjen fra da de ble lagt inn, og brukes ikke.
    /// </summary>
    private sealed record SquadMember(
        string Name,
        string FormerCode,
        DateOnly BirthDate,
        ConsentLevel? Consent,
        bool HasAccount = true,
        string? GuardianEmail = null);

    /// <summary>
    /// Hvor mange spillere under aldersgrensen som ikke har en foresatt registrert.
    ///
    /// Stoppet oppstarten før, da hver mindreårig fikk en oppdiktet foresatt i seedingen. Nå
    /// som bare ekte data legges inn, finnes ingen foresatte før klubben registrerer dem, og en
    /// app som ikke starter av den grunn, hjelper ingen. Regelen gjelder fortsatt -- loggen sier
    /// hvor mange som mangler, aldri hvem.
    /// </summary>
    private static async Task ReportMissingGuardiansAsync(AppDbContext db, ILogger logger)
    {
        var players = await db.Players
            .Include(p => p.Guardianships)
            .AsNoTracking()
            .ToListAsync();

        var missing = players.Count(p =>
            p.AgeAt(Today) < PlayerRules.GuardianRequiredBelowAge && p.Guardianships.Count == 0);

        if (missing > 0)
        {
            logger.LogWarning(
                "{Missing} av {PlayerCount} spillere er under {Age} år og har ingen foresatt registrert. " +
                "Foresatte legges inn av klubben; ingen er oppdiktet.",
                missing,
                players.Count,
                PlayerRules.GuardianRequiredBelowAge);
        }

        logger.LogInformation("Seeding fullført: {PlayerCount} spillere.", players.Count);
    }

    /// <summary>
    /// Fjerner alt seedingen diktet opp før de ekte dataene kom. Kjører ved hver oppstart i
    /// Development, og gjør ingenting når det ikke er noe igjen.
    ///
    ///   * De oppdiktede spillerne (<see cref="Squads"/>, under navnet eller koden de het før),
    ///     med alt som henger på dem, og kontoene deres.
    ///   * Alle foresatte: hver eneste var oppdiktet -- en konto per mindreårig, utledet av
    ///     navnet, på example.test. Koblingene og kontoene går.
    ///   * Samtykkene: de var satt av de oppdiktede foresatte, eller av seedingen. En
    ///     samtykkehendelse ingen har gitt, er verre enn ingen.
    ///   * 5C-svarene, de gamle ti-påstandssvarene og trenernes frigivelser fra før
    ///     <see cref="MadeUpBefore"/>: de er seedet, eller skrevet under testing.
    ///
    /// Samtykkeloggen og frigivelsene er append-only, og AppDbContext stopper sletting av dem.
    /// Her slettes de med ExecuteDelete, rett i databasen, og bare det som ble skrevet før
    /// <see cref="MadeUpBefore"/>: det er ikke en logg over noe som skjedde, men oppdiktede rader
    /// som utgir seg for å være det. Revisjonsloggen over oppslag (PlayerAccessEvent) røres ikke --
    /// den viser ekte oppslag, også de som ble gjort under testing.
    ///
    /// Loggen sier hvor mange, aldri hvem.
    /// </summary>
    private static async Task RemoveMadeUpDataAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        ILogger logger)
    {
        var members = Squads.SelectMany(team => team.Squad).ToList();
        var codes = members.Select(m => m.Name).Concat(members.Select(m => m.FormerCode)).ToList();

        var players = await db.Players.Where(p => codes.Contains(p.Code)).ToListAsync();
        db.Players.RemoveRange(players);
        await db.SaveChangesAsync();

        var submissions = await db.FiveCSubmissions.Where(f => f.SubmittedAt < MadeUpBefore).ExecuteDeleteAsync();
        var responses = await db.Responses.Where(r => r.SubmittedAt < MadeUpBefore).ExecuteDeleteAsync();
        var releases = await db.FeedbackReleases.Where(f => f.OccurredAt < MadeUpBefore).ExecuteDeleteAsync();
        var consents = await db.ConsentEvents.Where(c => c.OccurredAt < MadeUpBefore).ExecuteDeleteAsync();

        var guardianIds = (await userManager.GetUsersInRoleAsync(Roles.Guardian))
            .Where(u => u.Email?.EndsWith("@example.test", StringComparison.OrdinalIgnoreCase) == true)
            .Select(u => u.Id)
            .ToList();

        var guardianships = await db.Guardianships.Where(g => guardianIds.Contains(g.GuardianUserId)).ExecuteDeleteAsync();

        // The made-up players' own accounts, under the name and under the code they had before.
        var emails = FictionalAccountEmails()
            .Concat(members.SelectMany(m => new[]
            {
                $"spiller.{m.FormerCode.Replace("-", string.Empty).ToLowerInvariant()}@ikstart.example",
                $"foresatt.{m.FormerCode.Replace("-", string.Empty).ToLowerInvariant()}@example.test"
            }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var accounts = 0;
        var candidates = new List<IdentityUser>();

        foreach (var email in emails)
        {
            if (await userManager.FindByEmailAsync(email) is { } user)
            {
                candidates.Add(user);
            }
        }

        foreach (var id in guardianIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user && candidates.All(c => c.Id != user.Id))
            {
                candidates.Add(user);
            }
        }

        foreach (var user in candidates)
        {
            if (await db.Players.AnyAsync(p => p.UserId == user.Id)
                || await db.Guardianships.AnyAsync(g => g.GuardianUserId == user.Id))
            {
                continue;
            }

            // A name in the access log stays resolvable: the account is disabled, not deleted.
            if (await db.PlayerAccessEvents.AnyAsync(a => a.ViewedByUserId == user.Id)
                || await db.PlayerDeletionEvents.AnyAsync(d => d.DeletedByUserId == user.Id))
            {
                if (await userManager.GetLockoutEndDateAsync(user) != DateTimeOffset.MaxValue)
                {
                    foreach (var role in await userManager.GetRolesAsync(user))
                    {
                        await userManager.RemoveFromRoleAsync(user, role);
                    }

                    await userManager.SetLockoutEnabledAsync(user, true);
                    await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                    accounts++;
                }

                continue;
            }

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Klarte ikke å slette den oppdiktede kontoen {user.Email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            accounts++;
        }

        if (players.Count + submissions + responses + releases + consents + guardianships + accounts > 0)
        {
            logger.LogInformation(
                "Oppdiktede data fjernet: {Players} spillere, {Submissions} 5C-besvarelser, {Responses} eldre svar, " +
                "{Releases} frigivelser, {Consents} samtykker, {Guardianships} foresattkoblinger, {Accounts} kontoer.",
                players.Count,
                submissions,
                responses,
                releases,
                consents,
                guardianships,
                accounts);
        }
    }

    internal static async Task<string> EnsureUserAsync(
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
}
