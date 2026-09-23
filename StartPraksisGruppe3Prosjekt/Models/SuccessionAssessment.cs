using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Models.Succession;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// One coach's view of one player in one eight-week cycle: a row of the coaches' succession
/// planning workbook.
///
/// ONE ROW PER COACH, never one shared row. The workbook was a template each coach filled in,
/// and the point of bringing it in here was to keep those apart and put them side by side --
/// "compare coaches' responses". A single row that the last coach to save had overwritten
/// would have thrown away exactly the disagreement the page is for. The unique index on
/// (player, rater, cycle) makes rating again inside a cycle a correction.
///
/// What is NOT here, and why:
///
///   * The player's name. The workbook has one; this system has codes, on purpose. See
///     <see cref="Player"/>.
///   * Overall readiness. The workbook's =SUM(N:S)/6 is worked out from the ratings every time
///     it is shown, so it cannot drift from them after a correction.
///   * Contract, contract end, training group. Those are facts about the player, not a coach's
///     opinion, and live once in <see cref="PlayerSuccessionProfile"/>.
///
/// Only coaches and administrators read this -- never the player or a guardian. It is the
/// staff's working judgement, not a message to a teenager. It is part of a data access request
/// all the same, and goes with the player when the player is deleted (cascade).
/// </summary>
public class SuccessionAssessment
{
    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>The coach who made the assessment -- the workbook's "Coach/Coaches (Raters)".</summary>
    [Required]
    [StringLength(450)]
    public string RaterUserId { get; set; } = string.Empty;

    /// <summary>
    /// The first day of the cycle this belongs to. See <see cref="SuccessionCycle"/>: the cycle
    /// itself is worked out, never stored, and this date is enough to find it again.
    /// </summary>
    [Display(Name = "Cycle")]
    public DateOnly CycleStartsOn { get; set; }

    /// <summary>Which version of succession-planning.json the lists came from.</summary>
    [Required]
    [StringLength(100)]
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>When it was first saved or last corrected.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The level the player is judged against -- a key from "levels", e.g. "first-team".</summary>
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Rated as")]
    public string? RatedAs { get; set; }

    /// <summary>A key from "abilityCategories", e.g. "potential".</summary>
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Ability category")]
    public string? AbilityCategory { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "1st position")]
    public string? FirstPosition { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "2nd position")]
    public string? SecondPosition { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "3rd position")]
    public string? ThirdPosition { get; set; }

    /// <summary>
    /// "Coach's personal readiness for next step", 1-10. The coach's own call, next to the
    /// average of the six ratings -- the two are allowed to disagree, and it is worth seeing
    /// when they do.
    /// </summary>
    [Range(1, 10)]
    [Display(Name = "Personal readiness")]
    public int? PersonalReadiness { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "0–6 month projection")]
    public string? Projection0To6Months { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "6–18 month projection")]
    public string? Projection6To18Months { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "18–36 month projection")]
    public string? Projection18To36Months { get; set; }

    /// <summary>Null is "not answered", which is not "no".</summary>
    [Display(Name = "Pathway blocked")]
    public bool? PathwayBlocked { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "What now")]
    public string? WhatNow { get; set; }

    /// <summary>A key from "risks": green, amber, red.</summary>
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Succession risk")]
    public string? SuccessionRisk { get; set; }

    [Display(Name = "External needed")]
    public bool? ExternalNeeded { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "Key development focus")]
    public string? KeyDevelopmentFocus { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "Super strengths")]
    public string? SuperStrengths { get; set; }

    [StringLength(SuccessionRules.NotesLimit)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    /// <summary>The 1-10 ratings, one per entry in "ratings". See <see cref="SuccessionRating"/>.</summary>
    public List<SuccessionRating> Ratings { get; set; } = new();
}

/// <summary>
/// One 1-10 rating in a <see cref="SuccessionAssessment"/>: Physical, Technical and so on.
///
/// A row per rating rather than six columns, for the same reason as the 5C answers: the list is
/// content in succession-planning.json, and a club that adds "Speed" next season should not
/// need a migration to do it.
/// </summary>
public class SuccessionRating
{
    public int Id { get; set; }

    public int AssessmentId { get; set; }
    public SuccessionAssessment? Assessment { get; set; }

    /// <summary>The stable key from the file, e.g. "physical".</summary>
    [Required]
    [StringLength(SuccessionRules.OptionKeyLength)]
    public string RatingKey { get; set; } = string.Empty;

    /// <summary>1-10, as given. Never null: a rating that was not given has no row.</summary>
    [Range(1, 10)]
    public int Value { get; set; }
}

/// <summary>
/// The facts about a player that succession planning needs and the rest of the system does
/// not: contract, when it ends, and which group they train with. One row per player.
///
/// Kept out of <see cref="SuccessionAssessment"/> because they are not opinions -- three
/// coaches entering three contract end dates is two too many, and one of them would be wrong.
/// Kept out of <see cref="Player"/> because nothing outside succession planning reads them.
/// </summary>
public class PlayerSuccessionProfile
{
    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>A key from "contractTypes": pro, youth, non.</summary>
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Contract type")]
    public string? ContractType { get; set; }

    [Display(Name = "Contract ends")]
    public DateOnly? ContractEndsOn { get; set; }

    /// <summary>The MESO training group: a key from "levels".</summary>
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Training group")]
    public string? TrainingGroup { get; set; }

    [Required]
    [StringLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
