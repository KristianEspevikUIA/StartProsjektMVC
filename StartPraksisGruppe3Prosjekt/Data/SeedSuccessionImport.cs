using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Trenernes utfylte «IK Start Succession Planning»-ark, for Development: hver treners vurderinger
/// av de ekte spillerne, koblet til spillerne fra Data/Squads (se <see cref="SeedSquads"/>).
///
/// INGENTING AV DET STÅR I REPOET. Fila ligger i Data/Succession/Import/, som er git-ignorert:
/// repoet er offentlig, og nesten alle spillerne er mindreårige. Den lages av
/// scripts/succession/import_workbooks.py, som også matcher radene mot troppene og bare tar med
/// de den kunne matche trygt, og bare de strukturerte feltene -- ingen fritekst. Mangler fila,
/// gjør denne klassen ingenting.
///
/// Når fila finnes:
///   * Hvert ark er én trener, og hver trener får en konto med bare initialene
///     (trener.ab@ikstart.example), uten passord og låst. Ingen kan logge inn som dem før klubben
///     har bestemt hvem som skal ha kontoene; vurderingene deres vises likevel på tavla.
///   * Hver vurdering legges i syklusen arket ble lagret i, med datoen fra arket.
///   * Kontrakt og treningsgruppe legges inn der de mangler, eller der det som står, er
///     oppdiktet av <see cref="SeedSuccession"/>. Det admin har lagt inn, røres ikke.
///   * En spiller med importerte vurderinger får ingen oppdiktede ved siden av: de som finnes,
///     slettes her, og <see cref="SeedSuccession"/> hopper over spilleren etterpå.
///
/// Idempotent per (spiller, trener, syklus), samme nøkkel som den unike indeksen. Fila er
/// fasiten for kontoene den lager: en vurdering som er endret i fila, rettes, og en som er tatt
/// ut av fila (f.eks. etter en avgjørelse i decisions.json), slettes -- men bare i syklusen fila
/// gjelder, så en ny fil for neste syklus ikke visker ut historikken.
///
/// Loggen sier hvor mange, aldri hvem.
/// </summary>
internal static class SeedSuccessionImport
{
    private const int SchemaVersion = 1;

    private static readonly Regex RaterKey = new("^[a-z]{1,20}$");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Data/Succession/Import/assessments.json under appens rotmappe.</summary>
    internal static string PathIn(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "Data", "Succession", "Import", "assessments.json");

    /// <summary>Kontoen til treneren bak ett ark. Bare initialene, ikke navnet.</summary>
    internal static string RaterEmail(string key) => $"trener.{key}@ikstart.example";

