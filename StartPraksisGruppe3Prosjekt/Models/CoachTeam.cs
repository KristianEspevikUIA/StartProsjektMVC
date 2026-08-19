using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>Kobler en trener (Identity-bruker) til et lag. En trener kan ha flere lag.</summary>
public class CoachTeam
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Trener")]
    public string CoachUserId { get; set; } = string.Empty;

    public int TeamId { get; set; }
    public Team? Team { get; set; }
}
