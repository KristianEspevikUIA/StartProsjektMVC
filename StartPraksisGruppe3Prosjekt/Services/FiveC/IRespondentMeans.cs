namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// One slice of the questionnaire as the bar chart draws it: what the player, the guardian
/// and the coach each averaged, on the 1-5 scale after reversal.
///
/// It exists so that <c>_FiveCCategoryChart</c> is written once. A slice is one category
/// for a single player (<see cref="CategoryComparison"/>), or the whole form, one category
/// or one statement for a whole team (<see cref="TeamMeans"/>) -- the same three bars, read
/// the same way, whichever it is.
///
/// Every mean is nullable, and null is not zero: it means nobody usable answered, or -- at
/// team level -- that too few did to show a number. See <see cref="TeamRoleAverage"/>.
/// </summary>
public interface IRespondentMeans
{
    /// <summary>What the slice is called, e.g. "Commitment". Used in the chart's aria-label.</summary>
    string Label { get; }

    /// <summary>The player side, after reversal, or null.</summary>
    double? PlayerMean { get; }

    /// <summary>The guardian side, or null.</summary>
    double? GuardianMean { get; }

    /// <summary>The coach side, or null.</summary>
    double? CoachMean { get; }

    /// <summary>
    /// The player side is low enough, across enough answers, to be worth following up.
    /// Drives the red bar, so the flag is in the chart and not only in a badge above it.
    /// </summary>
    bool NeedsFollowUp { get; }
}
