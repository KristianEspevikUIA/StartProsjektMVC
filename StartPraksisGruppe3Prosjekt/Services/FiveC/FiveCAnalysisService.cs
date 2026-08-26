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
                    CoachAnswered: coachCount,
                    Differences: DifferencesBetween(
                        InCategory(playerScores, category.Key),
                        InCategory(guardianScores, category.Key),
                        InCategory(coachScores, category.Key)));
            })
            .ToList();

        return new PlayerFiveCComparison(
            PlayerId: playerId,
            PlayerCode: playerCode,
            RoundId: roundId,
            Categories: categories,
            PlayerSubmittedAt: player?.SubmittedAt,
            GuardianSubmittedAt: guardian?.SubmittedAt,
            CoachSubmittedAt: coach?.SubmittedAt,
            // Measured across all twenty-five statements at once. Averaging the five
            // category scores instead would give a category with two answered statements
            // the same weight as one with five.
            Differences: DifferencesBetween(
                Flatten(playerScores),
                Flatten(guardianScores),
                Flatten(coachScores)));
    }

    /// <summary>
    /// The three difference scores for one set of answers: coach against player, guardian
    /// against player, and coach against guardian, which together make the "between all"
    /// score. Used for a single category and again for the questionnaire as a whole.
    /// </summary>
    private static DifferenceScores DifferencesBetween(
        IReadOnlyDictionary<string, int> playerScores,
        IReadOnlyDictionary<string, int> guardianScores,
        IReadOnlyDictionary<string, int> coachScores) => new(
            CoachVsPlayer: RespondentGap.Between(
                RespondentType.Coach, coachScores, RespondentType.Player, playerScores),
            GuardianVsPlayer: RespondentGap.Between(
                RespondentType.Guardian, guardianScores, RespondentType.Player, playerScores),
            CoachVsGuardian: RespondentGap.Between(
                RespondentType.Coach, coachScores, RespondentType.Guardian, guardianScores));

    /// <summary>One category's scores, or an empty set when that category was not answered.</summary>
    private static IReadOnlyDictionary<string, int> InCategory(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores,
        string categoryKey) =>
        scores.TryGetValue(categoryKey, out var inCategory)
            ? inCategory
            : EmptyScores;

    /// <summary>
    /// Every category's scores in one dictionary, for measuring across the whole
    /// questionnaire. Question keys are unique across the catalog, which the catalog
    /// enforces at startup; the indexer is used anyway so a duplicate would be the last
    /// answer winning rather than an exception on a coach's page.
    /// </summary>
    private static IReadOnlyDictionary<string, int> Flatten(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores)
    {
        var flat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in scores.Values)
        {
            foreach (var (questionKey, score) in category)
            {
                flat[questionKey] = score;
            }
        }

        return flat;
    }

    private static readonly Dictionary<string, int> EmptyScores =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every answered value in a submission, turned into a score, grouped per category and
    /// keyed by question.
    ///
    /// The question key is kept rather than dropped into a flat list because the difference
    /// scores pair the three respondents STATEMENT BY STATEMENT. A 5 and a 1 average to the
    /// same 3 as two 3s do, and a difference built from category averages would read that
    /// as agreement.
    ///
    /// Two things happen here and nowhere else:
    ///   * Unanswered questions (null) are dropped, not counted as 3. An unanswered question
    ///     is an absence of an opinion, not a middling one.
    ///   * Negatively worded statements are flipped, so a high score always means "good".
    ///     Whether a statement is reversed is read from the question set at display time,
    ///     which is why the stored answers stay raw.
    /// </summary>
    private Dictionary<string, Dictionary<string, int>> ScoresByCategory(SurveySubmission? submission)
    {
        var scores = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

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

            if (!scores.TryGetValue(categoryKey, out var inCategory))
            {
                inCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                scores[categoryKey] = inCategory;
            }

            inCategory[answer.QuestionKey] = FiveCRules.Score(raw, question.Reversed);
        }

        return scores;
    }

    private static (double? Mean, int Count) MeanOf(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores,
        string categoryKey)
    {
        if (!scores.TryGetValue(categoryKey, out var inCategory) || inCategory.Count == 0)
        {
            return (null, 0);
        }

        return (inCategory.Values.Average(), inCategory.Count);
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
