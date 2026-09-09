namespace StartPraksisGruppe3Prosjekt.Models.FiveC;

/// <summary>
/// The fixed set of colours a question can be marked with in the UI.
///
/// A FIXED SET, and not a hex value in the question file, because the CSP has no
/// unsafe-inline: a colour that varies per row cannot arrive as style="#a06c14". Every
/// name below has a class in startcompass.css, and the file picks one of them by name --
/// which also means a question set cannot introduce an unreadable colour combination.
///
/// The marker exists because the form no longer groups the statements by category -- see
/// <see cref="Services.FiveC.IQuestionOrder"/>. With twenty-five statements shuffled into
/// one sequence, the colour is what tells a reader that two statements belong together,
/// and what makes a single statement recognisable again on the overview pages.
///
/// The names deliberately avoid "red", "amber" and "green". Those three mean a score band
/// elsewhere in the UI (<see cref="ScoreLevels"/>), and a question marked "red" sitting
/// next to a number coloured red would read as a judgement about the answer.
/// </summary>
public static class QuestionColors
{
    /// <summary>Used for an unmarked question when there is no category to fall back to.</summary>
    public const string Fallback = "slate";

    /// <summary>
    /// The palette, in the order it is handed out. The first five cover the five C's, so
    /// a question file that names no colours at all still gets five distinct markers.
    /// </summary>
    public static readonly IReadOnlyList<string> Palette = new[]
    {
        "indigo",
        "teal",
        "plum",
        "rust",
        "moss",
        "sky",
        "sand",
        "slate"
    };

    /// <summary>Whether the file may use this name. Case-insensitive; the classes are lower-case.</summary>
    public static bool IsKnown(string? name) =>
        name is not null
        && Palette.Contains(name.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// The colour a category gets when the file does not name one. Wraps round rather than
    /// running out, so a question set with more categories than colours still renders.
    /// </summary>
    public static string ByIndex(int index) =>
        Palette[((index % Palette.Count) + Palette.Count) % Palette.Count];

    /// <summary>The name as it is written in the classes, or the fallback for anything unknown.</summary>
    public static string Normalise(string? name) =>
        IsKnown(name) ? name!.Trim().ToLowerInvariant() : Fallback;

    /// <summary>The full class for a marker, e.g. "sc-qcolor sc-qcolor--teal".</summary>
    public static string CssClass(string? name) => $"sc-qcolor sc-qcolor--{Normalise(name)}";

    /// <summary>Every valid name in one string, for the error the catalog raises on a typo.</summary>
    public static string Names => string.Join(", ", Palette);
}
