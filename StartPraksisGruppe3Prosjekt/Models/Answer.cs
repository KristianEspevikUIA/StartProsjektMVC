using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>Ett svar på én påstand.</summary>
public class Answer
{
    public int Id { get; set; }

    public int ResponseId { get; set; }
    public Response? Response { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>
    /// 1-5, eller null = "Vet ikke". Null er ikke det samme som 3 —
    /// verdien skal holdes utenfor snitt og avvik, ikke settes til midtpunktet.
    /// </summary>
    [Range(1, 5)]
    [Display(Name = "Answer")]
    public int? Value { get; set; }
}
