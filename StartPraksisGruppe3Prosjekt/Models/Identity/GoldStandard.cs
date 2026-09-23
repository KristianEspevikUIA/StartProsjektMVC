using System.Text.Json.Serialization;

namespace StartPraksisGruppe3Prosjekt.Models.Identity;

/// <summary>
/// The IK Start Identity Gold Standard, as written in Data/Identity/gold-standard.json.
///
/// Content, not state -- the same arrangement as the 5C question set. The wording of the
/// club's markers, ranges and footnotes is transcribed from the club's Gold Standard PDF; the
/// substitutes standing in for the six a match report cannot answer are ours, and say so. No
/// view holds a number or a label of its own: change the file, and every page follows.
/// </summary>
public sealed class GoldStandard
{
    /// <summary>Which transcription of the document this is, e.g. "2026-09-16".</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>The PDF the markers were transcribed from.</summary>
    [JsonPropertyName("source")]
    public SourceReference Source { get; init; } = new();

    /// <summary>"Identity Benchmarking" -- the small overline above the title.</summary>
    [JsonPropertyName("eyebrow")]
    public string Eyebrow { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>"IK Start".</summary>
    [JsonPropertyName("organisation")]
    public string Organisation { get; init; } = string.Empty;

    /// <summary>"Academy Methodology".</summary>
    [JsonPropertyName("methodology")]
    public string Methodology { get; init; } = string.Empty;

    [JsonPropertyName("intro")]
    public string Intro { get; init; } = string.Empty;

    [JsonPropertyName("referenceClubs")]
    public IReadOnlyList<string> ReferenceClubs { get; init; } = Array.Empty<string>();

    /// <summary>The "Why these are the gold standards" box.</summary>
    [JsonPropertyName("why")]
    public GoldStandardNote Why { get; init; } = new();

    /// <summary>
    /// What a substitute marker is, and where its provisional range comes from. Ours, not the
    /// club's -- see <see cref="IdentityMarker.Replaces"/>.
    /// </summary>
    [JsonPropertyName("substitutes")]
    public GoldStandardNote Substitutes { get; init; } = new();

    [JsonPropertyName("statusRules")]
    public StatusRules StatusRules { get; init; } = new();

    /// <summary>In Possession, then Out of Possession -- in the order the document has them.</summary>
    [JsonPropertyName("phases")]
    public IReadOnlyList<IdentityPhase> Phases { get; init; } = Array.Empty<IdentityPhase>();

    /// <summary>The markers on the page, substitutes included, in the document's order.</summary>
    [JsonIgnore]
    public IEnumerable<IdentityMarker> AllMarkers => Phases.SelectMany(p => p.Markers);

    /// <summary>
    /// The club's own ten, in the document's order: each marker on the page, or the club marker
    /// a substitute replaced in that place.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<IdentityMarker> ClubMarkers => AllMarkers.Select(m => m.Replaces?.Marker ?? m);
}

/// <summary>A file and a page. Every number on the Identity page carries one.</summary>
public sealed class SourceReference
{
    [JsonPropertyName("file")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("page")]
    public int Page { get; init; }
}

public sealed class GoldStandardNote
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// The adjustable half of the status logic for markers where higher is better. The Elite
/// range itself is not here -- that is the marker's own <see cref="MarkerTarget"/>.
/// </summary>
public sealed class StatusRules
{
    /// <summary>At or above this share of the range's floor is Strong Alignment, e.g. 0.90.</summary>
    [JsonPropertyName("strongAlignmentAtFractionOfMin")]
    public double StrongAlignmentAtFractionOfMin { get; init; }

    /// <summary>At or above this share of the range's floor is Developing, e.g. 0.75.</summary>
    [JsonPropertyName("developingAtFractionOfMin")]
    public double DevelopingAtFractionOfMin { get; init; }
}

/// <summary>In Possession or Out of Possession.</summary>
public sealed class IdentityPhase
{
    /// <summary>"in-possession" or "out-of-possession". Also picks the section's colours.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string Tagline { get; init; } = string.Empty;

    [JsonPropertyName("markers")]
    public IReadOnlyList<IdentityMarker> Markers { get; init; } = Array.Empty<IdentityMarker>();

    [JsonPropertyName("footnotes")]
    public IReadOnlyList<GoldStandardFootnote> Footnotes { get; init; } = Array.Empty<GoldStandardFootnote>();
}

/// <summary>
/// One of the ten identity markers: the club's own, or a substitute standing in for one of the
/// club's that no StatsBomb report can answer.
/// </summary>
public sealed class IdentityMarker
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>As printed, e.g. "PPDA (Intensity)".</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>As printed, e.g. "50 – 112 / match". Shown on the Gold Standard reference.</summary>
    [JsonPropertyName("eliteRange")]
    public string EliteRange { get; init; } = string.Empty;

