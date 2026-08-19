namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>Hvem som har svart om en spiller.</summary>
public enum RespondentType
{
    Player = 0,
    Coach = 1,
    Guardian = 2
}

/// <summary>
/// Samtykkenivå for behandling av opplysninger om en spiller.
/// None       = ingen deling ut over spilleren selv og foresatte
/// Aggregated = kan inngå i anonymiserte lagsnitt, men ikke vises som enkeltperson
/// Full       = trener kan se spillerens egne svar og avvik
/// </summary>
public enum ConsentLevel
{
    None = 0,
    Aggregated = 1,
    Full = 2
}
