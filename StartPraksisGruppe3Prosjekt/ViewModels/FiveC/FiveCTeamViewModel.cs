using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// The 5C picture for one team in one round: a row per player, and which of them need
/// following up.
///
/// A row for a player the coach may not see is NOT dropped. It is listed with code and
/// position, without numbers and without a link, and with the reason written out. Hiding
/// the row would hide that the player exists; showing the numbers would ignore consent.
/// Which of the two a row is, is <see cref="PlayerRow.CanView"/>.
/// </summary>
public class FiveCTeamViewModel
{
    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public int RoundId { get; set; }

    public string RoundName { get; set; } = string.Empty;

    public DateTimeOffset RoundClosesAt { get; set; }

    public bool RoundIsOpen { get; set; }

    /// <summary>Every round, for the picker at the top. A coach compares rounds often.</summary>
    public IReadOnlyList<RoundOption> Rounds { get; set; } = Array.Empty<RoundOption>();

    public List<PlayerRow> Players { get; set; } = new();

    /// <summary>
    /// How many players have answered about themselves. A count of who answered, not of
    /// what they answered -- it says nothing about any individual and needs no consent.
    /// </summary>
    public int PlayerAnsweredCount => Players.Count(p => p.PlayerHasAnswered);

    /// <summary>How many the signed-in coach has answered for. Their own backlog.</summary>
    public int CoachAnsweredCount => Players.Count(p => p.CoachHasAnswered);

    /// <summary>
    /// How many players are flagged for follow-up among the rows this coach may see.
    /// Necessarily an undercount when some rows are withheld, which the view says out loud.
    /// </summary>
    public int FollowUpCount => Players.Count(p => p.NeedsFollowUp);

    public int HiddenCount => Players.Count(p => !p.CanView);

    public sealed class PlayerRow
    {
        public int PlayerId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? Position { get; set; }

        /// <summary>
        /// Whether CanViewPlayer said yes for this player. False means the row is shown
        /// without numbers -- not that the player is left out.
        /// </summary>
        public bool CanView { get; set; }

        public ConsentLevel Consent { get; set; }

        /// <summary>Neutral progress. Who answered, never what they answered.</summary>
        public bool PlayerHasAnswered { get; set; }

        public bool GuardianHasAnswered { get; set; }

        public bool CoachHasAnswered { get; set; }

        /// <summary>The per-category comparison, or null when <see cref="CanView"/> is false.</summary>
        public PlayerFiveCComparison? Comparison { get; set; }

        /// <summary>The player scores consistently low on at least one category.</summary>
        public bool NeedsFollowUp => Comparison?.NeedsFollowUp == true;

        /// <summary>The flagged categories, for the badge text. Empty is the normal case.</summary>
        public IReadOnlyList<CategoryComparison> FollowUp =>
            Comparison?.FollowUp ?? Array.Empty<CategoryComparison>();

        /// <summary>
        /// How far the coach's answers are from the player's own, across the whole
        /// questionnaire. Null when either of them has not answered.
        /// </summary>
        public RespondentGap? CoachVsPlayer => Comparison?.CoachVsPlayer;

        /// <summary>The same for the guardian. Null when either of them has not answered.</summary>
        public RespondentGap? GuardianVsPlayer => Comparison?.GuardianVsPlayer;

        /// <summary>
        /// One number for how far apart everyone who answered is. This is the number the
        /// whole comparison exists for, and what the row is worth opening for.
        /// </summary>
        public double? OverallDifference => Comparison?.OverallDifference;

        /// <summary>
        /// Why this row has no numbers. Separating "not allowed to see" from "has not
        /// answered" matters: they are entirely different states that otherwise become the
        /// same grey row.
        /// </summary>
        public string? WithheldReason
        {
            get
            {
                if (!CanView)
                {
                    return "This row is not available for the current viewer.";
                }

                if (Comparison?.HasAnyAnswers != true)
                {
                    return "Nobody has answered about this player yet.";
                }

                return null;
            }
        }
    }

    /// <param name="Id">Round id.</param>
    /// <param name="Name">Round name.</param>
    /// <param name="IsOpen">Whether it can still be answered.</param>
    public sealed record RoundOption(int Id, string Name, bool IsOpen);
}
