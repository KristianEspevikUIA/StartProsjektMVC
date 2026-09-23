using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// The player's first name and photo, for one thing only: welcoming them by name when they
/// sign in. One row per player, entered by an administrator.
///
/// Kept apart from <see cref="Player"/> on purpose. Everywhere else in the system -- the coach
/// pages, the team lists, the succession board -- a player goes by the full name in
/// <see cref="Player.Code"/>. The one other use of the first name is the coaches' best eleven,
/// which puts it on each shirt because the coaches asked for it (see SuccessionController's
/// DisplayNamesAsync). Nothing reads this table except through IPlayerWelcomeService, so the
/// photo cannot turn up on a page by accident.
///
/// A first name and not a full name: "Welcome, Alex" needs no more, and less is less to
/// lose. The photo is the club's official squad photo, published by the club and used here
/// with its permission (see docs/player-welcome.md).
///
/// Shown to the player themselves, and to an administrator on the page where it is entered.
/// The first name, not the photo, to coaches on the best eleven. Not to guardians. It goes with the
/// player when the player is deleted (cascade), and is part of a data access request.
/// </summary>
public class PlayerPersonalDetails
{
    /// <summary>The longest first name the form takes.</summary>
    public const int FirstNameLimit = 50;

    /// <summary>Where the photo came from, e.g. "ikstart.no, squad photos 2026".</summary>
    public const int PhotoSourceLimit = 200;

    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    [StringLength(FirstNameLimit)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    /// <summary>
    /// The photo as stored: already checked and stripped of metadata by
    /// <see cref="Services.PlayerPhotoRules"/>. Null when there is none.
    /// </summary>
    public byte[]? Photo { get; set; }

    /// <summary>"image/jpeg", "image/png" or "image/webp" -- what the bytes were found to be, not what the upload claimed.</summary>
    [StringLength(30)]
    public string? PhotoContentType { get; set; }

    [StringLength(PhotoSourceLimit)]
    [Display(Name = "Photo source")]
    public string? PhotoSource { get; set; }

    /// <summary>When the photo was last replaced. Part of the photo's URL, so a new photo is not served from cache.</summary>
    public DateTimeOffset? PhotoUpdatedAt { get; set; }

    [Required]
    [StringLength(450)]
    public string UpdatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
