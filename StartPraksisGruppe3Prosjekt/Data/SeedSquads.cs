using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// De ekte troppene til G14, G15, G17 og G19, for Development: navn, posisjon, fødselsdato og bilde
/// fra klubbens egne spillersider på ikstart.no. IK Start har gitt tillatelse til at navnene og
/// bildene brukes i appen.
///
/// INGENTING AV DET STÅR I REPOET. Fila og bildene ligger i Data/Squads/, som er git-ignorert:
/// repoet er offentlig, og nesten alle spillerne er mindreårige. De hentes med
/// scripts/squads/fetch_squads.py. Mangler fila, gjør denne klassen ingenting, og seedingen er
/// som før.
///
/// Når fila finnes, ERSTATTER troppene de oppdiktede:
///   * Hver spiller i fila får en spillerrad, en konto utledet av navnet
///     (spiller.leon.enger@ikstart.example), og fornavn og bilde til velkomsten. Spillere under
///     PlayerRules.GuardianRequiredBelowAge får en foresatt, oppdiktet som før; de eldste på
///     G19 er over grensen og får ingen.
///   * De oppdiktede spillerne i SeedData.Squads slettes, med alt som henger på dem, og
///     kontoene deres. Det skjer etter at de ekte er lagt inn, så en oppstart som stopper
///     halvveis aldri etterlater et lag uten spillere.
///
/// Idempotent per spiller, som resten av seedingen. En spiller som finnes, får lag, posisjon og
/// fødselsdato fra fila -- en spiller som har rykket opp, flytter med. Fornavn og bilde legges
/// inn der det mangler, men erstattes ikke: det admin har lagt inn på /Admin/Players, er ikke
/// seedingens å endre. Vil man ha et nytt bilde fra klubben, fjernes det gamle der først.
///
/// Loggen sier hvor mange, aldri hvem.
///
/// Lesingen (<see cref="Load(string)"/>) og innleggingen (<see cref="ApplyAsync"/>) deles med
/// engangskommandoene export-players og import-players -- se <see cref="PlayerTransfer"/>. De
/// flytter de samme spillerne mellom to databaser, og skal ha nøyaktig samme validering og
/// bildekontroll som seedingen.
/// </summary>
internal static class SeedSquads
{
    internal const int SchemaVersion = 1;

    internal static readonly string[] TeamNames = BaseSetup.TeamNames;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string HowToRecreate =
        "Lag fila på nytt: scripts/squads/fetch_squads.py for troppene fra ikstart.no, " +
        "eller export-players for en eksport.";

    /// <summary>Data/Squads under appens rotmappe. Fila og bildene ligger der.</summary>
    internal static string FolderIn(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "Data", "Squads");

    /// <summary>
    /// Leser troppene. Null når fila ikke finnes -- da er det ingen ekte tropper, og de oppdiktede
    /// gjelder. Finnes fila, men henger ikke sammen, stopper oppstarten: en halvlest tropp ville
    /// ellers erstattet de oppdiktede med noe som mangler spillere.
    /// </summary>
    internal static SquadFile? Load(IHostEnvironment environment) => Load(FolderIn(environment));

