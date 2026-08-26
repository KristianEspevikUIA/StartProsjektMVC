using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// The 5C round as the player -- or their guardian -- sees it.
///
/// This is the player's half of the conversation, and it moves through four stages:
///
///   <see cref="FeedbackStage.NotAnswered"/>   nothing submitted yet; the page is a button.
///   <see cref="FeedbackStage.WaitingForCoach"/> answers in, coach has not answered.
///   <see cref="FeedbackStage.CoachAnswered"/>  coach has answered but not shared yet.
///   <see cref="FeedbackStage.Released"/>       the coach shared; the comparison is visible.
///
/// The third stage is the point of the whole feature. The player is told the coach HAS
/// answered without being told what they said, so the numbers arrive in a conversation
/// rather than as a notification.
///
/// <see cref="Comparison"/> is REDACTED before it reaches the view in every stage but the
/// last -- see <see cref="Redact"/>. The view is not trusted to remember to hide things.
/// </summary>
public class FiveCFeedbackViewModel
{
    public int PlayerId { get; set; }

    public string PlayerCode { get; set; } = string.Empty;

    public string TeamName { get; set; } = string.Empty;

    /// <summary>True when a guardian is looking at their child rather than a player at themselves.</summary>
    public bool ViewerIsGuardian { get; set; }

    public int RoundId { get; set; }

    public string RoundName { get; set; } = string.Empty;

    public bool RoundIsOpen { get; set; }

    public DateTimeOffset RoundClosesAt { get; set; }

    /// <summary>Every period, for the picker. Older ones are readable, not answerable.</summary>
    public IReadOnlyList<FiveCTeamViewModel.RoundOption> Rounds { get; set; } =
        Array.Empty<FiveCTeamViewModel.RoundOption>();

    /// <summary>
    /// The scores. Coach figures are stripped out unless the coach has released them, so
    /// nothing in the view can leak them by accident.
    /// </summary>
    public PlayerFiveCComparison? Comparison { get; set; }

    /// <summary>Whether the coach has submitted. Shown even before they release.</summary>
    public bool CoachHasAnswered { get; set; }

    /// <summary>Whether the coach has shared their answers with the player.</summary>
    public bool CoachAnswersReleased { get; set; }

    /// <summary>Whether this viewer can still fill the form in for this period.</summary>
    public bool CanFillIn { get; set; }

    /// <summary>The role this viewer answers in: Player for themselves, Guardian for a child.</summary>
    public RespondentType FillRole { get; set; }

    public bool HasAnswered => Comparison?.PlayerSubmittedAt is not null || ViewerHasAnswered;

    /// <summary>Whether THIS viewer has submitted, which for a guardian is their own form.</summary>
    public bool ViewerHasAnswered { get; set; }

    public FeedbackStage Stage
    {
        get
        {
            if (!ViewerHasAnswered) return FeedbackStage.NotAnswered;
            if (!CoachHasAnswered) return FeedbackStage.WaitingForCoach;
            return CoachAnswersReleased ? FeedbackStage.Released : FeedbackStage.CoachAnswered;
        }
    }

    /// <summary>
    /// Removes everything the coach said from a comparison.
    ///
    /// Called whenever the coach has not released. Redacting the MODEL rather than hiding
    /// it in the view means a new page, a new partial or a stray debug dump cannot show
    /// what it should not -- there is nothing there to show.
    /// </summary>
    public static PlayerFiveCComparison Redact(PlayerFiveCComparison comparison) =>
        comparison with
        {
            Categories = comparison.Categories
                .Select(c => c with { CoachMean = null, CoachAnswered = 0 })
                .ToList(),

            // The coach's submission time is kept: "your coach has answered" is exactly
            // what the player is allowed to know at this stage.
            Differences = DifferenceScores.None
        };
}

/// <summary>Where in the conversation this player is.</summary>
public enum FeedbackStage
{
    /// <summary>Nothing submitted yet.</summary>
    NotAnswered = 0,

    /// <summary>The viewer has answered; the coach has not.</summary>
    WaitingForCoach = 1,

    /// <summary>The coach has answered but has not shared yet.</summary>
    CoachAnswered = 2,

    /// <summary>The coach shared. The comparison is visible.</summary>
    Released = 3
}
