using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// De to engangskommandoene som flytter de ekte spillerne fra én database til en annen:
///
///     dotnet run --project StartPraksisGruppe3Prosjekt -- export-players --out &lt;mappe&gt;
///     dotnet run --project StartPraksisGruppe3Prosjekt -- import-players --from &lt;mappe&gt;
///
/// export-players leser databasen appen er satt opp mot, og skriver spillerne som en
/// squads.json med bildene ved siden av -- samme format som scripts/squads/fetch_squads.py
/// lager, pluss fornavn og bildekilde per spiller. import-players leser en slik mappe og legger
/// spillerne inn i databasen appen er satt opp mot.
///
/// Det som flyttes, er spilleren (navn, lag, posisjon, fødselsdato) og velkomsten (fornavn,
/// bilde, bildekilde). Kontoer, foresatte, samtykker, svar og vurderinger flyttes ikke: i den
/// gamle databasen er alt det oppdiktet, også for de ekte spillerne.
///
/// TRE REGLER:
///   * Eksporten leser bare. Transaksjonen den leser i, er satt til READ ONLY, så det er
///     databasen og ikke denne koden som står for det.
///   * Importen legger til og oppdaterer. Den sletter aldri, og et fornavn eller bilde som
///     finnes fra før, blir stående. Den kan derfor kjøres flere ganger.
///   * Filene er personopplysninger om mindreårige. De skrives og leses bare i Data/Squads
///     (git-ignorert) eller en mappe utenfor repoet, og loggen sier hvor mange, aldri hvem.
///
/// Lesing, validering og innlegging er <see cref="SeedSquads"/> sin kode, ikke en kopi av den.
/// </summary>
internal static class PlayerTransfer
{
    public const string ExportCommand = "export-players";
    public const string ImportCommand = "import-players";

    /// <summary>
    /// Står som «endret av» på fornavn og bilde som importen legger inn. Ikke en bruker-ID, med
    /// vilje: det var kommandoen som la dem inn, ikke en administrator i appen.
    /// </summary>
    internal const string ImportedBy = "import-players";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,

