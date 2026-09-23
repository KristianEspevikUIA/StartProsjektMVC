using System.Globalization;
using StartPraksisGruppe3Prosjekt.Models.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>
/// Number formatting and the two player-level metrics, in one place, so the table, the
/// insights and the charts print the same value the same way.
/// </summary>
public static class IdentityFormat
{
    /// <summary>The metric keys that also exist per player, in the match data.</summary>
    public const string SuccessfulDribbles = "successfulDribbles";
    public const string Interceptions = "interceptions";

    /// <summary>
    /// "34%" for a match; "34.0%" for an average, so a mean is never mistaken for a count
    /// that was read off a report.
    /// </summary>
    public static string Value(double value, string unit, bool isAverage) =>
        value.ToString(isAverage ? "0.0" : "0.#", CultureInfo.InvariantCulture) + unit;

    /// <summary>
    /// "elite range", or "provisional range" when the range is not the club's. Every sentence
    /// that names a range takes it from here, so a derived range is never called an elite one.
    /// </summary>
    public static string RangeName(IdentityMarker marker) =>
        marker.Target.Provisional ? "provisional range" : "elite range";

    /// <summary>A difference, e.g. "24 percentage points" for a percentage marker, "11" otherwise.</summary>
    public static string Gap(double gap, string unit)
    {
        var number = gap.ToString("0.#", CultureInfo.InvariantCulture);
        return unit == "%" ? $"{number} percentage points" : number;
    }

    /// <summary>The value of a player-level metric for one player, or null for any other metric.</summary>
    public static int? PlayerValue(MatchPlayerLine player, string metric) => metric switch
    {
        SuccessfulDribbles => player.SuccessfulDribbles,
        Interceptions => player.Interceptions,
        _ => null
    };

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
    };

    /// <summary>"13 Jun 2026".</summary>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
