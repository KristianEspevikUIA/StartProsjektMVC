using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The page in front of deleting a player: what is about to go, and the typed confirmation.
///
/// The counts are here because "delete player 14" says nothing about what that means. This
/// is the one operation in the system that removes consent history, and the admin running it
/// should be able to see that eleven answers and four consent events are inside the cascade
/// before they run it, not afterwards.
///
/// Nothing here identifies anybody but the player the page is about. The counts are numbers,
/// and the code is the club-internal pseudonym the rest of the interface already shows.
/// </summary>
public class AdminDeletePlayerViewModel
{
    public int PlayerId { get; set; }

    /// <summary>The club-internal code, e.g. "TS-08-16". Also what has to be typed back.</summary>
    public string PlayerCode { get; set; } = string.Empty;

    /// <summary>Whether an Identity account is attached, and so goes with the player.</summary>
    public bool HasAccount { get; set; }

    public int GuardianshipCount { get; set; }

    public int ResponseCount { get; set; }

    public int AnswerCount { get; set; }

    public int FiveCSubmissionCount { get; set; }

    /// <summary>
    /// Skrevne refleksjonssvar i 5C-innsendingene. Egen linje fordi det er fritekst: et tall
    /// på hvor mange setninger noen har skrevet om spilleren sier noe annet enn hvor mange
    /// skjemaer som er levert.
    /// </summary>
    public int ReflectionAnswerCount { get; set; }

    public int ConsentEventCount { get; set; }

    public int AccessEventCount { get; set; }

    public int FeedbackReleaseCount { get; set; }

    /// <summary>
    /// What the admin types to confirm. Has to match <see cref="PlayerCode"/>.
    ///
    /// A typed code rather than a second button: the point is to make the operation cost a
    /// deliberate act. Copying eight characters off the page is short enough to be no real
    /// obstacle and long enough that it cannot happen by reflex on the wrong row.
    /// </summary>
    [Display(Name = "Player code")]
    public string ConfirmCode { get; set; } = string.Empty;
}
