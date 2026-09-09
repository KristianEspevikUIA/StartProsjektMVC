using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// APPEND-ONLY: one row every time an administrator deletes a player.
///
/// A table of its own rather than a <see cref="PlayerAccessEvent"/> row, and the reason is
/// the cascade. Every log that points at a player -- the access log, the consent log, the
/// feedback releases -- has a cascading foreign key to Player, precisely so that erasing a
/// player erases what is held about them. A trace written to any of those would therefore be
/// deleted by the very operation it exists to document: the deletion would erase its own
/// receipt, and afterwards nothing would show that it ever happened.
///
/// So there is no foreign key here. <see cref="PlayerId"/> is a plain number, kept as the
/// reference the club can quote afterwards, and by design it resolves to nothing.
///
/// What is deliberately NOT here: the player code, the team, the birth date, how many
/// answers were removed. A deletion log that describes the person is a copy of the data the
/// deletion was meant to remove, and it would outlive every retention rule in the system.
/// Who deleted, when, and which row id. Nothing more.
/// </summary>
public class PlayerDeletionEvent
{
    public int Id { get; set; }

    /// <summary>
    /// The <c>Player.Id</c> that was deleted. Not a foreign key: the row it named is gone,
    /// and that is the point.
    /// </summary>
    public int PlayerId { get; set; }

    /// <summary>Identity user id of the administrator who ran the deletion.</summary>
    [Required]
    [StringLength(450)]
    [Display(Name = "Deleted by")]
    public string DeletedByUserId { get; set; } = string.Empty;

    [Display(Name = "Time")]
    public DateTimeOffset OccurredAt { get; set; }
}
