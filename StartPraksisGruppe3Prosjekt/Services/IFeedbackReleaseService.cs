using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Whether the coach has released their own answers to the player for a round.
///
/// The 5C round is a conversation, not a verdict:
///
///   1. The player answers about themselves.
///   2. The coach answers about the player. Neither sees the other yet.
///   3. The player is told the coach HAS answered -- not what they answered.
///   4. The coach releases. Only now does the player see the coach's scores and the
///      difference between them.
///
/// The coach sees everything throughout. The guardian sees exactly what the player sees.
///
/// All reads and writes go through here so the append-only rule does not depend on every
/// controller remembering it -- the same arrangement <see cref="IConsentService"/> has.
/// </summary>
public interface IFeedbackReleaseService
{
    /// <summary>
    /// Whether the coach's answers are currently visible to the player for this round.
    /// The newest event wins; no event at all means not released.
    /// </summary>
    Task<bool> IsReleasedAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for several players in one query. The team overview needs a whole squad,
    /// and one call per player would be N+1.
    /// </summary>
    Task<IReadOnlyDictionary<int, bool>> GetReleasedAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the coach's answers to the player. Adds a new event; never edits one.
    /// Releasing something already released is a no-op rather than a duplicate row.
    /// </summary>
    Task ReleaseAsync(
        int roundId,
        int playerId,
        string coachUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a release. Adds a NEW event with IsReleased = false -- the release it
    /// undoes stays in the log, because it is a thing that happened.
    /// </summary>
    Task WithdrawAsync(
        int roundId,
        int playerId,
        string coachUserId,
        CancellationToken cancellationToken = default);

    /// <summary>The whole history for one player and round, newest first.</summary>
    Task<IReadOnlyList<FeedbackRelease>> GetHistoryAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default);
}
