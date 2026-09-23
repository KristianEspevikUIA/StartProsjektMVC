using System.Globalization;

namespace StartPraksisGruppe3Prosjekt.Models.Succession;

/// <summary>
/// One eight-week rating cycle: the coaches' "every 8 weeks".
///
/// Cycles are worked out from a date, never stored. The first one starts on
/// <see cref="CycleSettings.FirstCycleStartsOn"/> and each follows on from the last without a
/// gap, so any date belongs to exactly one cycle and no admin page is needed to open the
/// next one. An assessment stores <see cref="StartsOn"/>, which is all it takes to put it back
/// in its cycle -- and to keep it there if the cycle length is changed later.
/// </summary>
public sealed record SuccessionCycle(int Number, DateOnly StartsOn, DateOnly EndsOn)
{
    /// <summary>
    /// The cycle a date falls in. Dates before the first cycle count backwards from it --
    /// cycle 0, -1 and so on -- rather than failing, so a clock set wrong or an old import
    /// still lands somewhere a person can see it.
    /// </summary>
    public static SuccessionCycle Of(DateOnly date, CycleSettings settings)
    {
        var length = settings.LengthWeeks * 7;
        var offset = date.DayNumber - settings.FirstCycleStartsOn.DayNumber;

        // Floor division: -1 day is the last day of cycle 0, not the first of cycle 1.
        var index = offset >= 0 ? offset / length : -((-offset + length - 1) / length);

        var startsOn = settings.FirstCycleStartsOn.AddDays(index * length);

        return new SuccessionCycle(index + 1, startsOn, startsOn.AddDays(length - 1));
    }

    /// <summary>The cycle before this one.</summary>
    public SuccessionCycle Previous(CycleSettings settings) => Of(StartsOn.AddDays(-1), settings);

    public bool Contains(DateOnly date) => date >= StartsOn && date <= EndsOn;

    /// <summary>
    /// "17 Aug – 11 Oct 2026". Dates rather than "cycle 5": the number means nothing to a coach,
    /// and the dates say at once whether it is the one they are thinking of. English month
    /// names like the rest of the interface, whatever the server's culture.
    /// </summary>
    public string Label
    {
        get
        {
            var english = CultureInfo.GetCultureInfo("en-GB");

            var start = StartsOn.Year == EndsOn.Year
                ? StartsOn.ToString("d MMM", english)
                : StartsOn.ToString("d MMM yyyy", english);

            return $"{start} – {EndsOn.ToString("d MMM yyyy", english)}";
        }
    }

    /// <summary>The value in a URL: "2026-08-17". Invariant, so a link means the same everywhere.</summary>
    public string Key => StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Reads <see cref="Key"/> back. Any date inside a cycle is accepted and snapped to it.</summary>
    public static SuccessionCycle? TryParse(string? value, CycleSettings settings) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Of(date, settings)
            : null;
}
