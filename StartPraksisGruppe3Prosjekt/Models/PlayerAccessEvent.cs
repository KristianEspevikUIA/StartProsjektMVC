using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// APPEND-ONLY audit log: one row every time somebody opens an individual player's answers.
///
/// This exists because coaches no longer need consent to see a player. Consent used to be
/// the boundary between a coach and any given minor's answers; with that gone, the only
/// thing left is being able to say afterwards who looked at whom, and when. A log that can
/// be edited would not be able to say even that, so rows are never changed or deleted.
///
/// It records THAT a lookup happened, never what was concluded from it. No scores, no
/// gaps, no notes -- those are recalculated from the raw answers and are not the log's
/// business.
/// </summary>
public class PlayerAccessEvent
{
    public int Id { get; set; }

    /// <summary>The player who was looked at.</summary>
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>Identity user id of whoever opened the page.</summary>
    [Required]
    [StringLength(450)]
    [Display(Name = "Viewed by")]
    public string ViewedByUserId { get; set; } = string.Empty;

    /// <summary>
    /// The role they were acting in, e.g. "Coach" or "Admin". Stored rather than looked up
    /// later, because roles change and the log has to describe the moment it happened.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Role")]
    public string ViewedByRole { get; set; } = string.Empty;

    /// <summary>
    /// Which view the lookup came from, e.g. "Coach/FiveCPlayer". Enough to tell a coach
    /// reading one player's answers apart from a team list that happens to name them.
    /// </summary>
    [Required]
    [StringLength(100)]
    [Display(Name = "Page")]
    public string Context { get; set; } = string.Empty;

    /// <summary>The round being viewed, when the page is about one. Null otherwise.</summary>
    public int? RoundId { get; set; }
    public SurveyRound? Round { get; set; }

    [Display(Name = "Time")]
    public DateTimeOffset OccurredAt { get; set; }
}
