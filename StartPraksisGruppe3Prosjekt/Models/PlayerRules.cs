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

    /// <summary>
    /// Alder i hele år på en gitt dato.
    ///
    /// Regelen ligger her og ikke bare på <see cref="Player.AgeAt"/> fordi den også trengs
    /// der det ikke er noen Player å spørre — spillerlista bygges av
    /// <see cref="Services.FiveC.SurveyAssignment"/>, som bærer fødselsdatoen og ikke
    /// entiteten. To kopier av «trekk fra ett år hvis bursdagen ikke har vært» er én kopi
    /// for mye: kravet om foresatt henger på det samme tallet.
    /// </summary>
    public static int AgeAt(DateOnly birthDate, DateOnly onDate)
    {
        var age = onDate.Year - birthDate.Year;

        if (birthDate.AddYears(age) > onDate)
        {
            age--;
        }

        return age;
    }
}
