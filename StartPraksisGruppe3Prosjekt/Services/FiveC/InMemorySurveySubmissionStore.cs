using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Development fallback for <see cref="ISurveySubmissionStore"/>, used while Supabase is
/// not configured. Answers live in memory and are gone when the process stops.
///
/// It exists so the form and the coach overview can be built and demonstrated before
/// Victor's tables are in place, not as a stepping stone to a local database. Nothing here
/// should grow into one -- when Supabase is configured this class is not registered at all.
///
/// It starts empty. It used to fill itself with made-up submissions for every player in
/// Development; the players are real now, and an answer nobody gave is not something to show a
/// coach about a real teenager.
/// </summary>
public sealed class InMemorySurveySubmissionStore : ISurveySubmissionStore
{
    private readonly ConcurrentDictionary<SubmissionKey, SurveySubmission> _submissions = new();

    /// <inheritdoc />
    public string Description => "In-memory (development only, not saved)";

    /// <inheritdoc />
    public async Task SaveAsync(SurveySubmission submission, CancellationToken cancellationToken = default)
    {

        var key = new SubmissionKey(
            submission.RoundId,
            submission.PlayerId,
            submission.RespondentUserId);

        // Replace, never append: one submission per person, per player, per round.
        _submissions[key] = submission;
    }

    /// <inheritdoc />
    public async Task<SurveySubmission?> FindAsync(
        int roundId,
        int playerId,
        string respondentUserId,
        CancellationToken cancellationToken = default)
    {

        return _submissions.TryGetValue(
            new SubmissionKey(roundId, playerId, respondentUserId),
            out var submission)
            ? submission
            : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {

        return _submissions.Values
            .Where(s => s.RoundId == roundId && s.PlayerId == playerId)
            .OrderBy(s => s.RespondentRole, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {

        var wanted = playerIds.ToHashSet();

        return _submissions.Values
            .Where(s => s.RoundId == roundId && wanted.Contains(s.PlayerId))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> CountByRoundAsync(
        IEnumerable<int> roundIds,
        CancellationToken cancellationToken = default)
    {

        var wanted = roundIds.Distinct().ToList();

        var counts = _submissions.Values
            .GroupBy(s => s.RoundId)
            .ToDictionary(group => group.Key, group => group.Count());

        return wanted.ToDictionary(
            id => id,
            id => counts.TryGetValue(id, out var count) ? count : 0);
    }

    private readonly record struct SubmissionKey(int RoundId, int PlayerId, string RespondentUserId);
}
