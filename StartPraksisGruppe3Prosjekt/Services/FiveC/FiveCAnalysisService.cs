using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <inheritdoc cref="IFiveCAnalysisService" />
public sealed class FiveCAnalysisService : IFiveCAnalysisService
{
    private readonly ISurveySubmissionStore _store;
    private readonly IQuestionCatalog _catalog;

    public FiveCAnalysisService(ISurveySubmissionStore store, IQuestionCatalog catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    /// <inheritdoc />
    public async Task<PlayerFiveCComparison> GetForPlayerAsync(
        int roundId,
        int playerId,
        string playerCode,
        CancellationToken cancellationToken = default)
    {
        var submissions = await _store.GetForPlayerAsync(roundId, playerId, cancellationToken);

        return Build(roundId, playerId, playerCode, submissions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, PlayerFiveCComparison>> GetForPlayersAsync(
        int roundId,
        IReadOnlyDictionary<int, string> playerCodesById,
        CancellationToken cancellationToken = default)
    {
        var submissions = await _store.GetForPlayersAsync(
            roundId,
            playerCodesById.Keys,
            cancellationToken);

        var byPlayer = submissions.ToLookup(s => s.PlayerId);

        // Every player asked for gets an entry, answered or not. A missing key would make
        // "has not answered" indistinguishable from "was not asked about" in the view.
        return playerCodesById.ToDictionary(
            entry => entry.Key,
            entry => Build(roundId, entry.Key, entry.Value, byPlayer[entry.Key].ToList()));
    }

    /// <summary>
    /// Folds a player's submissions into one comparison, one row per category.
    /// </summary>
    private PlayerFiveCComparison Build(
        int roundId,
        int playerId,
        string playerCode,
        IReadOnlyList<SurveySubmission> submissions)
    {
        // At most one submission per role is expected -- the store replaces rather than
        // appends. If a stray duplicate does turn up, the most recent one is the answer.
        var byRole = submissions
            .GroupBy(s => SafeRole(s.RespondentRole))
            .Where(g => g.Key.HasValue)
            .ToDictionary(
                g => g.Key!.Value,
                g => g.OrderByDescending(s => s.SubmittedAt).First());

        byRole.TryGetValue(RespondentType.Player, out var player);
        byRole.TryGetValue(RespondentType.Guardian, out var guardian);
        byRole.TryGetValue(RespondentType.Coach, out var coach);

        var playerScores = ScoresByCategory(player);
        var guardianScores = ScoresByCategory(guardian);
        var coachScores = ScoresByCategory(coach);

        var categories = _catalog.Questions.Categories
            .Select(category =>
            {
                var (playerMean, playerCount) = MeanOf(playerScores, category.Key);
                var (guardianMean, guardianCount) = MeanOf(guardianScores, category.Key);
                var (coachMean, coachCount) = MeanOf(coachScores, category.Key);

                return new CategoryComparison(
                    CategoryKey: category.Key,
                    CategoryName: category.Name,
                    PlayerMean: playerMean,
                    PlayerAnswered: playerCount,
                    GuardianMean: guardianMean,
                    GuardianAnswered: guardianCount,
                    CoachMean: coachMean,
                    CoachAnswered: coachCount);
            })
            .ToList();

        return new PlayerFiveCComparison(
            PlayerId: playerId,
            PlayerCode: playerCode,
            RoundId: roundId,
            Categories: categories,
            PlayerSubmittedAt: player?.SubmittedAt,
            GuardianSubmittedAt: guardian?.SubmittedAt,
            CoachSubmittedAt: coach?.SubmittedAt);
    }

    /// <summary>
    /// Every answered value in a submission, turned into a score and grouped per category.
    ///
    /// Two things happen here and nowhere else:
    ///   * Unanswered questions (null) are dropped, not counted as 3. An unanswered question
    ///     is an absence of an opinion, not a middling one.
    ///   * Negatively worded statements are flipped, so a high score always means "good".
    ///     Whether a statement is reversed is read from the question set at display time,
    ///     which is why the stored answers stay raw.
    /// </summary>
    private Dictionary<string, List<int>> ScoresByCategory(SurveySubmission? submission)
    {
        var scores = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        if (submission is null)
        {
            return scores;
        }

        foreach (var answer in submission.Answers)
        {
            if (answer.Value is not { } raw)
            {
                continue;
            }

            var question = _catalog.FindQuestion(answer.QuestionKey);
            if (question is null)
            {
                // The question set has moved on since this was answered. Counting an answer
                // whose statement no longer exists would put an unknown question into a mean.
                continue;
            }

            if (raw < FiveCRules.ScaleMin || raw > FiveCRules.ScaleMax)
            {
                // Stored outside the scale. Whatever produced it, it is not an answer this
                // scale can average.
                continue;
            }

            var categoryKey = _catalog.FindCategoryForQuestion(answer.QuestionKey)?.Key
                              ?? answer.CategoryKey;

            if (!scores.TryGetValue(categoryKey, out var list))
            {
                list = new List<int>();
                scores[categoryKey] = list;
            }

            list.Add(FiveCRules.Score(raw, question.Reversed));
        }

        return scores;
    }

    private static (double? Mean, int Count) MeanOf(
        IReadOnlyDictionary<string, List<int>> scores,
        string categoryKey)
    {
        if (!scores.TryGetValue(categoryKey, out var values) || values.Count == 0)
        {
            return (null, 0);
        }

        return (values.Average(), values.Count);
    }

    /// <summary>
    /// Maps a stored role string without throwing. A row written by something else, with a
    /// role this application does not know, is skipped rather than taking the page down.
    /// </summary>
    private static RespondentType? SafeRole(string role)
    {
        try
        {
            return SurveySubmission.Roles.To(role);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
