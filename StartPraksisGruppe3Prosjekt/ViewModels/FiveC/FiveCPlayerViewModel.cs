using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// One player's 5C round: what the player, the guardian and the coach each said, per
/// category, and which categories are flagged for follow-up.
///
/// Nothing here is stored. Every number is recalculated from the raw answers on each
/// request -- see <see cref="IFiveCAnalysisService"/> for why.
/// </summary>
public class FiveCPlayerViewModel
{
    public int PlayerId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string TeamName { get; set; } = string.Empty;

    public int TeamId { get; set; }

    public string? Position { get; set; }

    /// <summary>
    /// Current consent level. Sits at the top because it is the reason this page can be
    /// opened at all: withdraw it, and the page stops being available.
    /// </summary>
    public ConsentLevel Consent { get; set; }

    public int RoundId { get; set; }

    public string RoundName { get; set; } = string.Empty;

    public bool RoundIsOpen { get; set; }

    public IReadOnlyList<FiveCTeamViewModel.RoundOption> Rounds { get; set; } =
        Array.Empty<FiveCTeamViewModel.RoundOption>();

    public PlayerFiveCComparison Comparison { get; set; } = null!;

    /// <summary>
    /// Which wording the answers were given against. Two rounds answered against different
    /// question sets are not comparable, and this is what makes that visible.
    /// </summary>
    public string QuestionSetVersion { get; set; } = string.Empty;

    /// <summary>
    /// The three difference scores at the top of the page: coach against player, guardian
    /// against player, and one between everyone who answered.
    ///
    /// A pair that cannot be measured still gets a card, saying which of the two has not
    /// answered. Dropping the card would make "they agree" and "nobody filled it in" look
    /// like the same page.
    /// </summary>
    public IReadOnlyList<FiveCScoreCardViewModel> ScoreCards => new[]
    {
        FiveCScoreCardViewModel.ForGap(
            Comparison.CoachVsPlayer,
            Code,
            Comparison.CoachHasAnswered
                ? $"A coach has answered, but {Code} has not, so there is nothing to compare against."
                : $"No coach has answered about {Code} in this round.",
            "coach"),

        FiveCScoreCardViewModel.ForGap(
            Comparison.GuardianVsPlayer,
            Code,
            Comparison.GuardianHasAnswered
                ? $"A guardian has answered, but {Code} has not, so there is nothing to compare against."
                : $"No guardian has answered about {Code} in this round.",
            "guardian"),

        FiveCScoreCardViewModel.ForOverall(Comparison.Differences, Code)
    };

    /// <summary>Links the coach can pass on so somebody else can fill the form in.</summary>
    public IReadOnlyList<ShareLink> ShareLinks { get; set; } = Array.Empty<ShareLink>();

    /// <summary>
    /// A link to the form for one respondent role.
    ///
    /// The link carries no authority. It preselects player and role, nothing more: whoever
    /// follows it signs in as themselves, and both CanViewPlayer and the role check run
    /// again on the server. That is why it is safe to paste into a message.
    /// </summary>
    /// <param name="Role">Who the link is for.</param>
    /// <param name="RoleName">Display name for the role.</param>
    /// <param name="Url">Absolute URL, ready to copy.</param>
    /// <param name="HasAnswered">Whether that role has already answered this round.</param>
    public sealed record ShareLink(
        RespondentType Role,
        string RoleName,
        string Url,
        bool HasAnswered);
}