    /// <summary>
    /// Det samme for en hvilken som helst mappe med squads.json og bildene ved siden av.
    /// import-players leser en eksport herfra.
    /// </summary>
    internal static SquadFile? Load(string folder)
    {
        var path = Path.Combine(folder, "squads.json");

        if (!File.Exists(path))
        {
            return null;
        }

        SquadFile? squads;

        try
        {
            squads = JsonSerializer.Deserialize<SquadFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{path} kan ikke leses: {ex.Message} {HowToRecreate}", ex);
        }

        var problems = new List<string>();

        if (squads is null)
        {
            problems.Add("fila er tom");
        }
        else
        {
            if (squads.SchemaVersion != SchemaVersion)
            {
                problems.Add($"schemaVersion er {squads.SchemaVersion}, ikke {SchemaVersion}");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;

            foreach (var team in squads.Teams ?? Array.Empty<SquadFileTeam>())
            {
                if (!TeamNames.Contains(team.Team))
                {
                    problems.Add($"laget «{team.Team}» finnes ikke i appen");
                }

                if (team.Players is not { Count: > 0 })
                {
                    problems.Add($"{team.Team} har ingen spillere");
                    continue;
                }

                for (var index = 0; index < team.Players.Count; index++)
                {
                    var player = team.Players[index];

                    // Spillerne er nummerert, ikke navngitt, i meldingene: de havner i loggen.
                    var where = $"{team.Team}, spiller {index + 1}";

                    if (string.IsNullOrWhiteSpace(player.Name) || player.Name.Length > 50)
                    {
                        problems.Add($"{where}: navnet mangler eller er over 50 tegn");
                    }
                    else if (!names.Add(player.Name))
                    {
                        problems.Add($"{where}: navnet står to ganger i fila");
                    }

                    if (string.IsNullOrWhiteSpace(player.Position) || player.Position.Length > 50)
                    {
                        problems.Add($"{where}: posisjonen mangler eller er over 50 tegn");
                    }

                    if (player.Photo is { } photo)
                    {
                        var full = Path.GetFullPath(Path.Combine(folder, photo));

                        if (!full.StartsWith(root, StringComparison.Ordinal))
                        {
                            problems.Add($"{where}: bildet ligger utenfor mappa fila står i");
                        }
                        else if (!File.Exists(full))
                        {
                            problems.Add($"{where}: bildet {photo} finnes ikke");
                        }
                    }
                }
            }

            if (squads.Teams is not { Count: > 0 })
            {
                problems.Add("fila har ingen lag");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{path} henger ikke sammen: {string.Join("; ", problems)}. {HowToRecreate}");
        }

        return squads;
    }

    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IHostEnvironment environment,
        IReadOnlyDictionary<string, Team> teams,
        SquadFile squads,
        string password,
        ILogger logger)
    {
        var admin = await userManager.FindByEmailAsync("admin@ikstart.example")
            ?? throw new InvalidOperationException("Admin-kontoen finnes ikke. Den seedes før troppene.");

        var changes = await ApplyAsync(
            db,
            teams,
            squads,
            FolderIn(environment),
            admin.Id,
            (teamId, member) => AddPlayerAsync(db, userManager, teamId, member, password),
            logger);

        if (changes.Created + changes.Updated + changes.Photos > 0)
        {
            logger.LogInformation(
                "Troppene fra {Source}: {Created} spillere lagt til, {Moved} oppdatert, {Photos} bilder lagt inn.",
                PhotoSourceOf(squads),
                changes.Created,
                changes.Updated,
                changes.Photos);
        }

        var keep = squads.Teams.SelectMany(t => t.Players).Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await RemoveFictionalPlayersAsync(db, userManager, keep, logger);
    }

    /// <summary>
    /// Legger spillerne i fila inn i databasen: en spiller som mangler, legges til med
    /// <paramref name="addPlayer"/>; en som finnes, får lag, posisjon og fødselsdato fra fila; og
    /// fornavn og bilde legges inn der de mangler. Ingenting slettes, og et fornavn eller bilde som
    /// finnes fra før, blir stående.
    ///
    /// Hvordan en ny spiller legges til, er det eneste som skiller de to som kaller: seedingen
    /// lager konto, foresatt og samtykke rundt spilleren, import-players bare spillerraden.
    /// </summary>
    /// <param name="folder">Mappa fila ble lest fra. Bildene ligger relativt til den.</param>
    /// <param name="updatedBy">Hva som står som «endret av» på fornavn og bilde.</param>
    internal static async Task<SquadChanges> ApplyAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Team> teams,
        SquadFile squads,
        string folder,
        string updatedBy,
        Func<int, SquadFilePlayer, Task<Player>> addPlayer,
        ILogger logger)
    {
        var photoSource = PhotoSourceOf(squads);

        // Sporet, og lest én gang: løkka både slår opp spillere og endrer dem den finner.
        var byCode = (await db.Players.ToListAsync())
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // Hva som er lagt inn til velkomsten fra før. Om det finnes et bilde, ikke selve bildet.
        var details = (await db.PlayerPersonalDetails
                .AsNoTracking()
                .Select(d => new { d.PlayerId, HasPhoto = d.Photo != null })
                .ToListAsync())
            .ToDictionary(d => d.PlayerId, d => d.HasPhoto);

        var created = 0;
        var updated = 0;
        var photos = 0;
        var refused = 0;

        foreach (var team in squads.Teams)
        {
            var teamId = teams[team.Team].Id;

            foreach (var member in team.Players)
            {
                if (byCode.TryGetValue(member.Name, out var player))
                {
                    if (player.TeamId != teamId || player.Position != member.Position || player.BirthDate != member.BirthDate)
                    {
                        player.TeamId = teamId;
                        player.Position = member.Position;
                        player.BirthDate = member.BirthDate;
                        updated++;
                    }
                }
                else
                {
                    player = await addPlayer(teamId, member);
                    byCode[member.Name] = player;
                    created++;
                }

                details.TryGetValue(player.Id, out var hasPhoto);

                if (!details.ContainsKey(player.Id) || (!hasPhoto && member.Photo is not null))
                {
                    switch (await AddWelcomeAsync(db, player, member, folder, photoSource, updatedBy, logger))
                    {
                        case PhotoOutcome.Added:
                            photos++;
                            break;
                        case PhotoOutcome.Refused:
                            refused++;
                            break;
                    }

                    details[player.Id] = true;
                }

                await db.SaveChangesAsync();
            }
        }

        return new SquadChanges(created, updated, photos, refused);
    }

