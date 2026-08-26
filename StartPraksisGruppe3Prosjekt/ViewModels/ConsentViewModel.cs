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

    /// <summary>Plain-language explanation of each level, shown in the consent form.</summary>
    public static string Describe(ConsentLevel level) => level switch
    {
        ConsentLevel.None => "No sharing. Only the player and their guardians see the answers.",
        ConsentLevel.Aggregated => "The answers may go into anonymous team averages, but are not shown for the individual.",
        ConsentLevel.Full => "The coach can see the player's own answers and the gap.",
        _ => level.ToString()
    };

    /// <summary>Short label for the same level, for a badge or a table cell.</summary>
    public static string ShortName(ConsentLevel level) => level switch
    {
        ConsentLevel.None => "No sharing",
        ConsentLevel.Aggregated => "Aggregated only",
        ConsentLevel.Full => "Full sharing",
        _ => level.ToString()
    };
}
