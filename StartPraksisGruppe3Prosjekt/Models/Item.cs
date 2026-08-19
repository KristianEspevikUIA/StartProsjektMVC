using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// En av de ti påstandene. Skala 1-5. <see cref="IsReversed"/> betyr at påstanden er
/// negativt formulert og skåres som (6 - verdi) — se ScoringService.
/// </summary>
public class Item
{
    public int Id { get; set; }

    [Range(1, 10)]
    [Display(Name = "Nummer")]
    public int Number { get; set; }

    [Required]
    [StringLength(300)]
    [Display(Name = "Påstand")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Området påstanden måler: Rolleforståelse, Trygghet eller Mestring.</summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Område")]
    public string Construct { get; set; } = string.Empty;

    [Display(Name = "Reversert")]
    public bool IsReversed { get; set; }

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
}
