namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// The coach's landing page: the teams they are registered on, and how far each one has
/// got in the current round.
///
/// The teams come from CoachTeam for the signed-in user, not from the role. Two coaches on
/// the same team see the same card; a coach with no CoachTeam row sees none.
/// </summary>
public class CoachTeamsViewModel
{
    public int? RoundId { get; set; }

    public string? RoundName { get; set; }

    public DateTimeOffset? RoundClosesAt { get; set; }

    public bool RoundIsOpen { get; set; }

    public IReadOnlyList<TeamCard> Teams { get; set; } = Array.Empty<TeamCard>();

    /// <summary>How many forms this coach still owes across all their teams.</summary>
    public int OwnBacklog => Teams.Sum(t => t.PlayerCount - t.CoachAnswered);

    /// <param name="TeamId">The team.</param>
    /// <param name="TeamName">Team name.</param>
    /// <param name="PlayerCount">Players on the team.</param>
    /// <param name="PlayersAnswered">How many answered about themselves. Neutral progress.</param>
    /// <param name="CoachAnswered">How many this coach has answered for.</param>
    /// <param name="FollowUpCount">
    /// Players flagged for follow-up among the rows this coach may see. An undercount when
    /// some players have not consented, which the team page says out loud.
    /// </param>
    public sealed record TeamCard(
        int TeamId,
        string TeamName,
        int PlayerCount,
        int PlayersAnswered,
        int CoachAnswered,
        int FollowUpCount);
}
