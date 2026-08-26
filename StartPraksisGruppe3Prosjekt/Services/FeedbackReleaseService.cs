using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <inheritdoc cref="IFeedbackReleaseService" />
public sealed class FeedbackReleaseService : IFeedbackReleaseService
{
    private readonly AppDbContext _db;

    public FeedbackReleaseService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<bool> IsReleasedAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        // Newest event wins. Id as the tiebreaker, in case two land on the same instant --
        // the same rule GetCurrentLevelAsync uses for consent.
        var latest = await _db.FeedbackReleases
            .AsNoTracking()
            .Where(f => f.RoundId == roundId && f.PlayerId == playerId)
            .OrderByDescending(f => f.OccurredAt)
            .ThenByDescending(f => f.Id)
            .Select(f => (bool?)f.IsReleased)
            .FirstOrDefaultAsync(cancellationToken);

        // No event at all means not released. Silence is not permission.
        return latest ?? false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, bool>> GetReleasedAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = playerIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, bool>();
        }

        // Grouped in memory: Postgres has no DISTINCT ON through EF, and a round is at most
        // a few hundred rows.
        var events = await _db.FeedbackReleases
            .AsNoTracking()
            .Where(f => f.RoundId == roundId && ids.Contains(f.PlayerId))
            .Select(f => new { f.PlayerId, f.IsReleased, f.OccurredAt, f.Id })
            .ToListAsync(cancellationToken);

        var latest = events
            .GroupBy(f => f.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(f => f.OccurredAt)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .IsReleased);

        // Every player asked for gets an entry. A missing key would make the caller guess,
        // and the guess would go the wrong way.
        return ids.ToDictionary(
            id => id,
            id => latest.TryGetValue(id, out var released) && released);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(
        int roundId,
        int playerId,
        string coachUserId,
        CancellationToken cancellationToken = default) =>
        RecordAsync(roundId, playerId, coachUserId, isReleased: true, cancellationToken);

    /// <inheritdoc />
    public Task WithdrawAsync(
        int roundId,
        int playerId,
        string coachUserId,
        CancellationToken cancellationToken = default) =>
        RecordAsync(roundId, playerId, coachUserId, isReleased: false, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedbackRelease>> GetHistoryAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default) =>
        await _db.FeedbackReleases
            .AsNoTracking()
            .Where(f => f.RoundId == roundId && f.PlayerId == playerId)
            .OrderByDescending(f => f.OccurredAt)
            .ThenByDescending(f => f.Id)
            .ToListAsync(cancellationToken);

    private async Task RecordAsync(
        int roundId,
        int playerId,
        string coachUserId,
        bool isReleased,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(coachUserId))
        {
            throw new ArgumentException("A release has to name the coach who made it.", nameof(coachUserId));
        }

        // Setting the state it is already in adds nothing but noise to a log people are
        // meant to be able to read.
        if (await IsReleasedAsync(roundId, playerId, cancellationToken) == isReleased)
        {
            return;
        }

        _db.FeedbackReleases.Add(new FeedbackRelease
        {
            RoundId = roundId,
            PlayerId = playerId,
            CoachUserId = coachUserId,
            IsReleased = isReleased,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