        // Æ, ø og å skrives som de er, slik fetch_squads.py gjør. Fila skal kunne leses av et
        // menneske som sjekker den før importen.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>
    /// Skriver de ekte spillerne til <paramref name="outOption"/>, eller til Data/Squads når
    /// ingen mappe er oppgitt. Returnerer prosessens exit-kode.
    /// </summary>
    public static async Task<int> ExportAsync(
        AppDbContext db,
        IHostEnvironment environment,
        string? outOption,
        ILogger logger)
    {
        var folder = ResolveFolder(environment, outOption);
        var file = Path.Combine(folder, "squads.json");
        var photoFolder = Path.Combine(folder, "photos");

        // Aldri oppå noe som ligger der. Mappa kan være den fetch_squads.py skrev til, og en
        // eksport oppå den ville byttet ut klubbens bilder med kopier under andre filnavn.
        if (File.Exists(file) || (Directory.Exists(photoFolder) && Directory.EnumerateFileSystemEntries(photoFolder).Any()))
        {
            throw new InvalidOperationException(
                $"{folder} har allerede en squads.json eller bilder. Eksporten skriver ikke over noe: " +
                "velg en tom mappe med --out, eller flytt det som ligger der.");
        }

        List<Player> players;
        Dictionary<int, PlayerPersonalDetails> details;
        int fictional;

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");

            var all = await db.Players
                .AsNoTracking()
                .Include(p => p.Team)
                .ToListAsync();

            players = all
                .Where(p => !SeedData.IsFictional(p.Name))
                .OrderBy(p => p.Team!.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            fictional = all.Count - players.Count;

            var ids = players.Select(p => p.Id).ToList();

            details = await db.PlayerPersonalDetails
                .AsNoTracking()
                .Where(d => ids.Contains(d.PlayerId))
                .ToDictionaryAsync(d => d.PlayerId);

            // En oppdiktet spiller har aldri fått bilde av seedingen. Har en likevel det, kan det
            // være en ekte spiller som deler navn med en oppdiktet -- og da mangler hen i fila.
            var fictionalIds = all.Where(p => SeedData.IsFictional(p.Name)).Select(p => p.Id).ToList();

            var fictionalWithPhoto = await db.PlayerPersonalDetails
                .AsNoTracking()
                .Where(d => fictionalIds.Contains(d.PlayerId) && d.Photo != null)
                .Select(d => d.PlayerId)
                .OrderBy(id => id)
                .ToListAsync();

            if (fictionalWithPhoto.Count > 0)
            {
                logger.LogWarning(
                    "{Count} spillere med navn fra de oppdiktede troppene har et bilde, og er IKKE med i " +
                    "eksporten (spiller-ID {Ids}). Sjekk dem på /Admin/Players: er en av dem en ekte " +
                    "spiller som deler navn med en oppdiktet, må hen legges inn for hånd etter importen.",
                    fictionalWithPhoto.Count,
                    string.Join(", ", fictionalWithPhoto));
            }

            await transaction.CommitAsync();
        }

        if (players.Count == 0)
        {
            logger.LogWarning(
                "Ingen spillere å eksportere: databasen har {Fictional} oppdiktede spillere og ingen andre. " +
                "Ingenting er skrevet.",
                fictional);

            return 1;
        }

        // Alt som ville stoppet importen, stopper eksporten -- før noe er skrevet, og mens den
        // gamle databasen fortsatt finnes. Spillerne er nummerert med ID, ikke navngitt.
        var problems = new List<string>();
        var extensions = new Dictionary<int, string>();

        foreach (var player in players)
        {
            if (!SeedSquads.TeamNames.Contains(player.Team!.Name))
            {
                problems.Add($"spiller-ID {player.Id} står på laget «{player.Team.Name}», som importen ikke kjenner");
            }

            if (string.IsNullOrWhiteSpace(player.Position))
            {
                problems.Add($"spiller-ID {player.Id} har ingen posisjon");
            }

            if (details.TryGetValue(player.Id, out var row) && row.Photo is { } stored)
            {
                // Samme bildekontroll som importen kjører. Typen leses av bytene, ikke av kolonnen
                // som sier hva de er.
                var check = PlayerPhotoRules.Prepare(stored);

                if (check.IsAccepted && ExtensionOf(check.ContentType) is { } extension)
                {
                    extensions[player.Id] = extension;
                }
                else
                {
                    problems.Add($"spiller-ID {player.Id} har et bilde som ikke går gjennom bildekontrollen ({check.Error})");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Eksporten er stoppet, og ingenting er skrevet: {string.Join("; ", problems)}. " +
                "Rett posisjon og lag i databasen, last opp bildet på nytt på /Admin/Players, og kjør igjen.");
        }

        Directory.CreateDirectory(photoFolder);

        var photos = 0;
        var teams = new List<SquadFileTeam>();

        foreach (var team in players.GroupBy(p => p.Team!.Name))
        {
            var members = new List<SquadFilePlayer>();

            foreach (var player in team)
            {
                details.TryGetValue(player.Id, out var row);

                string? photo = null;

                if (row?.Photo is { } bytes)
                {
                    // ID-en, ikke navnet: to navn kan gi samme filnavn, og mappa skal ikke navngi
                    // noen oftere enn den må.
                    photo = $"photos/{team.Key.ToLowerInvariant()}-{player.Id}.{extensions[player.Id]}";

                    await File.WriteAllBytesAsync(Path.Combine(folder, photo), bytes);
                    photos++;
                }

                members.Add(new SquadFilePlayer(
                    Name: player.Name,
                    Position: player.Position!,
                    BirthDate: player.BirthDate,
                    BirthDateEstimated: false,
                    Photo: photo,
                    ProfileUrl: null,
                    // Tom streng, ikke null, når ingenting var lagt inn: null ville fått importen
                    // til å utlede et fornavn og en kilde spilleren ikke hadde. Se SquadFilePlayer.
                    FirstName: row?.FirstName ?? string.Empty,
                    PhotoSource: row?.PhotoSource ?? string.Empty));
            }

            teams.Add(new SquadFileTeam(team.Key, Page: null, members));
        }

        var squads = new SquadFile(
            SeedSquads.SchemaVersion,
            Source: "StartCompass, export-players",
            FetchedOn: DateOnly.FromDateTime(DateTime.UtcNow),
            teams);

        // Fila til slutt. En eksport som stopper halvveis, etterlater bilder uten squads.json, og
        // den mappa leser ingen import.
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(squads, JsonOptions) + Environment.NewLine);

        // Les den tilbake slik importen vil lese den. En fil som ikke kan importeres, skal
        // oppdages nå og ikke etter at den gamle databasen er borte.
        _ = SeedSquads.Load(folder)
            ?? throw new InvalidOperationException($"{file} ble skrevet, men finnes ikke.");

        logger.LogInformation(
            "Eksporten er skrevet til {Folder}: {Players} spillere på {Teams} lag, {Photos} bilder. " +
            "{Fictional} oppdiktede spillere er ikke med.",
            folder,
            players.Count,
            teams.Count,
            photos,
            fictional);

        logger.LogInformation(
            "Mappa inneholder navn, fødselsdato og bilde av mindreårige. Oppbevar den trygt, og " +
            "aldri i git, chat eller e-post.");

        return 0;
    }

