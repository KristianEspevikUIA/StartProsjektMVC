using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The admin form for one player's first name and photo -- what the welcome on the front page
/// shows them when they sign in.
///
/// Which player is the route, not a field. The photo is a separate form field (an IFormFile on
/// the action), because it is only posted when it is being replaced.
/// </summary>
public class AdminPlayerDetailsViewModel
{
    // --- shown, not posted ------------------------------------------------------------

    public int PlayerId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string? TeamName { get; set; }

    public bool HasAccount { get; set; }

    public bool HasPhoto { get; set; }

    public string? PhotoContentType { get; set; }

    public int PhotoBytes { get; set; }

    public DateTimeOffset? PhotoUpdatedAt { get; set; }

    /// <summary>For the preview's URL, so a replaced photo is not the cached old one.</summary>
    public long PhotoVersion => PhotoUpdatedAt?.ToUnixTimeSeconds() ?? 0;

    // --- posted -----------------------------------------------------------------------

    [StringLength(PlayerPersonalDetails.FirstNameLimit)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(PlayerPersonalDetails.PhotoSourceLimit)]
    [Display(Name = "Where the photo is from")]
    public string? PhotoSource { get; set; }

    [Display(Name = "Remove the photo")]
    public bool RemovePhoto { get; set; }

    public static AdminPlayerDetailsViewModel For(Player player, PersonalDetailsSummary? summary) => new()
    {
        PlayerId = player.Id,
        Code = player.Code,
        TeamName = player.Team?.Name,
        HasAccount = !string.IsNullOrWhiteSpace(player.UserId),
        FirstName = summary?.FirstName,
        PhotoSource = summary?.PhotoSource,
        HasPhoto = summary?.HasPhoto == true,
        PhotoContentType = summary?.PhotoContentType,
        PhotoBytes = summary?.PhotoBytes ?? 0,
        PhotoUpdatedAt = summary?.PhotoUpdatedAt
    };
}
