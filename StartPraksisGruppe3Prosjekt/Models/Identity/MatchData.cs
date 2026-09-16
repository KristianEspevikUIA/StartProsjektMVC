using System.Text.Json.Serialization;

namespace StartPraksisGruppe3Prosjekt.Models.Identity;

/// <summary>
/// One team's matches, as written by scripts/identity/extract_stats.py to
/// Data/Identity/Matches/&lt;team&gt;.json.
///
/// That folder is not in git. The file names players, most of them minors, and is read by the
/// Identity page only -- which only coaches and administrators can open. See
/// docs/identity-benchmarking.md.
/// </summary>
public sealed class TeamMatchData
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    /// <summary>"U14".</summary>
    [JsonPropertyName("team")]
    public string Team { get; init; } = string.Empty;

    /// <summary>"Start U14", as the reports spell it.</summary>
    [JsonPropertyName("teamName")]
    public string TeamName { get; init; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("generatedBy")]
    public string GeneratedBy { get; init; } = string.Empty;

    [JsonPropertyName("sourceFormat")]
    public string SourceFormat { get; init; } = string.Empty;

    /// <summary>Oldest first.</summary>
    [JsonPropertyName("matches")]
    public IReadOnlyList<IdentityMatch> Matches { get; init; } = Array.Empty<IdentityMatch>();
}

/// <summary>One StatsBomb match report, reduced to what the Identity page shows.</summary>
public sealed class IdentityMatch
{
    /// <summary>Stable id for the URL, e.g. "2026-06-13-home-viking-u14".</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("competition")]
    public string Competition { get; init; } = string.Empty;

    /// <summary>The report's file name. Every value below points back to it.</summary>
    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("homeTeam")]
    public string HomeTeam { get; init; } = string.Empty;

    [JsonPropertyName("awayTeam")]
    public string AwayTeam { get; init; } = string.Empty;

    /// <summary>Whether Start is the left-hand column in the report.</summary>
    [JsonPropertyName("startIsHome")]
    public bool StartIsHome { get; init; }

    [JsonPropertyName("goals")]
    public MatchGoals Goals { get; init; } = new();

    /// <summary>Keyed by <see cref="MarkerMeasurement.Metric"/>.</summary>
    [JsonPropertyName("metrics")]
    public IReadOnlyDictionary<string, SourcedMetric> Metrics { get; init; } =
        new Dictionary<string, SourcedMetric>();

    [JsonPropertyName("players")]
    public IReadOnlyList<MatchPlayerLine> Players { get; init; } = Array.Empty<MatchPlayerLine>();

    /// <summary>The appendix page the player rows were read from.</summary>
    [JsonPropertyName("playersSource")]
    public SourceReference PlayersSource { get; init; } = new();

    [JsonIgnore]
    public string Opponent => StartIsHome ? AwayTeam : HomeTeam;
}

public sealed class MatchGoals
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("opponent")]
    public int Opponent { get; init; }

    [JsonPropertyName("source")]
    public SourceReference Source { get; init; } = new();
}

/// <summary>A number from a report, with where it came from and how it was arrived at.</summary>
public sealed class SourcedMetric
{
    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("source")]
    public SourceReference Source { get; init; } = new();

    /// <summary>In words, as the extraction script computed it. Shown on the page.</summary>
    [JsonPropertyName("formula")]
    public string Formula { get; init; } = string.Empty;
}

/// <summary>
/// One Start player in one match. Only the two columns the Player Highlight uses -- nothing
/// else about a player is extracted.
/// </summary>
public sealed class MatchPlayerLine
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("successfulDribbles")]
    public int SuccessfulDribbles { get; init; }

    [JsonPropertyName("interceptions")]
    public int Interceptions { get; init; }
}
