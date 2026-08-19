namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>Regler som gjelder på tvers av modell, tjenester og validering.</summary>
public static class PlayerRules
{
    /// <summary>Spillere under denne alderen må ha minst én registrert foresatt.</summary>
    public const int GuardianRequiredBelowAge = 19;

    /// <summary>Laveste gyldige verdi på svarskalaen.</summary>
    public const int ScaleMin = 1;

    /// <summary>Høyeste gyldige verdi på svarskalaen.</summary>
    public const int ScaleMax = 5;

    /// <summary>
    /// Reversert skåring: en påstand som er negativt formulert snus, slik at høy verdi
    /// alltid betyr "bra". Med skala 1-5 blir det (6 - verdi).
    /// </summary>
    public const int ReverseScoreBase = ScaleMin + ScaleMax;
}
