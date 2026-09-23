using System.Text.Json;
using StartPraksisGruppe3Prosjekt.Models.Succession;

namespace StartPraksisGruppe3Prosjekt.Services.Succession;

/// <summary>
/// Read access to Data/Succession/succession-planning.json: the lists, the thresholds and the
/// formations. Loaded and checked once, at startup; no page reads the file.
/// </summary>
public interface ISuccessionCatalog
{
    SuccessionSettings Settings { get; }

    /// <summary>The formation the page opens on: the first one in the file.</summary>
    FormationDefinition DefaultFormation { get; }

    /// <summary>The formation with this key ("4-3-3"), case-insensitive, or null.</summary>
    FormationDefinition? FindFormation(string? key);

    SuccessionOption? Position(string? key);

    SuccessionOption? AbilityCategory(string? key);

    SuccessionOption? ContractType(string? key);

    SuccessionOption? Level(string? key);

    SuccessionOption? Risk(string? key);

    /// <summary>How severe a risk is: its place in the list, least severe first. -1 when unknown.</summary>
    int RiskSeverity(string? key);

    /// <summary>The cycle a date falls in.</summary>
    SuccessionCycle CycleOf(DateOnly date);
}

/// <summary>
/// Loads the succession file and holds it.
///
/// Same rule as the 5C question set and the Identity Gold Standard: the file ships with the
/// application, and a mistake in it stops startup with a message that names the problem. A
/// formation with ten players, or a slot naming a position that is not in the list, would
/// otherwise turn up as a pitch with a hole in it in front of the coaching staff.
/// </summary>
public sealed class SuccessionCatalog : ISuccessionCatalog
{
    /// <summary>Relative to the content root.</summary>
    public const string SettingsPath = "Data/Succession/succession-planning.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, SuccessionOption> _positions;
    private readonly Dictionary<string, SuccessionOption> _categories;
    private readonly Dictionary<string, SuccessionOption> _contracts;
    private readonly Dictionary<string, SuccessionOption> _levels;
    private readonly Dictionary<string, SuccessionOption> _risks;
    private readonly Dictionary<string, FormationDefinition> _formations;

    public SuccessionCatalog(IWebHostEnvironment environment, ILogger<SuccessionCatalog> logger)
        : this(Load(Path.GetFullPath(Path.Combine(environment.ContentRootPath, SettingsPath))))
    {
        logger.LogInformation(
            "Succession planning: {Positions} positions, {Formations} formations, {Weeks}-week cycles (version {Version}).",
            Settings.Positions.Count,
            Settings.Formations.Count,
            Settings.Cycle.LengthWeeks,
            Settings.Version);
    }

    /// <summary>From settings already in hand. For tests, and for the constructor above.</summary>
    public SuccessionCatalog(SuccessionSettings settings)
    {
        Validate(settings);

        Settings = settings;

        _positions = ByKey(settings.Positions);
        _categories = ByKey(settings.AbilityCategories);
        _contracts = ByKey(settings.ContractTypes);
        _levels = ByKey(settings.Levels);
        _risks = ByKey(settings.Risks);
        _formations = settings.Formations.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
    }

    public SuccessionSettings Settings { get; }

    public FormationDefinition DefaultFormation => Settings.Formations[0];

    public FormationDefinition? FindFormation(string? key) =>
        key is not null && _formations.TryGetValue(key.Trim(), out var formation) ? formation : null;

    public SuccessionOption? Position(string? key) => Find(_positions, key);

    public SuccessionOption? AbilityCategory(string? key) => Find(_categories, key);

    public SuccessionOption? ContractType(string? key) => Find(_contracts, key);

    public SuccessionOption? Level(string? key) => Find(_levels, key);

    public SuccessionOption? Risk(string? key) => Find(_risks, key);

