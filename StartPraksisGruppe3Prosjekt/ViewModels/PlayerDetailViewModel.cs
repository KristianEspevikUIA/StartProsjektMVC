using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// Detaljbildet for én spiller: egne svar, trenerens gjetning, og avviket mellom dem.
/// Brukes av CoachController, PlayerController og GuardianController — hva som fylles ut
/// avhenger av hvem som ser.
/// </summary>
public class PlayerDetailViewModel
{
    public int PlayerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string? Position { get; set; }

    public int RoundId { get; set; }
    public string RoundName { get; set; } = string.Empty;

    public ConsentLevel Consent { get; set; }

    /// <summary>Utregnet, aldri lagret. Null hvis en av partene ikke har svart.</summary>
    public PlayerGap? Gap { get; set; }

    /// <summary>Påstandene, i nummerrekkefølge, til visning ved siden av svarene.</summary>
    public List<Item> Items { get; set; } = new();
}
