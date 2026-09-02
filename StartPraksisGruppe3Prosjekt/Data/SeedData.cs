using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

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
        // used to return early once players existed, and the two coach accounts it needs to
        // fold together were seeded long before this step existed.
        await ConsolidateCoachAsync(db, userManager, logger);

        // The demo history. Development only, and separate from SeedRoundsAsync above --
        // that step keeps exactly one placeholder period in every environment, and these
        // two exist so that "over time" has something to draw.
        var demoPeriods = await SeedDemoPeriodsAsync(db);

        await SeedFiveCAnswersAsync(
            db,
            services.GetRequiredService<IQuestionCatalog>(),
            services.GetRequiredService<ISurveySubmissionStore>(),
            demoPeriods,
            logger);
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
    ///
    /// IDEMPOTENT PER SPILLER, ikke "hopp over alt hvis det finnes spillere". Den gamle
    /// vakten gjorde at en tropp aldri kunne fylles ut i en base som allerede var seedet --
    /// og den delte basen ER allerede seedet. Å tømme public-skjemaet i Supabase for å få se
    /// nye demospillere rammer alle på prosjektet; å legge til dem som mangler gjør det ikke.
    ///
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
        var adminId = await EnsureUserAsync(userManager, "admin@ikstart.example", password, Roles.Admin);

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

        // Tracked, and read once: the loop both looks players up and edits the ones it finds.
        var byCode = (await db.Players.ToListAsync())
            .ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        var created = 0;

        foreach (var (teamName, squad) in Squads)
        {
            if (!teams.TryGetValue(teamName, out var team))
            {
                logger.LogWarning("Hopper over troppen til {Team}: laget finnes ikke.", teamName);
                continue;
            }

            // Zipped against the formation rather than carrying a position per row: that is
            // what makes "one player per slot" true by construction instead of by proofreading.
            for (var slot = 0; slot < squad.Count; slot++)
            {
                var member = squad[slot];
                var position = Formation[slot];

                if (byCode.TryGetValue(member.Code, out var player))
                {
                    // Positions are seeded and never edited in the app, so this is the only
                    // writer and it can line an older squad up with the formation. Nothing
                    // else about an existing player is touched.
                    player.Position = position;
                    continue;
                }

                var userId = member.HasAccount
                    ? await EnsureUserAsync(userManager, PlayerEmail(member.Code), password, Roles.Player)
                    : null;

                player = new Player
                {
                    Code = member.Code,
                    TeamId = team.Id,
                    BirthDate = member.BirthDate,
                    Position = position,
                    UserId = userId
                };

                db.Players.Add(player);
                await db.SaveChangesAsync();

                byCode[member.Code] = player;
                created++;

                // A guardian where the club needs one: an explicitly named account, or one
                // derived from the code for anybody still under the age limit. Adults on the
                // senior side get none, which is the point of the rule.
                var guardianEmail = member.GuardianEmail
                    ?? (player.AgeAt(Today) < PlayerRules.GuardianRequiredBelowAge
                        ? GuardianEmail(member.Code)
                        : null);

                string? guardianUserId = null;

                if (guardianEmail is not null)
                {
                    guardianUserId = await EnsureUserAsync(
                        userManager, guardianEmail, password, Roles.Guardian);

                    db.Guardianships.Add(new Guardianship
                    {
                        PlayerId = player.Id,
                        GuardianUserId = guardianUserId
                    });
                }

                if (member.Consent is { } level)
                {
                    // Samtykket settes av en foresatt der det finnes en, ellers av spilleren selv.
                    db.ConsentEvents.Add(new ConsentEvent
                    {
                        PlayerId = player.Id,
                        Level = level,
                        ChangedByUserId = guardianUserId ?? userId ?? adminId,
                        OccurredAt = DateTimeOffset.UtcNow.AddDays(-30)
                    });
                }

                await db.SaveChangesAsync();
            }
        }

        await db.SaveChangesAsync();

        if (created > 0)
        {
            logger.LogInformation("La til {Count} oppdiktede spillere.", created);
        }

        await SeedWithdrawnConsentAsync(db);
        await AssertGuardianRuleAsync(db, logger);
    }

    /// <summary>
    /// Startelleveren i en 4-3-3, i draktrekkefølge. Hver tropp fylles til nøyaktig denne,
    /// slik at en lagside viser et helt lag -- og slik at det er noe å filtrere på når man
    /// søker på posisjon.
    /// </summary>
    private static readonly string[] Formation =
    {
        "Goalkeeper",
        "Right-back",
        "Centre-back",
        "Centre-back",
        "Left-back",
        "Defensive midfielder",
        "Central midfielder",
        "Attacking midfielder",
        "Right winger",
        "Striker",
        "Left winger"
    };

    /// <summary>
    /// Troppene. Elleve spillere per lag, i samme rekkefølge som <see cref="Formation"/>.
    ///
    /// Kodene til de tolv opprinnelige spillerne står urørt, og med dem særtilfellene de
    /// finnes for: en spiller uten samtykkehendelse i det hele tatt, en med tilbaketrukket
    /// samtykke, to søsken på samme foresatte, og et par uten egen konto. De er de eneste
    /// radene her som betyr noe utover å fylle en tropp.
    ///
    /// Fødselsdatoene er valgt slik at hvert lag har både myndige og mindreårige. Seniorlaget
    /// har fire spillere under aldersgrensen -- unge som er tatt opp fra akademiet -- og det
    /// er også det som gir laget nok foresatte til at et lagsnitt for den rollen kan vises.
    /// </summary>
    private static readonly IReadOnlyList<(string TeamName, IReadOnlyList<SquadMember> Squad)> Squads =
        new (string, IReadOnlyList<SquadMember>)[]
        {
            ("Senior", new SquadMember[]
            {
                new("TS-98-07", new DateOnly(1998, 3, 11), ConsentLevel.Full),
                new("TS-02-05", new DateOnly(2002, 5, 14), ConsentLevel.Full),
                new("TS-01-22", new DateOnly(2001, 11, 2), ConsentLevel.Aggregated),
                new("TS-99-18", new DateOnly(1999, 7, 23), ConsentLevel.Full),
                new("TS-08-30", new DateOnly(2008, 3, 15), ConsentLevel.Full),
                new("TS-00-13", new DateOnly(2000, 9, 17), ConsentLevel.Full),
                new("TS-03-06", new DateOnly(2003, 4, 30), ConsentLevel.Aggregated),
                new("TS-08-24", new DateOnly(2008, 5, 2), ConsentLevel.Full),
                new("TS-05-09", new DateOnly(2005, 6, 19), ConsentLevel.Full),
                new("TS-08-16", new DateOnly(2008, 9, 30), ConsentLevel.Full, GuardianEmail: "foresatt1@example.test"),
                new("TS-09-21", new DateOnly(2009, 1, 27), ConsentLevel.Full)
            }),

            ("G19", new SquadMember[]
            {
                new("TS-07-21", new DateOnly(2007, 8, 9), ConsentLevel.Full),
                new("TS-07-14", new DateOnly(2007, 12, 1), ConsentLevel.Full, GuardianEmail: "foresatt2@example.test"),

                // Ingen egen konto, og samtykke None. Foresatt og trener har svart om hen;
                // spilleren selv kan ikke, og det skal se annerledes ut enn "har ikke svart".
                new("TS-08-05", new DateOnly(2008, 2, 17), ConsentLevel.None, HasAccount: false,
                    GuardianEmail: "foresatt3@example.test"),

                new("TS-08-27", new DateOnly(2008, 4, 25), ConsentLevel.Full),
                new("TS-07-09", new DateOnly(2007, 10, 30), ConsentLevel.Aggregated),
                new("TS-08-19", new DateOnly(2008, 7, 14), ConsentLevel.Full),
                new("TS-07-03", new DateOnly(2007, 4, 5), ConsentLevel.Aggregated),
                new("TS-08-02", new DateOnly(2008, 1, 19), ConsentLevel.Full),
                new("TS-07-26", new DateOnly(2007, 6, 22), ConsentLevel.Full),

                // Samtykket ble senere trukket ned fra Full til Aggregated. Se SeedWithdrawnConsentAsync.
                new("TS-08-11", new DateOnly(2008, 8, 22), ConsentLevel.Aggregated, GuardianEmail: "foresatt4@example.test"),

                new("TS-08-14", new DateOnly(2008, 3, 8), ConsentLevel.Full)
            }),

            ("G16", new SquadMember[]
            {
                // Samme foresatte som TS-08-05 på G19 -- søsken i to lag skal fungere.
                new("TS-10-02", new DateOnly(2010, 1, 14), ConsentLevel.Full, GuardianEmail: "foresatt3@example.test"),

                new("TS-10-19", new DateOnly(2010, 4, 3), ConsentLevel.Full),
                new("TS-10-25", new DateOnly(2010, 8, 11), ConsentLevel.Aggregated),
                new("TS-11-07", new DateOnly(2011, 2, 26), ConsentLevel.Full),
                new("TS-10-31", new DateOnly(2010, 11, 5), ConsentLevel.Full),
                new("TS-11-16", new DateOnly(2011, 5, 19), ConsentLevel.Full),
                new("TS-10-08", new DateOnly(2010, 5, 27), ConsentLevel.Aggregated, GuardianEmail: "foresatt5@example.test"),
                new("TS-11-21", new DateOnly(2011, 7, 8), ConsentLevel.Full),
                new("TS-11-04", new DateOnly(2011, 3, 9), ConsentLevel.None, GuardianEmail: "foresatt6@example.test"),

                // Ingen ConsentEvent i det hele tatt -- gjeldende nivå blir None. Det er en
                // egen tilstand fra "noen har aktivt satt None", og begge skal virke. Uten
                // konto, så det finnes heller ingen svar fra spilleren selv.
                new("TS-11-12", new DateOnly(2011, 10, 21), null, HasAccount: false,
                    GuardianEmail: "foresatt7@example.test"),

                new("TS-10-14", new DateOnly(2010, 9, 30), ConsentLevel.Full)
            })
        };

    /// <summary>
    /// Spillerkontoen som hører til en kode: "TS-08-16" blir spiller.ts0816@ikstart.example.
    /// Samme regel som de fire kontoene som ble seedet for hånd tidligere, så de gjenkjennes
    /// og ingen må lære seg en ny innlogging.
    /// </summary>
    private static string PlayerEmail(string code) =>
        $"spiller.{code.Replace("-", string.Empty).ToLowerInvariant()}@ikstart.example";

    /// <summary>
    /// Foresattkontoen som hører til en kode, for spillere uten en av de nummererte
    /// foresatt-kontoene. foresatt1..7@example.test er navngitt i README og i troppen over,
    /// og beholdes som de er.
    /// </summary>
    private static string GuardianEmail(string code) =>
        $"foresatt.{code.Replace("-", string.Empty).ToLowerInvariant()}@example.test";

    /// <summary>
    /// Én spiller i en tropp. Posisjonen kommer fra plassen i <see cref="Formation"/>.
    /// </summary>
    /// <param name="Code">Klubbintern kode. Aldri navn.</param>
    /// <param name="BirthDate">Fødselsdato. Avgjør om det kreves foresatt.</param>
    /// <param name="Consent">Samtykkenivå, eller null for "ingen hendelse i det hele tatt".</param>
    /// <param name="HasAccount">Om spilleren har fått egen Identity-konto ennå.</param>
    /// <param name="GuardianEmail">
    /// En navngitt foresattkonto. Null betyr at en utledes av koden når spilleren er under
    /// aldersgrensen, og at det ikke opprettes noen når hen er myndig.
    /// </param>
    private sealed record SquadMember(
        string Code,
        DateOnly BirthDate,
        ConsentLevel? Consent,
        bool HasAccount = true,
        string? GuardianEmail = null);

    /// <summary>
    /// Én spiller får en historikk der samtykket først var Full og senere ble trukket ned
    /// til Aggregated. Den gamle raden blir stående — det er hele poenget med loggen.
    ///
    /// Idempotent: steget kjører ved hver oppstart nå som spillerseedingen ikke lenger
    /// stopper seg selv, og loggen er append-only. En hendelse til for hver omstart ville
    /// vært en historikk om omstarter, ikke om samtykke.
    /// </summary>
    private static async Task SeedWithdrawnConsentAsync(AppDbContext db)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Code == "TS-08-11");
        if (player is null)
        {
            return;
        }

        var alreadyWithdrawn = await db.ConsentEvents
            .AnyAsync(c => c.PlayerId == player.Id && c.Level == ConsentLevel.Full);

        if (alreadyWithdrawn)
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

    // ---------------------------------------------------------------------------------
    // Demohistorikk: perioder bakover i tid, og 5C-svar i dem.
    //
    // Uten dette er "over time" en tom side i utvikling. Det trengs minst to perioder med
    // svar før det finnes en retning å tegne, og det trengs flere spillere med svar i hver
    // periode før et lagsnitt kan vises i det hele tatt -- se CanViewTeamAggregateHandler.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// De to avsluttede periodene demodataene ligger i, pluss den åpne som allerede finnes.
    ///
    /// Ligger her og ikke i <see cref="SeedRoundsAsync"/> med vilje. Det steget kjører i alle
    /// miljøer og holder på én plassholderperiode; disse to er demodata og skal ikke finnes
    /// utenfor Development. At de likevel overlever <c>RemoveEmptyRoundsExceptAsync</c> ved
    /// neste oppstart, er fordi de har svar i seg -- en periode med svar blir stående.
    ///
    /// Datoene er relative, ikke faste: seedingen skal gi det samme bildet uansett når den
    /// kjøres, og en historikk som stopper i fjor er ikke en historikk.
    /// </summary>
    private static async Task<IReadOnlyList<SurveyRound>> SeedDemoPeriodsAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        await EnsureRoundAsync(db, $"Spring {now.Year}", now.AddDays(-210), now.AddDays(-150));
        await EnsureRoundAsync(db, $"Summer {now.Year}", now.AddDays(-120), now.AddDays(-60));

        await db.SaveChangesAsync();

        var names = new[] { $"Spring {now.Year}", $"Summer {now.Year}", $"Autumn {now.Year}" };

        var rounds = await db.SurveyRounds
            .Where(r => names.Contains(r.Name))
            .ToListAsync();

        return rounds.OrderBy(r => r.ClosesAt).ToList();
    }

    /// <summary>
    /// Oppdiktede 5C-besvarelser, spredt over periodene.
    ///
    /// Hva de er laget for å vise:
    ///   * Utvikling over tid. De fleste spillerne svarer i alle periodene, og hver spiller
    ///     har en egen retning -- de fleste opp, noen flatt, et par ned. En trend der alle
    ///     går samme vei beviser ingenting.
    ///   * Tre roller. Spiller, trener og minst én foresatt har svart om de fleste, slik at
    ///     avviksmålene og lagsnittene per rolle har noe å regne på.
    ///   * Ulike tidspunkter. Innsendingene ligger spredt utover hver periode, og rollene
    ///     svarer på ulike tidspunkt: spilleren tidlig, foresatt midtveis, treneren sist.
    ///
    /// Radene skrives rett på <see cref="AppDbContext"/> og ikke gjennom
    /// <see cref="ISurveySubmissionStore"/>. Formen er den samme
    /// <see cref="Services.FiveC.EfSurveySubmissionStore"/> skriver, men lageret gjør ett
    /// oppslag og én lagring per besvarelse, og det er noen hundre rundturer til en database
    /// som ikke ligger på denne maskinen. Derfor sjekkes det først at det ER det lageret som
    /// er i bruk -- svar skrevet et sted ingen leser fra er verre enn ingen svar.
    /// </summary>
    private static async Task SeedFiveCAnswersAsync(
        AppDbContext db,
        IQuestionCatalog catalog,
        ISurveySubmissionStore store,
        IReadOnlyList<SurveyRound> periods,
        ILogger logger)
    {
        if (periods.Count == 0)
        {
            return;
        }

        if (store is not Services.FiveC.EfSurveySubmissionStore)
        {
            logger.LogInformation(
                "Hopper over oppdiktede 5C-svar: svarene leses fra {Store}, og seedingen " +
                "skriver til appens egen database.",
                store.Description);
            return;
        }

        var roundIds = periods.Select(r => r.Id).ToList();

        // Hva som allerede ligger der. Steget kjører ved hver oppstart, og unique-indeksen
        // på (runde, spiller, respondent) ville stoppet det -- men å la den gjøre jobben
        // ville betydd en exception i stedet for et hopp over.
        var existing = (await db.FiveCSubmissions
                .AsNoTracking()
                .Where(s => roundIds.Contains(s.RoundId))
                .Select(s => new { s.RoundId, s.PlayerId, s.RespondentUserId })
                .ToListAsync())
            .Select(s => (s.RoundId, s.PlayerId, s.RespondentUserId))
            .ToHashSet();

        var players = await db.Players
            .AsNoTracking()
            .Include(p => p.Guardianships)
            .ToListAsync();

        var coachByTeam = (await db.CoachTeams.AsNoTracking().ToListAsync())
            .GroupBy(ct => ct.TeamId)
            .ToDictionary(group => group.Key, group => group.First().CoachUserId);

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var player in players)
        {
            // Én tilfeldighetskilde per spiller, sådd fra koden. Samme kode gir samme
            // spiller hver gang, så to kjøringer av seedingen gir det samme bildet og
            // "endret tallene seg?" er et spørsmål som kan besvares.
            var profile = ProfileFor(new Random(StableSeed(player.Code)), catalog, periods.Count);

            var guardianUserId = player.Guardianships.FirstOrDefault()?.GuardianUserId;
            coachByTeam.TryGetValue(player.TeamId, out var coachUserId);

            var beforePlayer = added;

            for (var index = 0; index < periods.Count; index++)
            {
                if (index < profile.JoinedAtPeriod)
                {
                    // Kom til klubben senere. En spiller uten svar i den første perioden er
                    // et hull i linja, og hullet skal finnes i testdataene.
                    continue;
                }

                var period = periods[index];
                var step = index - profile.JoinedAtPeriod;

                // En egen kilde per periode, og en til per besvarelse inne i den. De henger
                // ikke sammen, og det er nettopp poenget: en besvarelse som allerede finnes
                // og hoppes over, flytter da ikke på hva den neste ville blitt. Med én felles
                // kilde gjorde den det -- og andre oppstart la til svar som ikke fantes i
                // den første, hver gang, helt til alt var fylt ut.
                var forPeriod = new Random(StableSeed($"{player.Code}|{index}"));

                // Spilleren selv. Uten konto finnes det ingen respondent-ID, og da er det
                // ingen som har svart -- ikke en anonym besvarelse.
                if (player.UserId is { } playerUserId && forPeriod.NextDouble() > 0.08)
                {
                    added += AddSubmission(
                        db, catalog, existing, period, index, player, playerUserId,
                        RespondentType.Player, profile, step, 0, now);
                }

                if (guardianUserId is not null && forPeriod.NextDouble() > 0.25)
                {
                    added += AddSubmission(
                        db, catalog, existing, period, index, player, guardianUserId,
                        RespondentType.Guardian, profile, step, profile.GuardianBias, now);
                }

                if (coachUserId is not null && forPeriod.NextDouble() > 0.12)
                {
                    added += AddSubmission(
                        db, catalog, existing, period, index, player, coachUserId,
                        RespondentType.Coach, profile, step, profile.CoachBias, now);
                }
            }

            // Lagres per spiller, ikke som én bunke til slutt. Hele troppen på én gang er et
            // par tusen sporede entiteter, og endringssporeren blir merkbart treg lenge før
            // det er noe som helst vunnet på å vente.
            if (added > beforePlayer)
            {
                await db.SaveChangesAsync();
            }
        }

        if (added == 0)
        {
            return;
        }

        logger.LogInformation(
            "La til {Count} oppdiktede 5C-besvarelser over {Periods} perioder: {Names}.",
            added,
            periods.Count,
            string.Join(", ", periods.Select(p => p.Name)));
    }

    /// <summary>
    /// Legger til én besvarelse hvis den ikke finnes fra før. Returnerer 1 hvis den ble lagt
    /// til, ellers 0, slik at telleren over er en telling av det som faktisk ble skrevet.
    /// </summary>
    private static int AddSubmission(
        AppDbContext db,
        IQuestionCatalog catalog,
        HashSet<(int RoundId, int PlayerId, string RespondentUserId)> existing,
        SurveyRound period,
        int periodIndex,
        Player player,
        string respondentUserId,
        RespondentType role,
        AnswerProfile profile,
        int step,
        double bias,
        DateTimeOffset now)
    {
        var key = (period.Id, player.Id, respondentUserId);

        if (!existing.Add(key))
        {
            return 0;
        }

        // Sådd fra spiller, periode og rolle, ikke ført videre fra forrige besvarelse. Den
        // samme besvarelsen får da de samme svarene uansett hva som ble skrevet før den, og
        // periodens nummer brukes framfor rundens ID fordi ID-en er ulik fra base til base.
        var random = new Random(StableSeed($"{player.Code}|{periodIndex}|{role}"));

        var answers = new List<FiveCAnswer>();

        foreach (var category in catalog.Questions.Categories)
        {
            // Der spilleren startet på denne C-en, pluss det de har flyttet seg siden, pluss
            // det denne rollen systematisk legger til eller trekker fra.
            var target = profile.StartByCategory[category.Key] + profile.DriftPerPeriod * step + bias;

            foreach (var question in category.Questions)
            {
                answers.Add(new FiveCAnswer
                {
                    QuestionKey = question.Key,
                    CategoryKey = category.Key,

                    // Noen påstander står ubesvart. Det er noe folk faktisk gjør, og det er
                    // det ene tilfellet som viser at null ikke behandles som 3.
                    Value = random.NextDouble() < 0.04
                        ? null
                        : RawAnswerFor(target, question.Reversed, random)
                });
            }
        }

        db.FiveCSubmissions.Add(new FiveCSubmission
        {
            RoundId = period.Id,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = Contracts.FiveC.SurveySubmission.Roles.From(role),
            RespondentUserId = respondentUserId,
            QuestionSetVersion = catalog.Questions.Version,
            SubmittedAt = SubmittedIn(period, now, random, role),
            Answers = answers
        });

        return 1;
    }

    /// <summary>
    /// Ett svar på skalaen, RÅTT slik det ville blitt lagret fra skjemaet.
    ///
    /// Målet er en SKÅR -- der høyt alltid er bra. På en negativt formulert påstand er råsvaret
    /// derfor det speilvendte, (6 - skår), for det er den veien
    /// <see cref="FiveCRules.Score"/> leser den tilbake. Uten dette ville hver reversert
    /// påstand i testdataene pekt motsatt vei av resten.
    /// </summary>
    private static int RawAnswerFor(double targetScore, bool reversed, Random random)
    {
        var jittered = targetScore + (random.NextDouble() - 0.5);

        var score = Math.Clamp(
            (int)Math.Round(jittered, MidpointRounding.AwayFromZero),
            FiveCRules.ScaleMin,
            FiveCRules.ScaleMax);

        return reversed ? PlayerRules.ReverseScoreBase - score : score;
    }

    /// <summary>
    /// Når i perioden besvarelsen ble sendt inn.
    ///
    /// Rollene lander på ulike steder i vinduet: spilleren tidlig, foresatt midtveis,
    /// treneren sist -- som er både realistisk og det som sprer tidsstemplene ut over uker i
    /// stedet for å gi alle samme klokkeslett. En åpen periode klippes mot nå: en besvarelse
    /// datert fram i tid er ikke en besvarelse.
    /// </summary>
    private static DateTimeOffset SubmittedIn(
        SurveyRound period,
        DateTimeOffset now,
        Random random,
        RespondentType role)
    {
        var opens = period.OpensAt;
        var closes = period.ClosesAt < now ? period.ClosesAt : now;
        var window = closes - opens;

        if (window <= TimeSpan.Zero)
        {
            return closes;
        }

        var start = role switch
        {
            RespondentType.Player => 0.05,
            RespondentType.Guardian => 0.35,
            _ => 0.60
        };

        var fraction = Math.Clamp(start + random.NextDouble() * 0.3, 0.01, 0.99);

        return opens + window * fraction;
    }

    /// <summary>
    /// Én oppdiktet spillers form: hvor de starter på hver C, hvilken vei de går, og hvordan
    /// de voksne rundt dem svarer i forhold til dem selv.
    /// </summary>
    /// <param name="StartByCategory">Startnivå per C, på 1-5-skalaen.</param>
    /// <param name="DriftPerPeriod">Hva de flytter seg per periode. Kan være negativt.</param>
    /// <param name="CoachBias">Hvor mye høyere eller lavere treneren svarer.</param>
    /// <param name="GuardianBias">Det samme for foresatt. Sjelden negativt.</param>
    /// <param name="JoinedAtPeriod">Første periode spilleren har svar i.</param>
    private sealed record AnswerProfile(
        IReadOnlyDictionary<string, double> StartByCategory,
        double DriftPerPeriod,
        double CoachBias,
        double GuardianBias,
        int JoinedAtPeriod);

    /// <summary>
    /// Trekker en spillerform. Spennet er valgt slik at troppen inneholder både noen som
    /// ligger lavt nok til å bli flagget for oppfølging, og noen som går nedover -- en
    /// tropp der alle er middels og alle går oppover ville ikke testet noe av visningen.
    /// </summary>
    private static AnswerProfile ProfileFor(Random random, IQuestionCatalog catalog, int periodCount)
    {
        // 2,0 til 4,2. Nederst i spennet havner en spiller under terskelen for oppfølging
        // på minst én C, som er tilfellet visningen har en egen farge for.
        var talent = 2.0 + random.NextDouble() * 2.2;

        var startByCategory = catalog.Questions.Categories.ToDictionary(
            category => category.Key,
            _ => Math.Clamp(talent + (random.NextDouble() - 0.5) * 1.4, 1.0, 5.0));

        return new AnswerProfile(
            StartByCategory: startByCategory,
            // Fire av fem går oppover. Den femte gjør det ikke, og skal ikke gjøre det.
            DriftPerPeriod: random.NextDouble() < 0.2
                ? -0.15 - random.NextDouble() * 0.35
                : 0.10 + random.NextDouble() * 0.45,
            // Treneren ser stort sett litt strengere på det enn spilleren selv.
            CoachBias: -0.5 + random.NextDouble() * 0.9,
            // Foresatt ser stort sett litt mildere på det.
            GuardianBias: random.NextDouble() * 0.8,
            // De fleste har vært der hele veien. Noen kom til underveis.
            JoinedAtPeriod: random.NextDouble() < 0.18 ? Math.Min(1, periodCount - 1) : 0);
    }

    /// <summary>
    /// En stabil hash av spillerkoden, brukt som frø.
    ///
    /// <see cref="string.GetHashCode()"/> er randomisert per prosess i .NET, så den ville gitt
    /// nye tall for de samme spillerne ved hver kjøring. FNV-1a er ikke det -- og poenget her
    /// er nettopp at demodataene skal se like ut i morgen.
    /// </summary>
    private static int StableSeed(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            var hash = offsetBasis;

            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
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
}
