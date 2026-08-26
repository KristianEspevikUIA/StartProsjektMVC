using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// APPEND-ONLY log of the coach releasing their own answers to the player.
///
/// This is what makes the 5C round a conversation rather than a verdict. The order is:
///
///   1. The player answers about themselves.
///   2. The coach answers about the player. Neither sees the other yet.
///   3. The player is told the coach HAS answered -- not what they answered.
///   4. The coach releases their answers. Only now does the player see the coach's scores
///      and the difference between them.
///
/// The coach always sees everything. The asymmetry is deliberate: a coach reading their own
/// disagreement with a fourteen-year-old is a coaching decision, and the same number arriving
/// unannounced on that player's phone is not.
///
/// Append-only, for the same reason <see cref="ConsentEvent"/> is: a release that was later
/// withdrawn is a thing that happened, and overwriting the row would erase it. Current state
/// is the newest row for (round, player) -- see IFeedbackReleaseService.
/// </summary>
public class FeedbackRelease
{
    public int Id { get; set; }

    /// <summary>The round whose answers are being released.</summary>
    public int RoundId { get; set; }
    public SurveyRound? Round { get; set; }

    /// <summary>The player the answers are about, and who they are released to.</summary>
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>Identity user id of the coach who released or withdrew.</summary>
    [Required]
    [StringLength(450)]
    [Display(Name = "Released by")]
    public string CoachUserId { get; set; } = string.Empty;

    /// <summary>
    /// True for a release, false for a withdrawal. A withdrawal is a NEW row with false,
    /// never an edit of the release it undoes.
    /// </summary>
    [Display(Name = "Released")]
    public bool IsReleased { get; set; }

    [Display(Name = "Time")]
    public DateTimeOffset OccurredAt { get; set; }
}
