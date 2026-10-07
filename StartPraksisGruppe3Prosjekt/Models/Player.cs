using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// En spiller i klubben. Merk at <see cref="UserId"/> er nullbar: en spiller kan være
/// registrert i systemet lenge før hen har fått egen Identity-konto.
/// <see cref="Name"/> er navnet spilleren står med i alle lister i appen.
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>
    /// Spillerens fulle navn, f.eks. "Henrik Kjellevold Skaanes". Unikt: det er navnet
    /// import-players og seedingen kjenner en spiller igjen på. Høyst 50 tegn; klubbens egne
    /// navn er opptil 25.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Identity-bruker-ID. Null til spilleren har fått egen konto, og satt til null igjen av
    /// databasen hvis kontoen slettes (fremmednøkkel med ON DELETE SET NULL, se AppDbContext).
    /// </summary>
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
    public ICollection<ConsentEvent> ConsentEvents { get; set; } = new List<ConsentEvent>();

    /// <summary>Alder i hele år på gitt dato. Grunnlaget for kravet om foresatt.</summary>
    public int AgeAt(DateOnly onDate) => PlayerRules.AgeAt(BirthDate, onDate);
}
