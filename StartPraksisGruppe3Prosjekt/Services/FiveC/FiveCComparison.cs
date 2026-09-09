using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// What player, guardian and coach each said about one category, for one player.
///
/// Every mean is nullable and every count is carried alongside it. A category nobody
/// answered and a category answered with ones are different things, and a view that only
/// gets a number cannot tell them apart.
///
/// None of this is stored. It is recalculated from the raw answers on every request, the
/// same rule the ten-statement form follows: a saved judgement about a minor outlives the
/// answers it was based on, the consent that allowed it, and the round it belonged to.
/// </summary>
/// <param name="CategoryKey">Category key, e.g. "commitment".</param>
/// <param name="CategoryName">Heading, e.g. "Commitment".</param>
/// <param name="PlayerMean">The player's own mean for the category, after reversal.</param>
/// <param name="PlayerAnswered">How many statements in the category the player answered.</param>
/// <param name="GuardianMean">The guardian's mean, or null if no guardian answered.</param>
/// <param name="GuardianAnswered">How many statements the guardian answered.</param>
/// <param name="CoachMean">The coach's mean, or null if no coach answered.</param>
/// <param name="CoachAnswered">How many statements the coach answered.</param>
/// <param name="Differences">
/// The difference scores for this category alone: coach against player, guardian against
/// player, and one across all of them. Paired per statement, so it catches disagreement
/// that the three means above hide -- see <see cref="RespondentGap"/>.
/// </param>
/// <summary>
/// One statement, with what each of the three actually answered.
///
/// The values are RAW -- the number the respondent clicked, 1 to 5 -- not the reversed
/// score. On a reversed statement a 5 therefore means the respondent agreed strongly with a
/// negatively worded sentence, which is a low score. That is why <see cref="Reversed"/> has
/// to be shown next to the numbers rather than quietly corrected for.
///
/// The distance between two answers is the same either way: reversal flips both sides, so
/// |(6-a) - (6-b)| equals |a - b|. Showing raw answers and an absolute difference together
/// is therefore consistent, which showing raw answers and a signed difference would not be.
/// </summary>
/// <param name="QuestionKey">Stable key from the question set, e.g. "commitment-1".</param>
/// <param name="Number">Running number across the whole form, 1-25. Matches the form.</param>
/// <param name="Text">The statement in the player's own wording, which is the reference one.</param>
/// <param name="Reversed">Negatively worded. A high answer here is a low score.</param>
/// <param name="PlayerValue">What the player answered, or null if they did not.</param>
/// <param name="GuardianValue">What the guardian answered, or null.</param>
/// <param name="CoachValue">What the coach answered, or null.</param>
public sealed record QuestionComparison(
    string QuestionKey,
    int Number,
    string Text,
    bool Reversed,
    int? PlayerValue,
    int? GuardianValue,
    int? CoachValue)
{
    /// <summary>
    /// How far apart the coach and the player are on this one statement, or null when one
    /// of them did not answer it. Null is "nothing to compare", not a difference of zero.
    /// </summary>
    public int? CoachPlayerDifference =>
        PlayerValue is { } player && CoachValue is { } coach ? Math.Abs(coach - player) : null;

    /// <summary>The same for the guardian.</summary>
    public int? GuardianPlayerDifference =>
        PlayerValue is { } player && GuardianValue is { } guardian ? Math.Abs(guardian - player) : null;

    /// <summary>
    /// The widest gap on this statement between any two who answered it. Used to pick out
    /// the statements worth talking about from a list of twenty-five.
    /// </summary>
    public int? LargestDifference
    {
        get
        {
            var values = new List<int>(3);
            if (PlayerValue is { } p) values.Add(p);
            if (GuardianValue is { } g) values.Add(g);
            if (CoachValue is { } c) values.Add(c);

            return values.Count < 2 ? null : values.Max() - values.Min();
        }
    }

    /// <summary>True when nobody answered this statement at all.</summary>
    public bool Unanswered =>
        PlayerValue is null && GuardianValue is null && CoachValue is null;
}