    /// <summary>
    /// Legger spillerne i <paramref name="fromOption"/> (eller Data/Squads) inn i databasen.
    /// Returnerer prosessens exit-kode.
    /// </summary>
    public static async Task<int> ImportAsync(
        AppDbContext db,
        IHostEnvironment environment,
        string? fromOption,
        ILogger logger)
    {
        var folder = ResolveFolder(environment, fromOption);

        var squads = SeedSquads.Load(folder)
            ?? throw new InvalidOperationException(
                $"Fant ingen squads.json i {folder}. Oppgi mappa eksporten ligger i med --from.");

        // Én transaksjon: enten er alle spillerne i fila inne, eller ingen av dem. En import som
        // stopper halvveis, skal ikke etterlate et lag med halve troppen.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var teams = await db.Teams.ToDictionaryAsync(t => t.Name);
        var teamsAdded = 0;

        // Et lag i fila som mangler i databasen, legges til: uten det har spilleren ingen plass å
        // stå. Load har allerede sjekket at det er et av lagene appen kjenner.
        foreach (var name in squads.Teams.Select(t => t.Team).Distinct())
        {
            if (!teams.ContainsKey(name))
            {
                var team = new Team { Name = name };

                db.Teams.Add(team);
                teams[name] = team;
                teamsAdded++;
            }
        }

        await db.SaveChangesAsync();

        // Bare spillerraden, fornavnet og bildet. Ingen konto, ingen foresatt og ikke noe samtykke:
        // de finnes ikke ennå for de ekte spillerne, og importen dikter ikke opp noen.
        var changes = await SeedSquads.ApplyAsync(db, teams, squads, folder, ImportedBy, logger);

        await transaction.CommitAsync();

        var inFile = squads.Teams.Sum(t => t.Players.Count);

        logger.LogInformation(
            "Importen fra {Folder} er ferdig: {InFile} spillere i fila, {Created} lagt til, {Updated} " +
            "oppdatert, {Photos} bilder lagt inn, {Teams} lag opprettet. Ingenting er slettet.",
            folder,
            inFile,
            changes.Created,
            changes.Updated,
            changes.Photos,
            teamsAdded);

        logger.LogInformation(
            "Databasen har nå {Players} spillere, {Photos} av dem med bilde.",
            await db.Players.CountAsync(),
            await db.PlayerPersonalDetails.CountAsync(d => d.Photo != null));

        if (changes.PhotosRefused > 0)
        {
            logger.LogError(
                "{Refused} bilder i fila gikk ikke gjennom bildekontrollen og er ikke lagt inn. " +
                "Spillerne er importert uten bilde; se advarslene over for hvilke spiller-ID-er.",
                changes.PhotosRefused);

            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Mappa det skal skrives til eller leses fra: Data/Squads når ingen er oppgitt, ellers den
    /// oppgitte -- men bare når den ligger i Data/Squads eller utenfor repoet.
    ///
    /// Alt annet i repoet følger med i git. En eksport lagt i en tilfeldig mappe der er ett
    /// «git add .» unna å bli publisert, og repoet er offentlig.
    /// </summary>
    private static string ResolveFolder(IHostEnvironment environment, string? option)
    {
        var squads = Path.GetFullPath(SeedSquads.FolderIn(environment));

        if (string.IsNullOrWhiteSpace(option))
        {
            return squads;
        }

        var folder = Path.GetFullPath(option);

        if (IsInside(folder, squads))
        {
            return folder;
        }

        // Uten en .git-mappe over oss (en publisert app) er det appens egen mappe som er grensa.
        var repository = RepositoryRoot(environment.ContentRootPath)
            ?? Path.GetFullPath(environment.ContentRootPath);

        if (IsInside(folder, repository))
        {
            throw new InvalidOperationException(
                $"{folder} ligger i repoet ({repository}). Filene navngir mindreårige og skal ikke " +
                $"kunne havne i git: bruk {squads}, som er git-ignorert, eller en mappe utenfor repoet.");
        }

        return folder;
    }

    private static string? RepositoryRoot(string from)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(from)); directory is not null; directory = directory.Parent)
        {
            // En mappe i en vanlig klone, en fil i et worktree.
            var git = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool IsInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var root = Path.TrimEndingDirectorySeparator(folder);
        var candidate = Path.TrimEndingDirectorySeparator(path);

        return candidate.Equals(root, comparison)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>Filendelsen for et bilde slik det er lagret. Null for en type bildekontrollen ikke slipper inn.</summary>
    private static string? ExtensionOf(string? contentType) => contentType switch
    {
        PlayerPhotoRules.Jpeg => "jpg",
        PlayerPhotoRules.Png => "png",
        PlayerPhotoRules.WebP => "webp",
        _ => null
    };
}