    /// <summary>Bildekilden for en fil som ikke oppgir en per spiller: «ikstart.no, spillersidene, 2026-10-07».</summary>
    private static string PhotoSourceOf(SquadFile squads) => $"{squads.Source}, {squads.FetchedOn:yyyy-MM-dd}";

    /// <summary>
    /// Spilleren, kontoen, den oppdiktede foresatte og samtykket -- det samme en oppdiktet spiller
    /// fikk i SeedData. En foresatt bare under aldersgrensen, som regelen i
    /// <see cref="PlayerRules.GuardianRequiredBelowAge"/>: en foresatt med innsyn i en voksen
    /// spillers svar er like galt som en mindreårig uten. Uten foresatt setter spilleren samtykket selv.
    /// </summary>
    private static async Task<Player> AddPlayerAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        int teamId,
        SquadFilePlayer member,
        string password)
    {
        var userId = await SeedData.EnsureUserAsync(
            userManager, SeedData.PlayerEmail(member.Name), password, Roles.Player);

        var player = new Player
        {
            Name = member.Name,
            TeamId = teamId,
            BirthDate = member.BirthDate,
            Position = member.Position,
            UserId = userId
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        string? guardianUserId = null;

        if (player.AgeAt(DateOnly.FromDateTime(DateTime.UtcNow)) < PlayerRules.GuardianRequiredBelowAge)
        {
            guardianUserId = await SeedData.EnsureUserAsync(
                userManager, SeedData.GuardianEmail(member.Name), password, Roles.Guardian);

            db.Guardianships.Add(new Guardianship
            {
                PlayerId = player.Id,
                GuardianUserId = guardianUserId
            });
        }

        db.ConsentEvents.Add(new ConsentEvent
        {
            PlayerId = player.Id,
            Level = ConsentLevel.Full,
            ChangedByUserId = guardianUserId ?? userId,
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-30)
        });

        return player;
    }

    /// <summary>
    /// Fornavnet og bildet til velkomsten, gjennom samme kontroll som et bilde lastet opp på
    /// /Admin/Players: formatet leses av bytene, og metadata fjernes. Et bilde som ikke går
    /// gjennom, hoppes over -- velkomsten viser da bare navnet.
    ///
    /// Fornavnet og bildekilden kommer fra fila når den oppgir dem per spiller (en eksport gjør
    /// det), ellers fra navnet og filas egen kilde. Se <see cref="SquadFilePlayer"/>.
    /// </summary>
    private static async Task<PhotoOutcome> AddWelcomeAsync(
        AppDbContext db,
        Player player,
        SquadFilePlayer member,
        string folder,
        string photoSource,
        string updatedBy,
        ILogger logger)
    {
        var row = await db.PlayerPersonalDetails.FirstOrDefaultAsync(d => d.PlayerId == player.Id);
        var now = DateTimeOffset.UtcNow;

        if (row is null)
        {
            var firstName = member.FirstName is null
                ? SeedData.FirstNameOf(member.Name)
                : Blank(member.FirstName);

            if (firstName is null && member.Photo is null)
            {
                // Verken fornavn eller bilde: da er det ingenting å lagre, og ingen rad.
                return PhotoOutcome.None;
            }

            row = new PlayerPersonalDetails
            {
                PlayerId = player.Id,
                FirstName = firstName
            };

            db.PlayerPersonalDetails.Add(row);
        }

        row.UpdatedByUserId = updatedBy;
        row.UpdatedAt = now;

        if (member.Photo is null)
        {
            return PhotoOutcome.None;
        }

        var check = PlayerPhotoRules.Prepare(await File.ReadAllBytesAsync(Path.Combine(folder, member.Photo)));

        if (check is not { IsAccepted: true })
        {
            logger.LogWarning(
                "Et bilde i {Folder} ble ikke godtatt ({Reason}). Spiller-ID {PlayerId} får velkomsten uten bilde.",
                folder,
                check.Error,
                player.Id);

            return PhotoOutcome.Refused;
        }

        row.Photo = check.Photo;
        row.PhotoContentType = check.ContentType;
        row.PhotoSource = member.PhotoSource is null ? photoSource : Blank(member.PhotoSource);
        row.PhotoUpdatedAt = now;

        return PhotoOutcome.Added;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private enum PhotoOutcome
    {
        None,
        Added,
        Refused
    }

    /// <summary>
    /// Sletter de oppdiktede spillerne og kontoene deres, nå som de ekte har tatt plassen.
    ///
    /// Spillerne går med cascade, som en sletting fra /Admin/Delete: svar, samtykkelogg,
    /// foresattkoblinger, revisjonslogg, frigivelser og succession-vurderinger. Kontoene slås opp
    /// på adressene troppene ga dem, ikke på spillerradene, slik at en oppstart som stoppet etter
    /// spillerne men før kontoene, rydder resten neste gang. En konto som fortsatt hører til en
    /// spiller eller en foresattkobling, blir stående.
    ///
    /// Ingen PlayerDeletionEvent: den er kvitteringen for at opplysninger om en virkelig person
    /// er slettet, og disse personene har aldri fantes.
    /// </summary>
    private static async Task RemoveFictionalPlayersAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IReadOnlySet<string> keep,
        ILogger logger)
    {
        var names = SeedData.FictionalNames.Where(name => !keep.Contains(name)).ToList();

        var fictional = await db.Players
            .Where(p => names.Contains(p.Name))
            .ToListAsync();

        if (fictional.Count > 0)
        {
            db.Players.RemoveRange(fictional);
            await db.SaveChangesAsync();
        }

        var accounts = 0;

        foreach (var email in SeedData.FictionalAccountEmails())
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                continue;
            }

            var inUse = await db.Players.AnyAsync(p => p.UserId == user.Id)
                || await db.Guardianships.AnyAsync(g => g.GuardianUserId == user.Id);

            if (inUse)
            {
                continue;
            }

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Klarte ikke å slette demobrukeren {email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            accounts++;
        }

        if (fictional.Count + accounts > 0)
        {
            logger.LogInformation(
                "De ekte troppene erstatter de oppdiktede: {Players} oppdiktede spillere og {Accounts} kontoer slettet.",
                fictional.Count,
                accounts);
        }
    }
}