/// <param name="Questions">
/// The individual statements in this category, with what each respondent actually answered.
/// The averages above are what a coach reads first; this is what they need when the
/// conversation gets specific and "you rated yourself 0.8 higher" turns into "on statement
/// four you said 5 and I said 2".
/// </param>
public sealed record CategoryComparison(
    string CategoryKey,
    string CategoryName,
    double? PlayerMean,
    int PlayerAnswered,
    double? GuardianMean,
    int GuardianAnswered,
    double? CoachMean,
    int CoachAnswered,
    DifferenceScores Differences,
    IReadOnlyList<QuestionComparison> Questions) : IRespondentMeans
{
    /// <summary>
    /// The heading the bar chart labels itself with. The same three bars are drawn for a
    /// team, where the slice is not always a category, so the shared interface asks for a
    /// neutral name -- see <see cref="IRespondentMeans"/>.
    /// </summary>
    public string Label => CategoryName;

    /// <summary>
    /// The player scores low across this category and should be followed up.
    ///
    /// Based on the player's own answers, not on what anyone thinks about them, and only
    /// once enough statements have actually been answered -- see
    /// <see cref="FiveCRules.NeedsFollowUp"/>. One low answer is a bad day; a mean under
    /// two across a category is the player disagreeing with the whole of it.
    /// </summary>
    public bool NeedsFollowUp => FiveCRules.NeedsFollowUp(PlayerMean, PlayerAnswered);

    /// <summary>True when at least one of the three answered anything in this category.</summary>
    public bool HasAnyAnswers => PlayerMean.HasValue || GuardianMean.HasValue || CoachMean.HasValue;

    /// <summary>
    /// The difference across everyone who answered this category, or null when fewer than
    /// two of them did. See <see cref="DifferenceScores.Overall"/>.
    /// </summary>
    public double? Difference => Differences.Overall;

    /// <summary>The coach-against-player score for this category, or null if either is missing.</summary>
    public RespondentGap? CoachVsPlayer => Differences.CoachVsPlayer;

    /// <summary>The guardian-against-player score for this category, or null if either is missing.</summary>
    public RespondentGap? GuardianVsPlayer => Differences.GuardianVsPlayer;
}

/// <summary>
/// All five categories for one player in one round, plus what is missing.
/// </summary>
/// <param name="PlayerId">The player.</param>
/// <param name="PlayerCode">Player code, e.g. "TS-08-16". Codes, not names.</param>
/// <param name="RoundId">The round.</param>
/// <param name="Categories">The five C's, in the order the question set lists them.</param>
/// <param name="PlayerSubmittedAt">When the player answered, or null.</param>
/// <param name="GuardianSubmittedAt">When a guardian answered, or null.</param>
/// <param name="CoachSubmittedAt">When a coach answered, or null.</param>
/// <param name="Differences">
/// The three difference scores across the whole questionnaire: coach against player,
/// guardian against player, and one between all of them.
///
/// Measured over all twenty-five statements at once rather than by averaging the five
/// category scores. Averaging the categories would silently give a category with two
/// answered statements the same weight as one with five.
/// </param>
public sealed record PlayerFiveCComparison(
    int PlayerId,
    string PlayerCode,
    int RoundId,
    IReadOnlyList<CategoryComparison> Categories,
    DateTimeOffset? PlayerSubmittedAt,
    DateTimeOffset? GuardianSubmittedAt,
    DateTimeOffset? CoachSubmittedAt,
    DifferenceScores Differences)
{
    public bool PlayerHasAnswered => PlayerSubmittedAt.HasValue;

    public bool GuardianHasAnswered => GuardianSubmittedAt.HasValue;

    public bool CoachHasAnswered => CoachSubmittedAt.HasValue;

    public bool HasAnyAnswers => PlayerHasAnswered || GuardianHasAnswered || CoachHasAnswered;

    /// <summary>The categories the player scores consistently low on. Empty is the normal case.</summary>
    public IReadOnlyList<CategoryComparison> FollowUp =>
        Categories.Where(c => c.NeedsFollowUp).ToList();

    /// <summary>True when at least one category needs following up. Drives the badge.</summary>
    public bool NeedsFollowUp => Categories.Any(c => c.NeedsFollowUp);

    /// <summary>Coach against the player's own answers, across the whole questionnaire.</summary>
    public RespondentGap? CoachVsPlayer => Differences.CoachVsPlayer;

    /// <summary>Guardian against the player's own answers, across the whole questionnaire.</summary>
    public RespondentGap? GuardianVsPlayer => Differences.GuardianVsPlayer;

    /// <summary>
    /// One number for how far apart everyone who answered is. Null when fewer than two of
    /// them answered -- there is nothing to disagree about with one set of answers.
    /// </summary>
    public double? OverallDifference => Differences.Overall;

    /// <summary>
    /// The category the difference score is worst in, or null when nothing can be compared.
    /// This is where a conversation with the player starts.
    /// </summary>
    public CategoryComparison? MostDifferentCategory =>
        Categories.Where(c => c.Differences.Overall.HasValue)
                  .OrderByDescending(c => c.Differences.Overall)
                  .FirstOrDefault();

    /// <summary>
    /// The worst category difference, for sorting a team list by "who is furthest from
    /// agreeing". Null when nothing anywhere could be compared.
    /// </summary>
    public double? LargestCategoryDifference =>
        Categories.Select(c => c.Difference).Where(d => d.HasValue).DefaultIfEmpty(null).Max();
}
