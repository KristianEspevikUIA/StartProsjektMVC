using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

public class Team
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Team name")]
    public string Name { get; set; } = string.Empty;

    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<CoachTeam> CoachTeams { get; set; } = new List<CoachTeam>();
}