    public int RiskSeverity(string? key)
    {
        if (key is null)
        {
            return -1;
        }

        for (var index = 0; index < Settings.Risks.Count; index++)
        {
            if (string.Equals(Settings.Risks[index].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    public SuccessionCycle CycleOf(DateOnly date) => SuccessionCycle.Of(date, Settings.Cycle);

    /// <summary>Reads the file. Internal so the tests can load THE file the application ships.</summary>
    internal static SuccessionSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The succession planning settings were not found at '{path}'. The file is required: every " +
                "position, formation and threshold on the Succession pages comes from it.");
        }

        try
        {
            using var stream = File.OpenRead(path);

            return JsonSerializer.Deserialize<SuccessionSettings>(stream, SerializerOptions)
                ?? throw new InvalidOperationException($"The succession planning settings at '{path}' are empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The succession planning settings at '{path}' are not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Everything the pages and the calculations rely on. Every message names the entry,
    /// because whoever broke it is editing JSON, not reading C#.
    /// </summary>
    internal static void Validate(SuccessionSettings settings)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            problems.Add("'version' is missing.");
        }

        if (settings.Cycle.LengthWeeks is < 1 or > 52)
        {
            problems.Add($"'cycle.lengthWeeks' must be between 1 and 52 (found {settings.Cycle.LengthWeeks}).");
        }

        if (settings.Cycle.FirstCycleStartsOn == default)
        {
            problems.Add("'cycle.firstCycleStartsOn' is missing. Write it as \"2026-01-05\".");
        }

        var scale = settings.Scale;
        if (scale.Min < 0 || scale.Max <= scale.Min)
        {
            problems.Add($"'scale' needs 0 <= min < max (found {scale.Min} and {scale.Max}).");
        }
        else if (!(scale.Min < settings.DevelopingAt
                   && settings.DevelopingAt < settings.ReadyAt
                   && settings.ReadyAt <= scale.Max))
        {
            problems.Add(
                $"The thresholds need scale.min < developingAt < readyAt <= scale.max (found {scale.Min}, " +
                $"{settings.DevelopingAt}, {settings.ReadyAt}, {scale.Max}).");
        }

        if (settings.DisagreementAt <= 0 || settings.DisagreementAt > scale.Max - scale.Min)
        {
            problems.Add($"'disagreementAt' must be above 0 and within the scale (found {settings.DisagreementAt}).");
        }

        if (settings.HorizonWeeks < 1)
        {
            problems.Add($"'horizonWeeks' must be at least 1 (found {settings.HorizonWeeks}).");
        }

        if (settings.PositionRankPenalty.Count != 3 || settings.PositionRankPenalty.Any(p => p < 0))
        {
            problems.Add("'positionRankPenalty' needs exactly three numbers of 0 or more: [1st, 2nd, 3rd].");
        }

        if (settings.Ratings.Count == 0)
        {
            problems.Add("'ratings' is empty. Overall readiness is the average of them, so at least one is needed.");
        }

        CheckList(settings.Ratings, "ratings", toned: false, problems);
        CheckList(settings.Positions, "positions", toned: false, problems);
        CheckList(settings.AbilityCategories, "abilityCategories", toned: true, problems);
        CheckList(settings.ContractTypes, "contractTypes", toned: true, problems);
        CheckList(settings.Levels, "levels", toned: false, problems);
        CheckList(settings.Risks, "risks", toned: true, problems);

        // The keys are stored in columns of this length. See SuccessionAssessment.
        foreach (var position in settings.Positions.Where(p => p.Key.Length > SuccessionRules.PositionKeyLength))
        {
            problems.Add($"The position key '{position.Key}' is longer than {SuccessionRules.PositionKeyLength} characters.");
        }

        foreach (var option in settings.Ratings.Concat(settings.AbilityCategories).Concat(settings.ContractTypes)
                     .Concat(settings.Levels).Concat(settings.Risks)
                     .Where(o => o.Key.Length > SuccessionRules.OptionKeyLength))
        {
            problems.Add($"The key '{option.Key}' is longer than {SuccessionRules.OptionKeyLength} characters.");
        }

        if (settings.Formations.Count == 0)
        {
            problems.Add("'formations' is empty. The best-eleven page needs at least one.");
        }

        foreach (var duplicate in Duplicates(settings.Formations.Select(f => f.Key)))
        {
            problems.Add($"The formation key '{duplicate}' is used more than once.");
        }

        var positionKeys = settings.Positions.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var formation in settings.Formations)
        {
            var name = string.IsNullOrWhiteSpace(formation.Key) ? $"(no key, name '{formation.Name}')" : formation.Key;

            if (string.IsNullOrWhiteSpace(formation.Key) || string.IsNullOrWhiteSpace(formation.Name))
            {
                problems.Add($"Formation {name} is missing 'key' or 'name'.");
            }

            var slots = formation.Slots.ToList();

            if (slots.Count != SuccessionRules.PlayersOnThePitch)
            {
                problems.Add($"Formation {name} has {slots.Count} slots; a team is {SuccessionRules.PlayersOnThePitch}.");
            }

            if (formation.Lines.Any(line => line.Count == 0))
            {
                problems.Add($"Formation {name} has an empty line.");
            }

            foreach (var unknown in slots.Where(s => !positionKeys.Contains(s)).Distinct())
            {
                problems.Add($"Formation {name} names the position '{unknown}', which is not in 'positions'.");
            }

            foreach (var duplicate in Duplicates(slots))
            {
                problems.Add($"Formation {name} has the position '{duplicate}' twice. Use two different positions.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The succession planning settings in {SettingsPath} have {problems.Count} problem(s):" +
                $"{Environment.NewLine}  - " + string.Join($"{Environment.NewLine}  - ", problems));
        }
    }

    private static void CheckList(
        IReadOnlyList<SuccessionOption> options,
        string name,
        bool toned,
        List<string> problems)
    {
        if (options.Count == 0 && name != "ratings")
        {
            problems.Add($"'{name}' is empty.");
        }

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Key) || string.IsNullOrWhiteSpace(option.Name))
            {
                problems.Add($"An entry in '{name}' is missing 'key' or 'name' (key: '{option.Key}').");
            }

            if (toned && !SuccessionTones.IsKnown(option.Tone))
            {
                problems.Add(
                    $"'{name}' entry '{option.Key}' has the tone '{option.Tone}'. Use one of: " +
                    string.Join(", ", SuccessionTones.All) + ".");
            }
        }

        foreach (var duplicate in Duplicates(options.Select(o => o.Key)))
        {
            problems.Add($"The key '{duplicate}' is used more than once in '{name}'.");
        }
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> keys) =>
        keys.Where(k => !string.IsNullOrWhiteSpace(k))
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

    private static Dictionary<string, SuccessionOption> ByKey(IEnumerable<SuccessionOption> options) =>
        options.ToDictionary(o => o.Key, StringComparer.OrdinalIgnoreCase);

    private static SuccessionOption? Find(Dictionary<string, SuccessionOption> options, string? key) =>
        key is not null && options.TryGetValue(key.Trim(), out var option) ? option : null;
}