    /// <summary>As printed, e.g. "Malmö FF (peak 112)".</summary>
    [JsonPropertyName("bestAtIt")]
    public string BestAtIt { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    public MarkerTarget Target { get; init; } = new();

    [JsonPropertyName("measurement")]
    public MarkerMeasurement Measurement { get; init; } = new();

    /// <summary>The club's marker this one stands in for. Null for the club's own markers.</summary>
    [JsonPropertyName("replaces")]
    public MarkerReplacement? Replaces { get; init; }

    [JsonIgnore]
    public bool IsSubstitute => Replaces is not null;
}

/// <summary>
/// A club marker that no StatsBomb report can answer, kept exactly as it was under the
/// substitute that took its place -- so the club's document is still all there, and moving it
/// back out restores it.
/// </summary>
public sealed class MarkerReplacement
{
    /// <summary>The club's marker, word for word, with its reason for not being measured.</summary>
    [JsonPropertyName("marker")]
    public IdentityMarker Marker { get; init; } = new();

    /// <summary>Why the substitute is the closest measure the report has.</summary>
    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = string.Empty;
}

public enum TargetDirection
{
    HigherIsBetter,
    LowerIsBetter
}

/// <summary>
/// The Elite range as numbers. Not in the PDF as such -- it is the printed range, read so a
/// status can be worked out from it.
/// </summary>
public sealed class MarkerTarget
{
    /// <summary>"higher-is-better" or "lower-is-better".</summary>
    [JsonPropertyName("direction")]
    public string DirectionName { get; init; } = string.Empty;

    [JsonIgnore]
    public TargetDirection? Direction => DirectionName switch
    {
        "higher-is-better" => TargetDirection.HigherIsBetter,
        "lower-is-better" => TargetDirection.LowerIsBetter,
        _ => null
    };

    /// <summary>Floor of the Elite range. Null for PPDA, which only has a ceiling.</summary>
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    /// <summary>
    /// Ceiling of the Elite range. For a higher-is-better marker, above it is Exceptional; for
    /// PPDA, below it is Elite Alignment.
    /// </summary>
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    /// <summary>"%" for the percentage markers, otherwise empty.</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    /// <summary>The short form in the Target column, e.g. "58–70%" or "&lt;10.0".</summary>
    [JsonPropertyName("display")]
    public string Display { get; init; } = string.Empty;

    // Lower is better only. Absolute values rather than shares of the range: a PPDA range has
    // no floor to take a share of.

    [JsonPropertyName("exceptionalAtOrBelow")]
    public double? ExceptionalAtOrBelow { get; init; }

    [JsonPropertyName("strongAlignmentAtOrBelow")]
    public double? StrongAlignmentAtOrBelow { get; init; }

    [JsonPropertyName("developingAtOrBelow")]
    public double? DevelopingAtOrBelow { get; init; }

    /// <summary>
    /// True when the range is not the club's but derived from the match reports, until the club
    /// sets its own. Wherever the range is shown, the page says so.
    /// </summary>
    [JsonPropertyName("provisional")]
    public bool Provisional { get; init; }

    /// <summary>Where a provisional range comes from, in words. Required when it is provisional.</summary>
    [JsonPropertyName("basis")]
    public string Basis { get; init; } = string.Empty;
}

/// <summary>
/// What answers a marker in the match data -- or, when nothing does, why not. Exactly one of
/// the two is set; the catalog refuses a file where that is not so.
/// </summary>
public sealed class MarkerMeasurement
{
    /// <summary>Key in <see cref="IdentityMatch.Metrics"/>, e.g. "possessionPct".</summary>
    [JsonPropertyName("metric")]
    public string? Metric { get; init; }

    /// <summary>Shown as the footnote to "Not measured".</summary>
    [JsonPropertyName("notMeasuredReason")]
    public string? NotMeasuredReason { get; init; }

    [JsonIgnore]
    public bool IsMeasured => !string.IsNullOrWhiteSpace(Metric);
}

public sealed class GoldStandardFootnote
{
    /// <summary>The bold word, e.g. "PPDA".</summary>
    [JsonPropertyName("term")]
    public string Term { get; init; } = string.Empty;

    /// <summary>"=" or "—", as printed after the term.</summary>
    [JsonPropertyName("separator")]
    public string Separator { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
