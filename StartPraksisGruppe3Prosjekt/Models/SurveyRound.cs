using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>En måleperiode, f.eks. "Høst 2026". Svar utenfor vinduet skal avvises.</summary>
public class SurveyRound
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Opens")]
    public DateTimeOffset OpensAt { get; set; }

    [Display(Name = "Closes")]
    public DateTimeOffset ClosesAt { get; set; }

    public ICollection<Response> Responses { get; set; } = new List<Response>();

    public bool IsOpenAt(DateTimeOffset now) => now >= OpensAt && now <= ClosesAt;
}
