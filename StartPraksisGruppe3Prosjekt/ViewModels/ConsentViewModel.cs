using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// Samtykkebildet for én spiller: gjeldende nivå, og hele historikken under.
/// Historikken vises fordi den foresatte skal kunne se hva som har vært gitt og
/// når det eventuelt ble trukket tilbake.
/// </summary>
public class ConsentViewModel
{
    public int PlayerId { get; set; }
    public string PlayerCode { get; set; } = string.Empty;

    public ConsentLevel CurrentLevel { get; set; }

    /// <summary>Om innlogget bruker har lov til å endre nivået.</summary>
    public bool CanChange { get; set; }

    /// <summary>Nyeste først.</summary>
    public List<ConsentEvent> History { get; set; } = new();

    /// <summary>Norsk forklaring på hvert nivå — brukes i skjemaet.</summary>
    public static string Describe(ConsentLevel level) => level switch
    {
        ConsentLevel.None => "Ingen deling. Bare spilleren selv og foresatte ser svarene.",
        ConsentLevel.Aggregated => "Svarene kan inngå i anonyme lagsnitt, men vises ikke som enkeltperson.",
        ConsentLevel.Full => "Treneren kan se spillerens egne svar og avviket.",
        _ => level.ToString()
    };
}
