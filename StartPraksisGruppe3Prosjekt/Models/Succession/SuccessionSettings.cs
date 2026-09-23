namespace StartPraksisGruppe3Prosjekt.Models.Succession;

/// <summary>
/// Data/Succession/succession-planning.json, as read. Every list, threshold and formation the
/// succession pages use comes from here -- see <see cref="Services.Succession.SuccessionCatalog"/>,
/// which loads it once and refuses to start on a mistake.
/// </summary>
public sealed class SuccessionSettings
{
    /// <summary>Stored on every assessment, so an assessment made against an older list can be told apart.</summary>
    public string Version { get; init; } = string.Empty;

    public CycleSettings Cycle { get; init; } = new();

    public ScaleSettings Scale { get; init; } = new();

    /// <summary>Overall readiness at or above this is "Ready". The workbook's green light.</summary>
    public double ReadyAt { get; init; }

    /// <summary>At or above this, and below <see cref="ReadyAt"/>, is "Developing". The amber light.</summary>
    public double DevelopingAt { get; init; }

    /// <summary>Coaches this many points apart on one rating are flagged as disagreeing.</summary>
    public double DisagreementAt { get; init; }

    /// <summary>How far ahead "weeks until ready" is willing to extrapolate.</summary>
    public int HorizonWeeks { get; init; }

    /// <summary>Readiness marked down per position rank when picking the eleven: [1st, 2nd, 3rd].</summary>
    public IReadOnlyList<double> PositionRankPenalty { get; init; } = Array.Empty<double>();

    public IReadOnlyList<SuccessionOption> Ratings { get; init; } = Array.Empty<SuccessionOption>();

    public IReadOnlyList<SuccessionOption> Positions { get; init; } = Array.Empty<SuccessionOption>();

    public IReadOnlyList<FormationDefinition> Formations { get; init; } = Array.Empty<FormationDefinition>();

    public IReadOnlyList<SuccessionOption> AbilityCategories { get; init; } = Array.Empty<SuccessionOption>();

    public IReadOnlyList<SuccessionOption> ContractTypes { get; init; } = Array.Empty<SuccessionOption>();

    public IReadOnlyList<SuccessionOption> Levels { get; init; } = Array.Empty<SuccessionOption>();

    /// <summary>Least severe first. The order is what "the most severe risk named" is measured by.</summary>
    public IReadOnlyList<SuccessionOption> Risks { get; init; } = Array.Empty<SuccessionOption>();
}

public sealed class CycleSettings
{
    public int LengthWeeks { get; init; }

    /// <summary>The first day of cycle one. Every cycle after it follows on without a gap.</summary>
    public DateOnly FirstCycleStartsOn { get; init; }
}

public sealed class ScaleSettings
{
    public int Min { get; init; }

    public int Max { get; init; }
}

/// <summary>One entry in a list: a stored key, a shown name, and for some lists a tone.</summary>
public sealed class SuccessionOption
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>A name from <see cref="SuccessionTones"/>, or null for a list that is not coloured.</summary>
    public string? Tone { get; init; }
}

/// <summary>
/// A formation, as rows on the pitch: the attack first, the goalkeeper last, each row left to
/// right as the team attacks. The slots are the position keys; the order is how they are drawn.
/// </summary>
public sealed class FormationDefinition
{
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<IReadOnlyList<string>> Lines { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>Every slot, top row first. Eleven for a real formation; the catalog checks.</summary>
    public IEnumerable<string> Slots => Lines.SelectMany(line => line);

    /// <summary>
    /// "1-3-5-2": the name with the goalkeeper counted, the way some coaches write it. Null when
    /// the name is not a row of numbers, or the last row is not the one keeper it would count.
    /// </summary>
    public string? GoalkeeperNotation =>
        Lines.Count > 0 && Lines[^1].Count == 1 && NumberedName.IsMatch(Name) ? $"1-{Name}" : null;

    private static readonly System.Text.RegularExpressions.Regex NumberedName =
        new(@"^\d+(-\d+)+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>
/// The fixed set of tones a category, contract type or risk can be shown in.
///
/// A FIXED SET, for the same reason as <see cref="FiveC.QuestionColors"/>: the CSP has no
/// unsafe-inline, so every tone is a class in startcompass.css and the file names one of them.
/// Unlike the question markers these do mean good-to-bad -- the workbook colours Squad red and
/// a Pro contract green, and the coaches read them that way -- so the tone is always next to
/// the name in words, never on its own.
/// </summary>
public static class SuccessionTones
{
    public const string Fallback = "grey";

    public static readonly IReadOnlyList<string> All = new[] { "blue", "green", "amber", "red", "grey" };

    public static bool IsKnown(string? tone) =>
        tone is not null && All.Contains(tone.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>The full class, e.g. "sc-tone sc-tone--green".</summary>
    public static string CssClass(string? tone) =>
        $"sc-tone sc-tone--{(IsKnown(tone) ? tone!.Trim().ToLowerInvariant() : Fallback)}";
}
