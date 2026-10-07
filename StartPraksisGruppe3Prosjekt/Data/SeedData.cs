using System.Text;
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
/// ALLE DATA HER ER OPPDIKTET, og ekte spillerdata skal ikke inn i dette repoet: det er
/// offentlig. Spillernavnene er tilfeldige, bortsett fra prosjektgruppas egne fire (se
/// <see cref="Squads"/>). Ingen telefonnumre eller e-postadresser til virkelige personer —
/// bruk example-domener.
///
/// De ekte troppene fra ikstart.no ligger utenfor repoet, i den git-ignorerte Data/Squads/, og
/// erstatter de oppdiktede når de er hentet. Se <see cref="SeedSquads"/>.
///
/// Seedingen er idempotent: hvert steg hopper over seg selv hvis dataene finnes.
///
/// BARE I DEVELOPMENT, og bare mot en database som er markert som utvikling. Skjemaet,
/// markeringen og grunnoppsettet (roller og lag, se <see cref="BaseSetup"/>) er på plass før
/// denne klassen kalles; alt her er demodata. Kontoene får et passord som står i denne fila,
/// og skal aldri finnes i en database med ekte spillerdata -- se <see cref="DatabaseGuard"/>.
/// </summary>
public static class SeedData
{
    /// <summary>Passord for demokontoene. Overstyres med Seed:DevPassword i user-secrets.</summary>
    private const string DefaultDevPassword = "Dev!passord1";

