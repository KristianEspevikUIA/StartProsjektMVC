using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// Kobler en foresatt (Identity-bruker) til en spiller.
/// Regel: alle spillere under <see cref="PlayerRules.GuardianRequiredBelowAge"/> år
/// skal ha minst én Guardianship.
///
/// Koblingen går med kontoen: slettes foresattes konto, sletter databasen raden
/// (fremmednøkkel med ON DELETE CASCADE, se AppDbContext).
/// </summary>
public class Guardianship
{
    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    [Required]
    [Display(Name = "Guardian")]
    public string GuardianUserId { get; set; } = string.Empty;
}
