using System.Text.Json;
using Microsoft.Extensions.Options;
using StartPraksisGruppe3Prosjekt.Models.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>
/// Loads Data/Identity/gold-standard.json and the extracted match data once, and keeps them.
///
/// The two are treated differently on purpose:
///
///   * The Gold Standard ships with the application. Missing or broken, it stops startup with
///     a message that names the problem -- same rule as the 5C question set.
///
///   * The match data does not ship: it names players and is not in git. A team without a file
///     is simply a team without data, and the page says so. A file that IS there but does not
///     add up still stops startup, because a page quietly showing half a season is worse than
///     one that does not start.
/// </summary>
public sealed class IdentityCatalog : IIdentityCatalog
{
    /// <summary>Relative to the content root.</summary>
    public const string GoldStandardPath = "Data/Identity/gold-standard.json";

    /// <summary>
    /// The match data format this code reads. The extraction script writes the same number.
    /// 2 added the six substitute markers' values; a version 1 file has to be extracted again.
    /// </summary>
    public const int SupportedSchemaVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, IdentityTeam> _teamsByKey;

    public IdentityCatalog(
        IWebHostEnvironment environment,
        IOptions<IdentityBenchmarkOptions> options,
        ILogger<IdentityCatalog> logger)
    {
        var goldStandardPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, GoldStandardPath));

        if (!File.Exists(goldStandardPath))
        {
            throw new InvalidOperationException(
                $"The Identity Gold Standard was not found at '{goldStandardPath}'. The file is required: " +
                "every marker, range and threshold on the Identity page comes from it.");
        }

        GoldStandard = Read<GoldStandard>(goldStandardPath, "The Identity Gold Standard");
        ValidateGoldStandard(GoldStandard, goldStandardPath);

        var markerCount = GoldStandard.AllMarkers.Count();
        if (markerCount != 10)
        {
            // The club's document has ten. Not fatal -- the document may change -- but worth
            // saying, because the usual cause is a marker lost in an edit.
            logger.LogWarning(
                "The Identity Gold Standard has {MarkerCount} markers; the club's document has 10.", markerCount);
        }

        var matchDataFolder = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.MatchDataPath));

        var teams = new List<IdentityTeam>();
        foreach (var key in options.Value.Teams.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()))
        {
            var path = Path.Combine(matchDataFolder, key.ToLowerInvariant() + ".json");

            if (!File.Exists(path))
            {
                logger.LogInformation(
                    "Identity Benchmarking: no match data for {Team} ({Path}). The page will say so.", key, path);
                teams.Add(new IdentityTeam(key, null));
                continue;
            }

            var data = Read<TeamMatchData>(path, $"The {key} match data");
            ValidateMatchData(data, key, GoldStandard, path);

            // Oldest first, whatever order the file is in. The development charts depend on it.
            data = new TeamMatchData
            {
                SchemaVersion = data.SchemaVersion,
                Team = data.Team,
                TeamName = data.TeamName,
                GeneratedAt = data.GeneratedAt,
                GeneratedBy = data.GeneratedBy,
                SourceFormat = data.SourceFormat,
                Matches = data.Matches.OrderBy(m => m.Date).ThenBy(m => m.Id, StringComparer.Ordinal).ToList()
            };

            logger.LogInformation(
                "Identity Benchmarking: loaded {MatchCount} matches for {Team}, generated {GeneratedAt:u}.",
                data.Matches.Count, key, data.GeneratedAt);

            teams.Add(new IdentityTeam(key, data));
        }

        Teams = teams;
        _teamsByKey = teams.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
    }

    public GoldStandard GoldStandard { get; }

    public IReadOnlyList<IdentityTeam> Teams { get; }

    public IdentityTeam? FindTeam(string key) =>
        _teamsByKey.TryGetValue(key, out var team) ? team : null;

    private static T Read<T>(string path, string description)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, SerializerOptions)
                ?? throw new InvalidOperationException($"{description} at '{path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{description} at '{path}' is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Everything the status logic and the page rely on. Every message names the marker, because
    /// whoever broke it is editing JSON, not reading C#.
    /// </summary>
    internal static void ValidateGoldStandard(GoldStandard standard, string path)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(standard.Version))
        {
            problems.Add("'version' is missing.");
        }

        if (string.IsNullOrWhiteSpace(standard.Title))
        {
            problems.Add("'title' is missing.");
        }

        var rules = standard.StatusRules;
        if (!(rules.DevelopingAtFractionOfMin > 0
              && rules.DevelopingAtFractionOfMin < rules.StrongAlignmentAtFractionOfMin
              && rules.StrongAlignmentAtFractionOfMin < 1))
        {
            problems.Add(
                "'statusRules' needs 0 < developingAtFractionOfMin < strongAlignmentAtFractionOfMin < 1 " +
                $"(found {rules.DevelopingAtFractionOfMin} and {rules.StrongAlignmentAtFractionOfMin}).");
        }

        if (standard.Phases.Count == 0)
        {
            problems.Add("'phases' is empty.");
        }

        foreach (var duplicate in Duplicates(standard.Phases.Select(p => p.Key)))
        {
            problems.Add($"The phase key '{duplicate}' is used more than once.");
        }

        // The club's replaced markers too: a substitute given the key of the marker it replaces
        // would make it look like the club's own.
        var keys = standard.AllMarkers.Select(m => m.Key)
            .Concat(standard.AllMarkers.Where(m => m.IsSubstitute).Select(m => m.Replaces!.Marker.Key));
        foreach (var duplicate in Duplicates(keys))
        {
            problems.Add($"The marker key '{duplicate}' is used more than once.");
        }

        if (standard.AllMarkers.Any(m => m.IsSubstitute) && string.IsNullOrWhiteSpace(standard.Substitutes.Text))
        {
            problems.Add("'substitutes.text' is missing. Both pages explain the substitute markers with it.");
        }

        foreach (var phase in standard.Phases)
        {
            if (string.IsNullOrWhiteSpace(phase.Key) || string.IsNullOrWhiteSpace(phase.Title))
            {
                problems.Add($"A phase is missing 'key' or 'title' (key: '{phase.Key}').");
            }

            if (phase.Markers.Count == 0)
            {
                problems.Add($"Phase '{phase.Key}' has no markers.");
            }

            foreach (var marker in phase.Markers)
            {
                ValidateMarker(marker, problems);
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The Identity Gold Standard at '{path}' has {problems.Count} problem(s):{Environment.NewLine}  - " +
                string.Join($"{Environment.NewLine}  - ", problems));
        }
    }

    private static void ValidateMarker(IdentityMarker marker, List<string> problems)
    {
        var name = string.IsNullOrWhiteSpace(marker.Key) ? $"(no key, name '{marker.Name}')" : marker.Key;

        if (string.IsNullOrWhiteSpace(marker.Key)
            || string.IsNullOrWhiteSpace(marker.Name)
            || string.IsNullOrWhiteSpace(marker.EliteRange)
            || string.IsNullOrWhiteSpace(marker.BestAtIt)
            || string.IsNullOrWhiteSpace(marker.Target.Display))
        {
            problems.Add(
                $"Marker {name} needs 'key', 'name', 'eliteRange', 'bestAtIt' and 'target.display'.");
        }

        var target = marker.Target;
        switch (target.Direction)
        {
            case TargetDirection.HigherIsBetter:
                if (target.Min is not { } min || target.Max is not { } max || min >= max)
                {
                    problems.Add($"Marker {name} is higher-is-better and needs 'target.min' lower than 'target.max'.");
                }
                break;

            case TargetDirection.LowerIsBetter:
                if (target.ExceptionalAtOrBelow is not { } exceptional
                    || target.Max is not { } ceiling
                    || target.StrongAlignmentAtOrBelow is not { } strong
                    || target.DevelopingAtOrBelow is not { } developing
                    || !(exceptional < ceiling && ceiling <= strong && strong <= developing))
                {
                    problems.Add(
                        $"Marker {name} is lower-is-better and needs exceptionalAtOrBelow < max <= " +
                        "strongAlignmentAtOrBelow <= developingAtOrBelow.");
                }
                break;

            default:
                problems.Add(
                    $"Marker {name} has 'target.direction' '{target.DirectionName}'. " +
                    "Use 'higher-is-better' or 'lower-is-better'.");
                break;
        }

        // Exactly one. A marker with both would be measured and explained away at once; a
        // marker with neither would say "Not measured" with no reason given.
        var hasMetric = marker.Measurement.IsMeasured;
        var hasReason = !string.IsNullOrWhiteSpace(marker.Measurement.NotMeasuredReason);
        if (hasMetric == hasReason)
        {
            problems.Add(
                $"Marker {name} needs exactly one of 'measurement.metric' and 'measurement.notMeasuredReason'.");
        }

        // A range that is not the club's is shown as provisional, with where it comes from.
        if (target.Provisional && string.IsNullOrWhiteSpace(target.Basis))
        {
            problems.Add($"Marker {name} has a provisional target and needs 'target.basis' to say where it comes from.");
        }

        if (marker.Replaces is { } replacement)
        {
            ValidateReplacement(marker, name, replacement, problems);
        }
    }

    /// <summary>
    /// A substitute stands in for a club marker the reports cannot answer. So it has to be
    /// measured itself, the club's marker has to be complete -- it is what the page quotes --
    /// and it has to be one that is not measured; otherwise there was nothing to replace.
    /// </summary>
    private static void ValidateReplacement(
        IdentityMarker substitute, string name, MarkerReplacement replacement, List<string> problems)
    {
        var replaced = replacement.Marker;

        if (!substitute.Measurement.IsMeasured)
        {
            problems.Add($"Marker {name} replaces '{replaced.Name}' but is not measured itself.");
        }

        if (string.IsNullOrWhiteSpace(replacement.Rationale))
        {
            problems.Add($"Marker {name} needs 'replaces.rationale': why it is the closest measure to '{replaced.Name}'.");
        }

        ValidateMarker(replaced, problems);

        if (replaced.Measurement.IsMeasured)
        {
            problems.Add(
                $"Marker {name} replaces '{replaced.Key}', which is measured. Move '{replaced.Key}' back out instead.");
        }

        if (replaced.IsSubstitute)
        {
            problems.Add($"Marker {name} replaces '{replaced.Key}', which replaces another marker in turn.");
        }
    }

    internal static void ValidateMatchData(TeamMatchData data, string team, GoldStandard standard, string path)
    {
        var problems = new List<string>();

        if (data.SchemaVersion != SupportedSchemaVersion)
        {
            problems.Add(
                $"'schemaVersion' is {data.SchemaVersion}; this version of the application reads " +
                $"{SupportedSchemaVersion}. Re-run scripts/identity/extract_stats.py.");
        }

        if (!string.Equals(data.Team, team, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'team' is '{data.Team}', but the file is loaded as {team}.");
        }

        foreach (var duplicate in Duplicates(data.Matches.Select(m => m.Id)))
        {
            problems.Add($"The match id '{duplicate}' is used more than once.");
        }

        var metrics = standard.AllMarkers
            .Where(m => m.Measurement.IsMeasured)
            .Select(m => m.Measurement.Metric!)
            .ToList();

        foreach (var match in data.Matches)
        {
            var label = string.IsNullOrWhiteSpace(match.Id) ? $"(no id, file '{match.File}')" : match.Id;

            if (string.IsNullOrWhiteSpace(match.Id) || string.IsNullOrWhiteSpace(match.File))
            {
                problems.Add($"Match {label} needs 'id' and 'file'.");
            }

            // Every number on the page has to lead back to a file and a page. A value without
            // one is not shown with a blank source -- the file is refused.
            foreach (var metric in metrics)
            {
                if (!match.Metrics.TryGetValue(metric, out var value))
                {
                    problems.Add($"Match {label} has no '{metric}', which the Gold Standard measures.");
                }
                else if (!IsSourced(value.Source))
                {
                    problems.Add($"Match {label}: '{metric}' has no source file and page.");
                }
            }

            if (match.Players.Count > 0 && !IsSourced(match.PlayersSource))
            {
                problems.Add($"Match {label} has players but no 'playersSource'.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The {team} match data at '{path}' has {problems.Count} problem(s):{Environment.NewLine}  - " +
                string.Join($"{Environment.NewLine}  - ", problems));
        }
    }

    private static bool IsSourced(SourceReference source) =>
        !string.IsNullOrWhiteSpace(source.File) && source.Page > 0;

    private static IEnumerable<string> Duplicates(IEnumerable<string> keys) =>
        keys.Where(k => !string.IsNullOrWhiteSpace(k))
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
}
