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

    /// <inheritdoc />
    public async Task<PlayerTrend> GetTrendAsync(
        int playerId,
        string playerCode,
        IReadOnlyList<TrendPeriod> periods,
        CancellationToken cancellationToken = default)
    {
        var ordered = periods.OrderBy(p => p.ClosesAt).ToList();

        // One read per period. A handful of periods is a handful of queries, and the store
        // has no "several rounds at once" method -- adding one would only pay off with far
        // more periods than a season has.
        var meansPerPeriod = new List<Dictionary<string, double?>>(ordered.Count);

        foreach (var period in ordered)
        {
            var submissions = await _store.GetForPlayerAsync(period.RoundId, playerId, cancellationToken);

            var own = submissions.FirstOrDefault(s =>
                SafeRole(s.RespondentRole) == RespondentType.Player);

            var scores = ScoresByCategory(own);

            meansPerPeriod.Add(_catalog.Questions.Categories.ToDictionary(
                category => category.Key,
                category => MeanOf(scores, category.Key).Mean));
        }

        var categories = _catalog.Questions.Categories
            .Select(category => new CategoryTrend(
                CategoryKey: category.Key,
                CategoryName: category.Name,
                Means: meansPerPeriod
                    .Select(period => period.TryGetValue(category.Key, out var mean) ? mean : null)
                    .ToList()))
            .ToList();

        return new PlayerTrend(playerId, playerCode, ordered, categories);
    }

    /// <inheritdoc />
    public async Task<TeamFiveCAggregate> GetForTeamAsync(
        int roundId,
        int teamId,
        string teamName,
        IReadOnlyCollection<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        // One read for the whole squad -- the same call the team overview already makes,
        // for the same reason: one per player would be N+1 round trips.
        var submissions = await _store.GetForPlayersAsync(roundId, playerIds, cancellationToken);

        var squad = ScoredSquad(submissions);

        var number = 0;

        var categories = _catalog.Questions.Categories
            .Select(category => new TeamCategoryAverage(
                CategoryKey: category.Key,
                Means: MeansOf(squad, category.Name, scores => MeanIn(scores, category.Key)),
                Questions: category.Questions
                    .Select(question => new TeamQuestionAverage(
                        QuestionKey: question.Key,
                        // The same running number the form used, so a statement is called
                        // the same thing here as it was when it was answered.
                        Number: ++number,
                        Text: question.Text,
                        Reversed: question.Reversed,
                        Means: MeansOf(
                            squad,
                            question.Text,
                            scores => ScoreOf(scores, category.Key, question.Key))))
                    .ToList()))
            .ToList();

        return new TeamFiveCAggregate(
            TeamId: teamId,
            TeamName: teamName,
            RoundId: roundId,
            SquadSize: playerIds.Count,
            PlayersWithAnswers: squad.Select(entry => entry.PlayerId).Distinct().Count(),
            // Across all twenty-five statements at once, not the mean of the five category
            // means: a player who answered one category would otherwise weigh as much in
            // "overall" as one who answered the whole form.
            Overall: MeansOf(squad, "All statements", MeanOfEverything),
            Categories: categories);
    }

    /// <inheritdoc />
    public async Task<TeamTrend> GetTeamTrendAsync(
        int teamId,
        string teamName,
        IReadOnlyCollection<int> playerIds,
        IReadOnlyList<TrendPeriod> periods,
        CancellationToken cancellationToken = default)
    {
        var ordered = periods.OrderBy(p => p.ClosesAt).ToList();

        var meansPerPeriod = new List<Dictionary<string, double?>>(ordered.Count);
        var playersPerPeriod = new List<int>(ordered.Count);

        foreach (var period in ordered)
        {
            var submissions = await _store.GetForPlayersAsync(
                period.RoundId,
                playerIds,
                cancellationToken);

            // The players' own answers only, exactly as the individual trend does it: a
            // squad line that moved when a coach changed their mind would be read as the
            // squad having developed.
            var own = ScoredSquad(submissions)
                .Where(entry => entry.Role == RespondentType.Player)
                .ToList();

            playersPerPeriod.Add(own.Count);

            meansPerPeriod.Add(_catalog.Questions.Categories.ToDictionary(
                category => category.Key,
                category => AverageOfPlayers(
                    own,
                    RespondentType.Player,
                    scores => MeanIn(scores, category.Key)).Mean));
        }

        var categories = _catalog.Questions.Categories
            .Select(category => new CategoryTrend(
                CategoryKey: category.Key,
                CategoryName: category.Name,
                Means: meansPerPeriod
                    .Select(period => period.TryGetValue(category.Key, out var mean) ? mean : null)
                    .ToList()))
            .ToList();

        return new TeamTrend(teamId, teamName, ordered, categories, playersPerPeriod);
    }

    /// <summary>
    /// One person's scored answers about one player: who they are, who they answered about,
    /// and every usable answer keyed by category and question.
    ///
    /// The squad aggregate is built from these rather than from
    /// <see cref="PlayerFiveCComparison"/> because it needs the SCORED value per statement,
    /// and a comparison carries the raw one. Building thirty comparisons -- difference
    /// scores and all -- to read one number out of each would also be most of the work
    /// thrown away.
    /// </summary>
    private sealed record ScoredAnswers(
        RespondentType Role,
        int PlayerId,
        IReadOnlyDictionary<string, Dictionary<string, int>> ByCategory);

    /// <summary>
    /// Every submission in the squad, scored, with one entry per player and role.
    ///
    /// At most one submission per (player, role) is expected -- the store replaces rather
    /// than appends -- and a stray duplicate resolves to the most recent, the same rule
    /// <see cref="Build"/> follows. An entry with nothing usable in it is dropped, so a
    /// respondent count is a count of people who actually answered something.
    /// </summary>
    private List<ScoredAnswers> ScoredSquad(IReadOnlyList<SurveySubmission> submissions) =>
        submissions
            .Select(submission => (Submission: submission, Role: SafeRole(submission.RespondentRole)))
            .Where(entry => entry.Role.HasValue)
            .GroupBy(entry => (entry.Submission.PlayerId, Role: entry.Role!.Value))
            .Select(group => new ScoredAnswers(
                group.Key.Role,
                group.Key.PlayerId,
                ScoresByCategory(
                    group.OrderByDescending(entry => entry.Submission.SubmittedAt)
                         .First()
                         .Submission)))
            .Where(entry => entry.ByCategory.Count > 0)
            .ToList();

    /// <summary>
    /// The three team averages for one slice of the questionnaire.
    /// </summary>
    /// <param name="squad">Every scored submission on the team.</param>
    /// <param name="name">What the slice is called.</param>
    /// <param name="perPlayer">One player's number for the slice, or null if they have none.</param>
    private static TeamMeans MeansOf(
        IReadOnlyList<ScoredAnswers> squad,
        string name,
        Func<IReadOnlyDictionary<string, Dictionary<string, int>>, double?> perPlayer) =>
        new(
            Name: name,
            Player: AverageOfPlayers(squad, RespondentType.Player, perPlayer),
            Guardian: AverageOfPlayers(squad, RespondentType.Guardian, perPlayer),
            Coach: AverageOfPlayers(squad, RespondentType.Coach, perPlayer));

    /// <summary>
    /// One role's team average: the mean of the per-player numbers, and how many players
    /// are behind it.
    ///
    /// The mean of MEANS, not of answers. One player counts once whether they answered five
    /// statements or twenty-five -- see <see cref="TeamFiveCAggregate"/>. Players with
    /// nothing to contribute to this slice are left out rather than counted as a gap.
    /// </summary>
    private static TeamRoleAverage AverageOfPlayers(
        IReadOnlyList<ScoredAnswers> squad,
        RespondentType role,
        Func<IReadOnlyDictionary<string, Dictionary<string, int>>, double?> perPlayer)
    {
        var means = squad
            .Where(entry => entry.Role == role)
            .Select(entry => perPlayer(entry.ByCategory))
            .Where(mean => mean.HasValue)
            .Select(mean => mean!.Value)
            .ToList();

        return means.Count == 0
            ? TeamRoleAverage.None(role)
            : TeamRoleAverage.From(role, means.Average(), means.Count);
    }

    /// <summary>One respondent's mean across every statement they answered, or null.</summary>
    private static double? MeanOfEverything(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores)
    {
        var all = scores.Values.SelectMany(category => category.Values).ToList();

        return all.Count == 0 ? null : all.Average();
    }

    /// <summary>One respondent's mean within a single category, or null.</summary>
    private static double? MeanIn(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores,
        string categoryKey) =>
        scores.TryGetValue(categoryKey, out var inCategory) && inCategory.Count > 0
            ? inCategory.Values.Average()
            : null;

    /// <summary>One respondent's score for a single statement, or null if they skipped it.</summary>
    private static double? ScoreOf(
        IReadOnlyDictionary<string, Dictionary<string, int>> scores,
        string categoryKey,
        string questionKey) =>
        scores.TryGetValue(categoryKey, out var inCategory)
        && inCategory.TryGetValue(questionKey, out var score)
            ? score
            : null;

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

        // Raw answers as well as scores. The averages are built from scores, so that a high
        // number always means "good"; the per-statement table shows what people actually
        // clicked, which is what a conversation refers back to.
        var playerRaw = RawByQuestion(player);
        var guardianRaw = RawByQuestion(guardian);
        var coachRaw = RawByQuestion(coach);

        // Running number across the whole form, so a statement is called the same thing here
        // as it was on the form the respondent filled in.
        var number = 0;

        var categories = _catalog.Questions.Categories
            .Select(category =>
            {
                var (playerMean, playerCount) = MeanOf(playerScores, category.Key);
                var (guardianMean, guardianCount) = MeanOf(guardianScores, category.Key);
                var (coachMean, coachCount) = MeanOf(coachScores, category.Key);

                var questions = category.Questions
                    .Select(question => new QuestionComparison(
                        QuestionKey: question.Key,
                        Number: ++number,
                        // The player's own wording. The coach reads the same statement the
                        // player answered, not the about-the-player rewrite of it.
                        Text: question.Text,
                        Reversed: question.Reversed,
                        PlayerValue: Raw(playerRaw, question.Key),
                        GuardianValue: Raw(guardianRaw, question.Key),
                        CoachValue: Raw(coachRaw, question.Key)))
                    .ToList();

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
                        InCategory(coachScores, category.Key)),
                    Questions: questions);
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

    /// <summary>
    /// The raw answers in a submission, keyed by question. Unreversed and unscored: this is
    /// the number the respondent clicked.
    ///
    /// Unanswered questions and values outside the scale are left out, so a missing key
    /// means "no usable answer" and the caller does not have to test for both.
    /// </summary>
    private Dictionary<string, int> RawByQuestion(SurveySubmission? submission)
    {
        var raw = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (submission is null)
        {
            return raw;
        }

        foreach (var answer in submission.Answers)
        {
            if (answer.Value is not { } value
                || value < FiveCRules.ScaleMin
                || value > FiveCRules.ScaleMax)
            {
                continue;
            }

            // A question that has since been removed from the question set is skipped, for
            // the same reason it is skipped when scoring: it has no statement to show.
            if (_catalog.FindQuestion(answer.QuestionKey) is null)
            {
                continue;
            }

            raw[answer.QuestionKey] = value;
        }

        return raw;
    }

    private static int? Raw(IReadOnlyDictionary<string, int> answers, string questionKey) =>
        answers.TryGetValue(questionKey, out var value) ? value : null;

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
