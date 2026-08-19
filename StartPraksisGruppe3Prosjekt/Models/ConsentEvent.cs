using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// APPEND-ONLY hendelseslogg for samtykke. Rader skal aldri endres eller slettes —
/// gjeldende samtykke er den nyeste hendelsen for spilleren (se IConsentService).
/// Historikken er selve dokumentasjonen på at behandlingen har hatt et grunnlag,
/// og på når et samtykke eventuelt ble trukket tilbake.
/// </summary>
public class ConsentEvent
{
    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    [Display(Name = "Samtykkenivå")]
    public ConsentLevel Level { get; set; }

    /// <summary>Identity-bruker-ID til den som endret samtykket (foresatt, spiller eller admin).</summary>
    [Required]
    public string ChangedByUserId { get; set; } = string.Empty;

    [Display(Name = "Tidspunkt")]
    public DateTimeOffset OccurredAt { get; set; }
}
