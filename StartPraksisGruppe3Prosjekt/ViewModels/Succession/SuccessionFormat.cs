using System.Globalization;
using StartPraksisGruppe3Prosjekt.Services.Succession;

namespace StartPraksisGruppe3Prosjekt.ViewModels.Succession;

/// <summary>
/// How the succession numbers are written and coloured. One place, so the board, the eleven and
/// the player page say "Ready" and colour a 7.4 the same way.
/// </summary>
public static class SuccessionFormat
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// "30 Jun 2027". English month names whatever the server's culture, the same as the cycle
    /// labels next to it -- a page that says "17 Aug – 11 Oct" above "30. jun. 2027" reads as two.
    /// </summary>
    public static string Date(DateOnly? date) => date?.ToString("d MMM yyyy", English) ?? "–";

    /// <summary>"7.4", or a dash for nothing -- a dash, never 0, which would claim a rating of zero.</summary>
    public static string Number(double? value) => value is { } v ? v.ToString("0.0") : "–";

    /// <summary>
    /// The workbook's 1-10 colour scale as a class: red at 1, yellow at 6, green at 10, one step
    /// per whole point. A class from a fixed set rather than a colour per cell, because the CSP
    /// has no unsafe-inline; see .sc-rate in startcompass.css.
    /// </summary>
    public static string RatingClass(double? value)
    {
        if (value is not { } v)
        {
            return "sc-rate sc-rate--none";
        }

        var step = Math.Clamp((int)Math.Round(v, MidpointRounding.AwayFromZero), 1, 10);

        return $"sc-rate sc-rate--{step}";
    }

    /// <summary>The icon set's three lights, in words and on the score-band classes.</summary>
    public static string LevelName(ReadinessLevel level) => level switch
    {
        ReadinessLevel.Ready => "Ready",
        ReadinessLevel.Developing => "Developing",
        ReadinessLevel.NotYet => "Not yet",
        _ => "Not rated"
    };

    public static string LevelClass(ReadinessLevel level) => level switch
    {
        ReadinessLevel.Ready => "sc-mean sc-mean--strong",
        ReadinessLevel.Developing => "sc-mean sc-mean--mid",
        ReadinessLevel.NotYet => "sc-mean sc-mean--low",
        _ => "sc-mean sc-mean--none"
    };

    /// <summary>
    /// "1.4 off · about 18 weeks". How far from ready, and how long at the rate the player has
    /// been moving -- or why there is no number.
    /// </summary>
    public static string Outlook(ReadinessOutlook outlook) => outlook.Kind switch
    {
        OutlookKind.NoData => "Not rated",
        OutlookKind.ReadyNow => "Ready now",
        OutlookKind.NeedsMoreCycles => $"{Number(outlook.Gap)} off · needs a second cycle for a trend",
        OutlookKind.NotClosing => $"{Number(outlook.Gap)} off · not closing the gap",
        OutlookKind.BeyondHorizon => $"{Number(outlook.Gap)} off · years away at this rate",
        OutlookKind.Weeks => $"{Number(outlook.Gap)} off · about {Weeks(outlook.Weeks!.Value)}",
        _ => "–"
    };

    /// <summary>The short form for a table cell: "18 wks", "Ready", "–".</summary>
    public static string OutlookShort(ReadinessOutlook outlook) => outlook.Kind switch
    {
        OutlookKind.ReadyNow => "Ready",
        OutlookKind.Weeks => $"~{outlook.Weeks} wks",
        OutlookKind.NotClosing => "Not closing",
        OutlookKind.BeyondHorizon => "Years",
        OutlookKind.NeedsMoreCycles => "1 cycle",
        _ => "–"
    };

    /// <summary>"+0.4 per cycle".</summary>
    public static string Trend(double? perCycle) => perCycle switch
    {
        null => "–",
        var p when Math.Abs(p.Value) < 0.05 => "flat",
        var p => $"{(p > 0 ? "+" : "")}{p.Value:0.0} per cycle"
    };

    public static string Weeks(int weeks) => weeks == 1 ? "1 week" : $"{weeks} weeks";

    /// <summary>"RB · RWB · RCB": the positions the coaches named, best first.</summary>
    public static string Positions(IEnumerable<PositionPreference> positions, int take = 3) =>
        string.Join(" · ", positions.Take(take).Select(p => p.Key));

    /// <summary>"1st", "2nd", "3rd".</summary>
    public static string Rank(int rank) => rank switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{rank}th"
    };

    public static string YesOrNo(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "–"
    };

    /// <summary>"2 of 3", or a dash when nobody answered.</summary>
    public static string YesCount(YesNo answers) =>
        answers.Answered == 0 ? "–" : answers.Yes == 0 ? "No" : $"Yes · {answers.Yes} of {answers.Answered}";
}
