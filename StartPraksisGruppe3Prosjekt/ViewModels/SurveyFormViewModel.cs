using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// Skjemaet med de ti påstandene. Samme skjema brukes av alle tre respondenttypene;
/// det er ledeteksten som skifter:
///   Player   — "Jeg vet hva som forventes av meg ..."
///   Coach    — "Spilleren vet hva som forventes av seg ..." (hva treneren TROR)
///   Guardian — samme perspektiv som treneren, sett fra foresatt
///
/// TODO (Victor): ordlyden skal snus i visningen, ikke i databasen. Item.Text er
/// spillerens formulering og er fasit.
/// </summary>
public class SurveyFormViewModel
{
    public int RoundId { get; set; }
    public string RoundName { get; set; } = string.Empty;

    /// <summary>Spilleren skjemaet handler om.</summary>
    public int PlayerId { get; set; }
    public string PlayerCode { get; set; } = string.Empty;

    /// <summary>Hvem som fyller ut. Avgjør ledetekst og hvilken Response som lagres.</summary>
    public RespondentType Respondent { get; set; }

    public List<ItemAnswerInput> Answers { get; set; } = new();

    public class ItemAnswerInput
    {
        public int ItemId { get; set; }
        public int Number { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Construct { get; set; } = string.Empty;

        /// <summary>
        /// 1-5, eller null for "Vet ikke". Null er et gyldig svar og skal kunne lagres —
        /// ikke gjør feltet påkrevd.
        /// </summary>
        [Range(PlayerRules.ScaleMin, PlayerRules.ScaleMax,
            ErrorMessage = "Velg en verdi mellom 1 og 5, eller \"Vet ikke\".")]
        [Display(Name = "Svar")]
        public int? Value { get; set; }
    }
}
