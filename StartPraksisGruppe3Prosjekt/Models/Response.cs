using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// Ett utfylt skjema i én runde. <see cref="PlayerId"/> er spilleren svarene handler OM —
/// også når det er treneren eller en foresatt som fyller ut.
/// Treneren svarer på hva hen TROR spilleren svarer; avviket regnes ut i ScoringService
/// og lagres aldri.
/// </summary>
public class Response
{
    public int Id { get; set; }

    public int RoundId { get; set; }
    public SurveyRound? Round { get; set; }

    /// <summary>Spilleren svarene handler om.</summary>
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    [Display(Name = "Answered by")]
    public RespondentType Respondent { get; set; }

    /// <summary>Identity-bruker-ID til den som faktisk fylte ut skjemaet.</summary>
    [Required]
    public string RespondentUserId { get; set; } = string.Empty;

    [Display(Name = "Submitted")]
    public DateTimeOffset SubmittedAt { get; set; }

    public List<Answer> Answers { get; set; } = new();
}
