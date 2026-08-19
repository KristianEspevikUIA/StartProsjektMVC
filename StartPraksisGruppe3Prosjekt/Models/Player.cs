using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// En spiller i klubben. Merk at <see cref="UserId"/> er nullbar: en spiller kan være
/// registrert i systemet lenge før hen har fått egen Identity-konto.
/// Bruk <see cref="Code"/> (f.eks. "TS-03-14") i lister og eksport der det er mulig,
/// slik at færrest mulig navn er i omløp.
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>Klubbintern, pseudonym kode, f.eks. "TS-03-14".</summary>
    [Required]
    [StringLength(20)]
    [Display(Name = "Spillerkode")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Identity-bruker-ID. Null til spilleren har fått egen konto.</summary>
    [Display(Name = "Brukerkonto")]
    public string? UserId { get; set; }

    [Display(Name = "Lag")]
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    [Display(Name = "Fødselsdato")]
    public DateOnly BirthDate { get; set; }

    [StringLength(50)]
    [Display(Name = "Posisjon")]
    public string? Position { get; set; }

    public ICollection<Guardianship> Guardianships { get; set; } = new List<Guardianship>();
    public ICollection<Response> Responses { get; set; } = new List<Response>();
    public ICollection<ConsentEvent> ConsentEvents { get; set; } = new List<ConsentEvent>();

    /// <summary>Alder i hele år på gitt dato. Grunnlaget for kravet om foresatt.</summary>
    public int AgeAt(DateOnly onDate)
    {
        var age = onDate.Year - BirthDate.Year;
        if (BirthDate.AddYears(age) > onDate) age--;
        return age;
    }
}
