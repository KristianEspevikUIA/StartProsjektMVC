using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// En spiller i klubben. Merk at <see cref="UserId"/> er nullbar: en spiller kan være
/// registrert i systemet lenge før hen har fått egen Identity-konto.
/// <see cref="Code"/> er navnet spilleren står med i alle lister i appen.
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>
    /// Spillerens navn, f.eks. "Henrik Kjellevold Skaanes". Heter Code fordi spillerne het koder
    /// ("TS-08-16") før de fikk navn -- kolonnen er den samme. Høyst 50 tegn: den var 20 da
    /// navnene var oppdiktede og korte, og klubbens egne navn er opptil 25.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Name")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Identity-bruker-ID. Null til spilleren har fått egen konto.</summary>
    [Display(Name = "User account")]
    public string? UserId { get; set; }

    [Display(Name = "Team")]
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    [Display(Name = "Date of birth")]
    public DateOnly BirthDate { get; set; }

    [StringLength(50)]
    [Display(Name = "Position")]
    public string? Position { get; set; }

    public ICollection<Guardianship> Guardianships { get; set; } = new List<Guardianship>();
    public ICollection<Response> Responses { get; set; } = new List<Response>();
    public ICollection<ConsentEvent> ConsentEvents { get; set; } = new List<ConsentEvent>();

    /// <summary>Alder i hele år på gitt dato. Grunnlaget for kravet om foresatt.</summary>
    public int AgeAt(DateOnly onDate) => PlayerRules.AgeAt(BirthDate, onDate);
}