    /// <summary>
    /// Leser fila. Null når den ikke finnes. Finnes den, men henger ikke sammen med listene i
    /// succession-planning.json, stopper oppstarten: en halvlest import ville ellers rettet
    /// og slettet vurderinger ut fra noe som mangler.
    /// </summary>
    internal static SuccessionImportFile? Load(IHostEnvironment environment, ISuccessionCatalog catalog)
    {
        var path = PathIn(environment);

        if (!File.Exists(path))
        {
            return null;
        }

        SuccessionImportFile? file;

        try
        {
            file = JsonSerializer.Deserialize<SuccessionImportFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{path} kan ikke leses: {ex.Message} Kjør scripts/succession/import_workbooks.py på nytt.", ex);
        }

        var problems = file is null ? new List<string> { "fila er tom" } : Check(file, catalog);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{path} henger ikke sammen: {string.Join("; ", problems)}. " +
                "Kjør scripts/succession/import_workbooks.py på nytt.");
        }

        return file;
    }

    private static List<string> Check(SuccessionImportFile file, ISuccessionCatalog catalog)
    {
        var problems = new List<string>();
        var settings = catalog.Settings;

        if (file.SchemaVersion != SchemaVersion)
        {
            problems.Add($"schemaVersion er {file.SchemaVersion}, ikke {SchemaVersion}");
        }

        if (file.Raters is null || file.Players is null || file.Players.Any(p => p.Assessments is null))
        {
            problems.Add("trenerne, spillerne eller vurderingene til en spiller mangler");
            return problems;
        }

        var raters = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rater in file.Raters)
        {
            if (rater.Key is null || !RaterKey.IsMatch(rater.Key) || !raters.Add(rater.Key))
            {
                problems.Add($"treneren «{rater.Key}» er ikke små bokstaver, eller står to ganger");
            }

            if (rater.RatedOn < settings.Cycle.FirstCycleStartsOn)
            {
                problems.Add($"treneren «{rater.Key}» har en dato før den første syklusen");
            }
        }

        var ratingKeys = settings.Ratings.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var players = file.Players;

        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];

            // Spillerne er nummerert, ikke navngitt, i meldingene: de havner i loggen.
            var where = $"spiller {index + 1}";

            if (string.IsNullOrWhiteSpace(player.Name) || player.Name.Length > 50 || !names.Add(player.Name))
            {
                problems.Add($"{where}: navnet mangler, er over 50 tegn eller står to ganger");
            }

            Known(problems, where, "contractType", player.ContractType, catalog.ContractType);
            Known(problems, where, "trainingGroup", player.TrainingGroup, catalog.Level);

            var ratedBy = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assessment in player.Assessments)
            {
                if (!raters.Contains(assessment.Rater) || !ratedBy.Add(assessment.Rater))
                {
                    problems.Add($"{where}: treneren «{assessment.Rater}» er ukjent, eller har vurdert to ganger");
                }

                var ratings = assessment.Ratings ?? new Dictionary<string, int>();

                if (ratings.Count == 0)
                {
                    problems.Add($"{where}: en vurdering uten tall");
                }

                foreach (var (key, value) in ratings)
                {
                    if (!ratingKeys.Contains(key) || value < settings.Scale.Min || value > settings.Scale.Max)
                    {
                        problems.Add($"{where}: vurderingen «{key}» = {value} finnes ikke i lista eller skalaen");
                    }
                }

                if (assessment.PersonalReadiness is < 1 or > 10)
                {
                    problems.Add($"{where}: personal readiness er utenfor 1–10");
                }

                Known(problems, where, "ratedAs", assessment.RatedAs, catalog.Level);
                Known(problems, where, "abilityCategory", assessment.AbilityCategory, catalog.AbilityCategory);
                Known(problems, where, "firstPosition", assessment.FirstPosition, catalog.Position);
                Known(problems, where, "secondPosition", assessment.SecondPosition, catalog.Position);
                Known(problems, where, "thirdPosition", assessment.ThirdPosition, catalog.Position);
                Known(problems, where, "successionRisk", assessment.SuccessionRisk, catalog.Risk);
            }
        }

        return problems;
    }

    private static void Known(
        List<string> problems,
        string where,
        string field,
        string? key,
        Func<string?, SuccessionOption?> find)
    {
        if (key is not null && find(key) is null)
        {
            problems.Add($"{where}: {field} «{key}» finnes ikke i succession-planning.json");
        }
    }

    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        ISuccessionCatalog catalog,
        SuccessionImportFile file,
        ILogger logger)
    {
        var admin = await userManager.FindByEmailAsync("admin@ikstart.example")
            ?? throw new InvalidOperationException("Admin-kontoen finnes ikke. Den seedes før importen.");

        var players = (await db.Players.AsNoTracking().ToListAsync())
            .ToDictionary(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase);

        var demoRaters = await DemoRaterIdsAsync(userManager);

        // Hver trener: kontoen, syklusen og datoen arket ble lagret. Kontoen lages bare når det
        // finnes noe å legge inn -- et ark ingen har fylt ut, gir ingen konto.
        var raters = new Dictionary<string, (string UserId, DateOnly Cycle, DateTimeOffset RatedAt)>();

        foreach (var rater in file.Raters)
        {
            var email = RaterEmail(rater.Key);
            var user = await userManager.FindByEmailAsync(email);

            var needed = file.Players.Any(p => players.ContainsKey(p.Name) && p.Assessments.Any(a => a.Rater == rater.Key));

            if (user is null && needed)
            {
                user = await CreateLockedRaterAsync(userManager, email);
            }

            if (user is not null)
            {
                raters[rater.Key] = (
                    user.Id,
                    catalog.CycleOf(rater.RatedOn).StartsOn,
                    new DateTimeOffset(rater.RatedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            }
        }

        var raterIds = raters.Values.Select(r => r.UserId).ToList();

        // Sporet, og lest én gang: løkka både slår opp og endrer.
        var existing = (await db.SuccessionAssessments
                .Include(a => a.Ratings)
                .Where(a => raterIds.Contains(a.RaterUserId))
                .ToListAsync())
            .ToDictionary(a => (a.PlayerId, a.RaterUserId, a.CycleStartsOn));

        var profiles = (await db.PlayerSuccessionProfiles.ToListAsync())
            .ToDictionary(p => p.PlayerId);

        var kept = new HashSet<(int, string, DateOnly)>();
        var added = 0;
        var corrected = 0;
        var profilesSet = 0;
        var missing = 0;
        var removedDemo = 0;

        foreach (var item in file.Players)
        {
            if (!players.TryGetValue(item.Name, out var playerId))
            {
                // Ikke i basen: troppene er ikke lastet inn, eller spilleren har sluttet.
                missing++;
                continue;
            }

            if (SetProfile(db, profiles, playerId, item, demoRaters, admin.Id))
            {
                profilesSet++;
            }

            var assessments = item.Assessments.Where(a => raters.ContainsKey(a.Rater)).ToList();

            if (assessments.Count == 0)
            {
                continue;
            }

            removedDemo += await RemoveDemoAssessmentsAsync(db, playerId, demoRaters);

            foreach (var assessment in assessments)
            {
                var (raterUserId, cycle, ratedAt) = raters[assessment.Rater];
                var key = (playerId, raterUserId, cycle);
                kept.Add(key);

                if (!existing.TryGetValue(key, out var row))
                {
                    row = new SuccessionAssessment
                    {
                        PlayerId = playerId,
                        RaterUserId = raterUserId,
                        CycleStartsOn = cycle
                    };

                    db.SuccessionAssessments.Add(row);
                    existing[key] = row;
                    added++;
                }
                else if (SameAs(row, assessment))
                {
                    continue;
                }
                else
                {
                    corrected++;
                }

                Apply(db, assessment, row, catalog.Settings.Version, ratedAt);
            }
        }

        // Det som er tatt ut av fila, i syklusen fila gjelder for hver trener.
        var cycles = raters.Values.Select(r => (r.UserId, r.Cycle)).ToHashSet();
        var withdrawn = existing.Values
            .Where(a => cycles.Contains((a.RaterUserId, a.CycleStartsOn))
                        && !kept.Contains((a.PlayerId, a.RaterUserId, a.CycleStartsOn)))
            .ToList();

        db.SuccessionAssessments.RemoveRange(withdrawn);

        await db.SaveChangesAsync();

        if (added + corrected + withdrawn.Count + profilesSet + removedDemo > 0 || missing > 0)
        {
            logger.LogInformation(
                "Succession-arkene: {Added} vurderinger lagt inn, {Corrected} rettet, {Withdrawn} tatt ut, " +
                "{Profiles} kontrakter og treningsgrupper satt, {Demo} oppdiktede vurderinger slettet. " +
                "{Missing} spillere i fila finnes ikke i basen.",
                added,
                corrected,
                withdrawn.Count,
                profilesSet,
                removedDemo,
                missing);
        }
    }

    /// <summary>
    /// En trenerkonto uten passord, låst. Uten passord kan ingen logge inn med den uansett; låsen
    /// sier det samme til den som ser på kontoen. Trenerrollen fordi det er en trener som har
    /// vurdert, slik eksporten og revisjonsloggen viser.
    /// </summary>
    private static async Task<IdentityUser> CreateLockedRaterAsync(UserManager<IdentityUser> userManager, string email)
    {
        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue
        };

        var result = await userManager.CreateAsync(user);
        if (result.Succeeded)
        {
            result = await userManager.AddToRoleAsync(user, Roles.Coach);
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Klarte ikke å opprette trenerkontoen {email}: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return user;
    }

    /// <summary>Kontoene <see cref="SeedSuccession"/> lager oppdiktede vurderinger med.</summary>
    private static async Task<HashSet<string>> DemoRaterIdsAsync(UserManager<IdentityUser> userManager)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var email in SeedSuccession.ExtraRaterEmails.Append(SeedData.CoachEmail))
        {
            if (await userManager.FindByEmailAsync(email) is { } user)
            {
                ids.Add(user.Id);
            }
        }

        return ids;
    }

    private static async Task<int> RemoveDemoAssessmentsAsync(AppDbContext db, int playerId, HashSet<string> demoRaters)
    {
        var demo = await db.SuccessionAssessments
            .Where(a => a.PlayerId == playerId && demoRaters.Contains(a.RaterUserId))
            .ToListAsync();

        db.SuccessionAssessments.RemoveRange(demo);
        return demo.Count;
    }

    /// <summary>
    /// Kontrakt og treningsgruppe fra arket, der det ikke står noe, eller der det som står, er
    /// lagt inn av <see cref="SeedSuccession"/>. True når noe ble satt.
    /// </summary>
    private static bool SetProfile(
        AppDbContext db,
        Dictionary<int, PlayerSuccessionProfile> profiles,
        int playerId,
        SuccessionImportPlayer item,
        HashSet<string> demoRaters,
        string adminUserId)
    {
        if (item.ContractType is null && item.ContractEndsOn is null && item.TrainingGroup is null)
        {
            return false;
        }

        if (profiles.TryGetValue(playerId, out var profile) && !demoRaters.Contains(profile.UpdatedByUserId))
        {
            return false;
        }

        if (profile is null)
        {
            profile = new PlayerSuccessionProfile { PlayerId = playerId };
            db.PlayerSuccessionProfiles.Add(profile);
            profiles[playerId] = profile;
        }

        profile.ContractType = item.ContractType;
        profile.ContractEndsOn = item.ContractEndsOn;
        profile.TrainingGroup = item.TrainingGroup;
        profile.UpdatedByUserId = adminUserId;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        return true;
    }

    private static bool SameAs(SuccessionAssessment row, SuccessionImportAssessment item) =>
        row.RatedAs == item.RatedAs
        && row.AbilityCategory == item.AbilityCategory
        && row.FirstPosition == item.FirstPosition
        && row.SecondPosition == item.SecondPosition
        && row.ThirdPosition == item.ThirdPosition
        && row.PersonalReadiness == item.PersonalReadiness
        && row.PathwayBlocked == item.PathwayBlocked
        && row.ExternalNeeded == item.ExternalNeeded
        && row.SuccessionRisk == item.SuccessionRisk
        && row.Ratings.Count == item.Ratings.Count
        && row.Ratings.All(r => item.Ratings.TryGetValue(r.RatingKey, out var value) && value == r.Value);

    /// <summary>
    /// Feltene fra arket. Fritekstfeltene står tomme: skriptet leser dem aldri, se
    /// scripts/succession/import_workbooks.py.
    /// </summary>
    private static void Apply(
        AppDbContext db,
        SuccessionImportAssessment item,
        SuccessionAssessment row,
        string catalogVersion,
        DateTimeOffset ratedAt)
    {
        db.SuccessionRatings.RemoveRange(row.Ratings);
        row.Ratings.Clear();

        foreach (var (key, value) in item.Ratings)
        {
            row.Ratings.Add(new SuccessionRating { RatingKey = key, Value = value });
        }

        row.CatalogVersion = catalogVersion;
        row.UpdatedAt = ratedAt;
        row.RatedAs = item.RatedAs;
        row.AbilityCategory = item.AbilityCategory;
        row.FirstPosition = item.FirstPosition;
        row.SecondPosition = item.SecondPosition;
        row.ThirdPosition = item.ThirdPosition;
        row.PersonalReadiness = item.PersonalReadiness;
        row.PathwayBlocked = item.PathwayBlocked;
        row.ExternalNeeded = item.ExternalNeeded;
        row.SuccessionRisk = item.SuccessionRisk;
    }
}

