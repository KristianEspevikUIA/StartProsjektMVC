namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Turns raw 5C answers into the per-category picture the coach overview draws: what the
/// player, the guardian and the coach each said, and which categories need following up.
///
/// Nothing here is stored. Every number is recalculated from the raw answers on request --
/// the same rule <see cref="Services.IScoringService"/> follows for the ten-statement form,
/// and for the same reason: a stored judgement about a minor stays behind after the answers
/// are corrected, the consent is withdrawn or the round is over.
///
/// This service does not check who is allowed to see anything. The caller runs CanViewPlayer
/// per player first, exactly as CoachController.PlayerDetail does.
/// </summary>
public interface IFiveCAnalysisService
{
    /// <summary>
    /// One player in one round. Returns a comparison with empty categories when nobody has
    /// answered, so the view can say "not answered" rather than being handed null.
    /// </summary>
    Task<PlayerFiveCComparison> GetForPlayerAsync(
        int roundId,
        int playerId,
        string playerCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A set of players in one round, read in a single round trip. Keyed by player id, with
    /// an entry for every player asked for -- including the ones nobody has answered about.
    /// </summary>
    Task<IReadOnlyDictionary<int, PlayerFiveCComparison>> GetForPlayersAsync(
        int roundId,
        IReadOnlyDictionary<int, string> playerCodesById,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One player's own averages across several periods -- development over time, which is
    /// what the club asked the system for in the first place.
    ///
    /// Only the player's own answers. What a coach thought of them in March is not part of
    /// how the player developed by September.
    /// </summary>
    /// <param name="periods">The periods to include. Ordered oldest first by the service.</param>
    Task<PlayerTrend> GetTrendAsync(
        int playerId,
        string playerCode,
        IReadOnlyList<TrendPeriod> periods,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A whole squad in one round, as one picture: across every statement, per category and
    /// per statement.
    ///
    /// The same three levels as one player, one step up, and built by averaging the
    /// per-player numbers rather than by pooling every answer -- see
    /// <see cref="TeamFiveCAggregate"/> for why.
    ///
    /// Player IDS are asked for and player CODES are not, deliberately: an aggregate has no
    /// use for them, and a method that never receives them cannot leak one.
    ///
    /// The caller still decides whether an aggregate may be shown at all. That is
    /// <see cref="Authorization.Policies.CanViewTeamAggregate"/>, and
    /// <see cref="TeamFiveCAggregate.PlayersWithAnswers"/> is the count it is checked
    /// against.
    /// </summary>
    Task<TeamFiveCAggregate> GetForTeamAsync(
        int roundId,
        int teamId,
        string teamName,
        IReadOnlyCollection<int> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A squad's averages across several periods -- the team line under
    /// <see cref="GetTrendAsync"/>'s individual one.
    ///
    /// Only the players' own answers, and aggregated per period the same way
    /// <see cref="GetForTeamAsync"/> aggregates within one. One read per period, as the
    /// player trend does, but one read for the whole squad rather than one per player.
    /// </summary>
    /// <param name="periods">The periods to include. Ordered oldest first by the service.</param>
    Task<TeamTrend> GetTeamTrendAsync(
        int teamId,
        string teamName,
        IReadOnlyCollection<int> playerIds,
        IReadOnlyList<TrendPeriod> periods,
        CancellationToken cancellationToken = default);
}
