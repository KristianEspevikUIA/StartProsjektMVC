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
}