/// <summary>Data/Succession/Import/assessments.json, slik scripts/succession/import_workbooks.py skriver den.</summary>
internal sealed record SuccessionImportFile(
    int SchemaVersion,
    string Source,
    DateOnly CreatedOn,
    IReadOnlyList<SuccessionImportRater> Raters,
    IReadOnlyList<SuccessionImportPlayer> Players);

/// <param name="Key">Initialene fra arkets filnavn, med små bokstaver: «ab».</param>
/// <param name="RatedOn">Da arket sist ble lagret. Avgjør syklusen.</param>
internal sealed record SuccessionImportRater(string Key, DateOnly RatedOn);

/// <param name="Name">Navnet slik klubben skriver det. Er <see cref="Player.Code"/>.</param>
internal sealed record SuccessionImportPlayer(
    string Name,
    string? ContractType,
    DateOnly? ContractEndsOn,
    string? TrainingGroup,
    IReadOnlyList<SuccessionImportAssessment> Assessments);

/// <param name="Rater">Nøkkelen til et ark i <see cref="SuccessionImportFile.Raters"/>.</param>
/// <param name="Ratings">Nøkkel fra «ratings» i succession-planning.json, og verdien 1–10.</param>
internal sealed record SuccessionImportAssessment(
    string Rater,
    IReadOnlyDictionary<string, int> Ratings,
    string? RatedAs,
    string? AbilityCategory,
    string? FirstPosition,
    string? SecondPosition,
    string? ThirdPosition,
    int? PersonalReadiness,
    bool? PathwayBlocked,
    bool? ExternalNeeded,
    string? SuccessionRisk);