/// <summary>Hva <see cref="SeedSquads.ApplyAsync"/> gjorde. Tall, aldri navn: de havner i loggen.</summary>
/// <param name="PhotosRefused">Bilder i fila som ikke gikk gjennom bildekontrollen, og ikke ble lagt inn.</param>
internal sealed record SquadChanges(int Created, int Updated, int Photos, int PhotosRefused);

/// <summary>
/// Data/Squads/squads.json, slik scripts/squads/fetch_squads.py skriver den -- og slik
/// export-players skriver en eksport fra en database.
/// </summary>
internal sealed record SquadFile(
    int SchemaVersion,
    string Source,
    DateOnly FetchedOn,
    IReadOnlyList<SquadFileTeam> Teams);

/// <param name="Team">Lagets navn i appen: G14, G15, G17 eller G19.</param>
/// <param name="Page">Klubbens side troppen er hentet fra. Null i en eksport.</param>
internal sealed record SquadFileTeam(
    string Team,
    string? Page,
    IReadOnlyList<SquadFilePlayer> Players);

/// <param name="Name">Navnet slik klubben skriver det. Blir <see cref="Player.Name"/>.</param>
/// <param name="Position">Goalkeeper, Defender, Midfielder eller Forward -- linja klubben fører spilleren under.</param>
/// <param name="BirthDateEstimated">Klubben oppgir ingen dato, og 1. januar i årsklassen er brukt.</param>
/// <param name="Photo">Bildet, relativt til mappa fila står i. Null når klubben ikke har noe.</param>
/// <param name="FirstName">
/// Fornavnet til velkomsten. Bare en eksport oppgir det: null (feltet mangler) betyr at det
/// utledes av navnet, en tom streng at spilleren ikke hadde noe fornavn lagt inn.
/// </param>
/// <param name="PhotoSource">
/// Hvor bildet kom fra. Bare en eksport oppgir det: null (feltet mangler) betyr filas egen kilde
/// og dato, en tom streng at ingen kilde var lagt inn.
/// </param>
internal sealed record SquadFilePlayer(
    string Name,
    string Position,
    DateOnly BirthDate,
    bool BirthDateEstimated,
    string? Photo,
    string? ProfileUrl,
    string? FirstName = null,
    string? PhotoSource = null);