    /// <summary>The one coach account. Kept as-is so nobody has to relearn a login.</summary>
    internal const string CoachEmail = "trener.senior@ikstart.example";

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

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeedData));

        // Program.cs kaller bare hit i Development, og vernet har alt sjekket markeringen. Den
        // sjekkes en gang til her, fordi det er DENNE koden som lager kontoer med et kjent
        // passord: den skal ikke kunne gjøre det i en driftsdatabase, uansett hvem som kaller.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "SeedData.InitializeAsync lager demokontoer og oppdiktede data, og kjøres bare i Development.");
        }

        await DatabaseGuard.RequireMarkedAsAsync(db, DatabaseMarker.Development);

        // Lagene er grunnoppsett og finnes fra før. Se BaseSetup.
        var teams = await db.Teams.ToDictionaryAsync(t => t.Name, t => t);

        await SeedRoundsAsync(db, logger);

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var password = configuration["Seed:DevPassword"] ?? DefaultDevPassword;

        // De ekte troppene, når de er hentet. Null uten fila -- se SeedSquads.
        var squads = SeedSquads.Load(environment);

        // De oppdiktede troppene bare i en base som ikke har andre spillere. Med de ekte troppene
        // i fila erstattes de; uten fila, men med andre spillere i basen -- lagt inn med
        // import-players -- ville de ellers kommet tilbake ved siden av dem.
        var seedFictionalSquads = squads is null && !await HasOtherPlayersAsync(db);

        await SeedUsersAndPlayersAsync(db, userManager, teams, password, seedFictionalSquads, logger);

        if (squads is not null)
        {
            await SeedSquads.SeedAsync(db, userManager, environment, teams, squads, password, logger);
        }

        await AssertGuardianRuleAsync(db, logger);

        // The demo history: two closed periods behind the open one, so that "over time" has
        // something to draw.
        var demoPeriods = await SeedDemoPeriodsAsync(db);

        await SeedFiveCAnswersAsync(
            db,
            services.GetRequiredService<IQuestionCatalog>(),
            services.GetRequiredService<ISurveySubmissionStore>(),
            demoPeriods,
            logger);

        // Oppdiktede fornavn til velkomsten når en spiller logger inn. Se SeedWelcome.
        await SeedWelcome.SeedAsync(db, userManager, logger);

        // Succession planning: three coaches' ratings over three cycles. Its own file, since
        // none of it touches anything above. See SeedSuccession.
        await SeedSuccession.SeedAsync(
            db,
            userManager,
            services.GetRequiredService<Services.Succession.ISuccessionCatalog>(),
            password,
            logger);
    }

    /// <summary>
    /// The demo period that is open: "Autumn" of this year. Development only, like everything
    /// else here -- a database in operation gets its periods from an administrator, on
    /// Admin/Periods, and from nowhere else.
    ///
    /// Added if it is missing and otherwise left alone. Nothing here renames or removes a
    /// period: a period somebody created on Admin/Periods is theirs, empty or not.
    /// </summary>
    private static async Task SeedRoundsAsync(AppDbContext db, ILogger logger)
    {
        var now = DateTimeOffset.UtcNow;

        await EnsureRoundAsync(
            db,
            $"Autumn {now.Year}",
            now.AddDays(-7),
            now.AddDays(PlaceholderOpenDays));

        await db.SaveChangesAsync();

        await KeepPlaceholderOpenAsync(db, $"Autumn {now.Year}", now, logger);
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
    /// ONLY WHEN NOTHING ELSE IS OPEN. Somebody who has defined their own periods has
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
    /// IDEMPOTENT PER SPILLER, ikke "hopp over alt hvis det finnes spillere". En tropp skal
    /// kunne fylles ut i en base som allerede er seedet, uten at den må slettes først.
    ///
    /// Regelen som håndheves til slutt: hver spiller under
    /// <see cref="PlayerRules.GuardianRequiredBelowAge"/> år må ha minst én foresatt.
    /// </summary>
    private static async Task SeedUsersAndPlayersAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IReadOnlyDictionary<string, Team> teams,
        string password,
        bool seedFictionalSquads,
        ILogger logger)
    {
        var adminId = await EnsureUserAsync(userManager, "admin@ikstart.example", password, Roles.Admin);

        // One coach, for every team. The coach role is not team-scoped -- CanViewPlayer lets
        // any coach see any player -- so a second account would only add a login to remember.
        await EnsureUserAsync(userManager, CoachEmail, password, Roles.Coach);

        if (!seedFictionalSquads)
        {
            logger.LogInformation("Hopper over de oppdiktede troppene: basen har de ekte.");
            return;
        }

        // Tracked, and read once: the loop both looks players up and edits the ones it finds.
        var byCode = (await db.Players.ToListAsync())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

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

                if (byCode.TryGetValue(member.Name, out var player))
                {
                    // Positions are seeded and never edited in the app, so this is the only
                    // writer and it can line an older squad up with the formation.
                    player.Position = position;

                    // And the birth date, since the squads became G14, G15 and G17: an
                    // under-17 born in 1998 is not one. A player who is a minor now and was
                    // not before gets the guardian every minor has -- without one the check
                    // at the end of seeding stops the application.
                    if (player.BirthDate != member.BirthDate)
                    {
                        player.BirthDate = member.BirthDate;

                        if (player.AgeAt(Today) < PlayerRules.GuardianRequiredBelowAge
                            && !await db.Guardianships.AnyAsync(g => g.PlayerId == player.Id))
                        {
                            await AddGuardianAsync(db, userManager, player, member, password);
                        }
                    }

                    continue;
                }

                var userId = member.HasAccount
                    ? await EnsureUserAsync(userManager, PlayerEmail(member.Name), password, Roles.Player)
                    : null;

                player = new Player
                {
                    Name = member.Name,
                    TeamId = team.Id,
                    BirthDate = member.BirthDate,
                    Position = position,
                    UserId = userId
                };

                db.Players.Add(player);
                await db.SaveChangesAsync();

                byCode[member.Name] = player;
                created++;

                var guardianUserId = await AddGuardianAsync(db, userManager, player, member, password);

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
    }

    /// <summary>
    /// Om basen har spillere som ikke er fra de oppdiktede troppene -- de ekte fra
    /// <see cref="SeedSquads"/>, eller en admin har lagt inn. Spillere som fortsatt heter koden
    /// sin (TS-08-16), er oppdiktede og teller ikke.
    /// </summary>
    private static async Task<bool> HasOtherPlayersAsync(AppDbContext db)
    {
        var fictional = FictionalNames.Concat(FormerCodeByName.Values).ToList();

        return await db.Players.AnyAsync(p => !fictional.Contains(p.Name));
    }

    /// <summary>
    /// A guardian where the club needs one: an explicitly named account, or one derived from
    /// the name for anybody under the age limit -- which, in G14, G15 and G17, is everybody.
    /// Null when the player needs none. Added, not saved.
    /// </summary>
    private static async Task<string?> AddGuardianAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        Player player,
        SquadMember member,
        string password)
    {
        var guardianEmail = member.GuardianEmail
            ?? (player.AgeAt(Today) < PlayerRules.GuardianRequiredBelowAge
                ? GuardianEmail(member.Name)
                : null);

        if (guardianEmail is null)
        {
            return null;
        }

        var guardianUserId = await EnsureUserAsync(userManager, guardianEmail, password, Roles.Guardian);

        db.Guardianships.Add(new Guardianship
        {
            PlayerId = player.Id,
            GuardianUserId = guardianUserId
        });

        return guardianUserId;
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
    /// Lagene er G14, G15 og G17, de tre prosjektet gjelder.
    ///
    /// Navnene er tilfeldige og oppdiktet, med ett unntak: prosjektgruppa -- Brage
    /// Kristoffersen, Kristian Espevik, Victor Ziad og Taavi-Topias Henell -- spiller på
    /// G17. Ingen fornavn går igjen i klubben, så en drakt på beste elleve aldri
    /// trenger mer enn fornavnet. De to som deler etternavn, er søsknene nedenfor.
    ///
    /// Særtilfellene er de samme som da spillerne het koder: en spiller uten samtykkehendelse
    /// i det hele tatt, en med tilbaketrukket samtykke, to søsken på samme foresatte, og et par
    /// uten egen konto. De er de eneste radene her som betyr noe utover å fylle en tropp.
    ///
    /// Fødselsdatoene følger årsklassene slik NFF regner dem, etter året spilleren fyller:
    /// G17 er født 2009 og 2010, G15 2011 og G14 2012, med et par som er et år yngre og spiller
    /// opp. Alle er dermed mindreårige, og alle har en foresatt -- som i virkeligheten for disse
    /// lagene. Koden ved siden av navnet (TS-98-07) er det spilleren het før navnene; i dag er
    /// den bare frøet til demodataene.
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

                // Samtykket ble senere trukket ned fra Full til Aggregated. Se SeedWithdrawnConsentAsync.
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
    /// Om en spiller med dette navnet er en av de oppdiktede: navnet står i troppene over, eller
    /// det er koden en av dem het før navnene. export-players holder dem utenfor eksporten.
    /// </summary>
    internal static bool IsFictional(string name) =>
        FictionalNames.Contains(name)
        || FormerCodeByName.Values.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Kontoene de oppdiktede troppene har fått: spillerens egen, foresatt utledet av navnet, og
    /// de nummererte foresatt1..7.
    /// </summary>
    internal static IEnumerable<string> FictionalAccountEmails() => Squads
        .SelectMany(team => team.Squad)
        .SelectMany(member => new[] { PlayerEmail(member.Name), GuardianEmail(member.Name), member.GuardianEmail })
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Det demodataene til en spiller trekkes fra: koden spilleren het før navnene, eller
    /// navnet for en spiller som aldri hadde kode. Slik gir samme spiller de samme svarene og
    /// vurderingene i en ny base som i en som ble seedet før navnene -- og en base som ble
    /// seedet før, får ikke nye svar fra et annet frø ved siden av de gamle.
    /// </summary>
    internal static string SeedKey(Player player) =>
        FormerCodeByName.TryGetValue(player.Name, out var former) ? former : player.Name;

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
    /// Én spiller i en tropp. Posisjonen kommer fra plassen i <see cref="Formation"/>.
    /// </summary>
    /// <param name="Name">
    /// Fornavn og etternavn, slik spilleren står i appen. Lagres i <see cref="Player.Name"/>,
    /// og er derfor høyst 50 tegn.
    /// </param>
    /// <param name="FormerCode">
    /// Koden spilleren het før navnene, f.eks. "TS-08-16". Den er frøet demodataene trekkes
    /// fra (se <see cref="SeedKey"/>), så samme spiller får de samme svarene og vurderingene.
    /// </param>
    /// <param name="BirthDate">Fødselsdato. Avgjør om det kreves foresatt.</param>
    /// <param name="Consent">Samtykkenivå, eller null for "ingen hendelse i det hele tatt".</param>
    /// <param name="HasAccount">Om spilleren har fått egen Identity-konto ennå.</param>
    /// <param name="GuardianEmail">
    /// En navngitt foresattkonto. Null betyr at en utledes av navnet når spilleren er under
    /// aldersgrensen, og at det ikke opprettes noen når hen er myndig.
    /// </param>
    private sealed record SquadMember(
        string Name,
        string FormerCode,
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
        var player = await db.Players.FirstOrDefaultAsync(p => p.Name == "Viljar Holm");
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
            .Select(p => p.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Seed-data bryter regelen om foresatt for mindreårige. Mangler foresatt: " +
                string.Join(", ", missing));
        }

        logger.LogInformation("Seeding fullført: {PlayerCount} spillere.", players.Count);
    }

    // ---------------------------------------------------------------------------------
    // Demohistorikk: perioder bakover i tid, og 5C-svar i dem.
    //
    // Uten dette er "over time" en tom side i utvikling. Det trengs minst to perioder med
    // svar før det finnes en retning å tegne, og det trengs flere spillere med svar i hver
    // periode før et lagsnitt kan vises i det hele tatt -- se CanViewTeamAggregateHandler.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// De to avsluttede periodene demodataene ligger i, pluss den åpne fra
    /// <see cref="SeedRoundsAsync"/>.
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
    /// oppslag og én lagring per besvarelse, og det er noen hundre rundturer til databasen.
    /// Derfor sjekkes det først at det ER det lageret som er i bruk -- svar skrevet et sted
    /// ingen leser fra er verre enn ingen svar.
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

        // The one coach answers about every team. See SeedUsersAndPlayersAsync.
        var normalizedCoachEmail = CoachEmail.ToUpperInvariant();

        var coachUserId = await db.Users
            .Where(u => u.NormalizedEmail == normalizedCoachEmail)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var player in players)
        {
            // Én tilfeldighetskilde per spiller, sådd fra SeedKey. Samme spiller gir samme
            // svar hver gang, så to kjøringer av seedingen gir det samme bildet og
            // "endret tallene seg?" er et spørsmål som kan besvares.
            var profile = ProfileFor(new Random(StableSeed(SeedKey(player))), catalog, periods.Count);

            var guardianUserId = player.Guardianships.FirstOrDefault()?.GuardianUserId;

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
                var forPeriod = new Random(StableSeed($"{SeedKey(player)}|{index}"));

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
        var random = new Random(StableSeed($"{SeedKey(player)}|{periodIndex}|{role}"));

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
    internal static int StableSeed(string value)
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
