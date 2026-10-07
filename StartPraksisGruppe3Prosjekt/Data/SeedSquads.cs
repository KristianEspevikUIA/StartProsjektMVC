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
/// </summary>
internal static class SeedSquads
{
    private const int SchemaVersion = 1;

    private static readonly string[] TeamNames = { "G14", "G15", "G17", "G19" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Data/Squads under appens rotmappe. Fila og bildene ligger der.</summary>
    internal static string FolderIn(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "Data", "Squads");

    /// <summary>
    /// Leser troppene. Null når fila ikke finnes -- da er det ingen ekte tropper, og de oppdiktede
    /// gjelder. Finnes fila, men henger ikke sammen, stopper oppstarten: en halvlest tropp ville
    /// ellers erstattet de oppdiktede med noe som mangler spillere.
    /// </summary>
    internal static SquadFile? Load(IHostEnvironment environment)
    {
        var folder = FolderIn(environment);
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
                $"{path} kan ikke leses: {ex.Message} Kjør scripts/squads/fetch_squads.py på nytt.", ex);
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
                            problems.Add($"{where}: bildet ligger utenfor Data/Squads");
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
                $"{path} henger ikke sammen: {string.Join("; ", problems)}. " +
                "Kjør scripts/squads/fetch_squads.py på nytt.");
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

        var folder = FolderIn(environment);
        var photoSource = $"{squads.Source}, {squads.FetchedOn:yyyy-MM-dd}";

        // Sporet, og lest én gang: løkka både slår opp spillere og endrer dem den finner.
        var byCode = (await db.Players.ToListAsync())
            .ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        // Hva som er lagt inn til velkomsten fra før. Om det finnes et bilde, ikke selve bildet.
        var details = (await db.PlayerPersonalDetails
                .AsNoTracking()
                .Select(d => new { d.PlayerId, HasPhoto = d.Photo != null })
                .ToListAsync())
            .ToDictionary(d => d.PlayerId, d => d.HasPhoto);

        var created = 0;
        var moved = 0;
        var photos = 0;

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
                        moved++;
                    }
                }
                else
                {
                    player = await AddPlayerAsync(db, userManager, teamId, member, password);
                    byCode[member.Name] = player;
                    created++;
                }

                details.TryGetValue(player.Id, out var hasPhoto);

                if (!details.ContainsKey(player.Id) || (!hasPhoto && member.Photo is not null))
                {
                    if (await AddWelcomeAsync(db, player, member, folder, photoSource, admin.Id, logger))
                    {
                        photos++;
                    }

                    details[player.Id] = true;
                }

                await db.SaveChangesAsync();
            }
        }

        if (created + moved + photos > 0)
        {
            logger.LogInformation(
                "Troppene fra {Source}: {Created} spillere lagt til, {Moved} oppdatert, {Photos} bilder lagt inn.",
                photoSource,
                created,
                moved,
                photos);
        }

        var keep = squads.Teams.SelectMany(t => t.Players).Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await RemoveFictionalPlayersAsync(db, userManager, keep, logger);
    }

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
            Code = member.Name,
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
    /// gjennom, hoppes over -- velkomsten viser da bare navnet. True når et bilde ble lagt inn.
    /// </summary>
    private static async Task<bool> AddWelcomeAsync(
        AppDbContext db,
        Player player,
        SquadFilePlayer member,
        string folder,
        string photoSource,
        string adminUserId,
        ILogger logger)
    {
        var row = await db.PlayerPersonalDetails.FirstOrDefaultAsync(d => d.PlayerId == player.Id);
        var now = DateTimeOffset.UtcNow;

        if (row is null)
        {
            row = new PlayerPersonalDetails
            {
                PlayerId = player.Id,
                FirstName = SeedData.FirstNameOf(member.Name)
            };

            db.PlayerPersonalDetails.Add(row);
        }

        row.UpdatedByUserId = adminUserId;
        row.UpdatedAt = now;

        if (member.Photo is null)
        {
            return false;
        }

        var check = PlayerPhotoRules.Prepare(await File.ReadAllBytesAsync(Path.Combine(folder, member.Photo)));

        if (check is not { IsAccepted: true })
        {
            logger.LogWarning(
                "Et bilde i Data/Squads ble ikke godtatt ({Reason}). Spiller-ID {PlayerId} får velkomsten uten bilde.",
                check.Error,
                player.Id);

            return false;
        }

        row.Photo = check.Photo;
        row.PhotoContentType = check.ContentType;
        row.PhotoSource = photoSource;
        row.PhotoUpdatedAt = now;

        return true;
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
            .Where(p => names.Contains(p.Code))
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

/// <summary>Data/Squads/squads.json, slik scripts/squads/fetch_squads.py skriver den.</summary>
internal sealed record SquadFile(
    int SchemaVersion,
    string Source,
    DateOnly FetchedOn,
    IReadOnlyList<SquadFileTeam> Teams);

/// <param name="Team">Lagets navn i appen: G14, G15, G17 eller G19.</param>
/// <param name="Page">Klubbens side troppen er hentet fra.</param>
internal sealed record SquadFileTeam(
    string Team,
    string Page,
    IReadOnlyList<SquadFilePlayer> Players);

/// <param name="Name">Navnet slik klubben skriver det. Blir <see cref="Player.Code"/>.</param>
/// <param name="Position">Goalkeeper, Defender, Midfielder eller Forward -- linja klubben fører spilleren under.</param>
/// <param name="BirthDateEstimated">Klubben oppgir ingen dato, og 1. januar i årsklassen er brukt.</param>
/// <param name="Photo">Bildet, relativt til Data/Squads. Null når klubben ikke har noe.</param>
internal sealed record SquadFilePlayer(
    string Name,
    string Position,
    DateOnly BirthDate,
    bool BirthDateEstimated,
    string? Photo,
    string? ProfileUrl);
