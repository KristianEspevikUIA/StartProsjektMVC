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
/// What one person wrote in answer to one reflection question, ready to be shown.
///
/// <see cref="Value"/> is what is stored: a category key for a choice between the five C's,
/// the words themselves for a written answer. <see cref="Display"/> is the same thing as it
/// should be read -- "Confidence" rather than "confidence" -- so a view never has to look a
/// category up, and a heading rewritten in the question set is picked up here.
///
/// <see cref="None"/> is both "did not answer" and "not yours to see". The player-facing
/// page redacts the coach's reflection to it until the coach has shared, which is the same
/// rule the numbers follow -- see <see cref="ViewModels.FiveC.FiveCFeedbackViewModel.Redact"/>.
/// </summary>
public sealed record ReflectionResponse(string? Value, string? Display)
{
    /// <summary>Nothing to show.</summary>
    public static ReflectionResponse None { get; } = new(null, null);

    public bool HasAnswer => !string.IsNullOrWhiteSpace(Value);
}

/// <summary>
/// One reflection question, with what the player, the guardian and the coach each said.
///
/// Nothing here is scored or compared numerically. Two people picking the same C is worth
/// seeing, and the view says so, but there is no difference score: the distance between
/// "Control" and "Concentration" is not a number.
/// </summary>
/// <param name="QuestionKey">Stable key from the question set, e.g. "reflection-strength".</param>
/// <param name="Number">Running number within the reflection. Matches the form.</param>
/// <param name="Text">
/// The question in the player's own wording, which is the reference one -- the same choice
/// <see cref="QuestionComparison.Text"/> makes.
/// </param>
/// <param name="IsCategoryChoice">
/// True when the answer is one of the five C's, false when it is written. The view shows a
/// chosen C as a badge and a written answer as a quotation.
/// </param>
/// <param name="Player">What the player wrote or picked.</param>
/// <param name="Guardian">The same for the guardian.</param>
/// <param name="Coach">The same for the coach.</param>
public sealed record ReflectionComparison(
    string QuestionKey,
    int Number,
    string Text,
    bool IsCategoryChoice,
    ReflectionResponse Player,
    ReflectionResponse Guardian,
    ReflectionResponse Coach)
{
    /// <summary>True when nobody answered this question.</summary>
    public bool Unanswered => !Player.HasAnswer && !Guardian.HasAnswer && !Coach.HasAnswer;

    /// <summary>
    /// True when the player and the coach picked the same C. Only meaningful for a choice,
    /// and only when both of them answered.
    /// </summary>
    public bool PlayerAndCoachAgree =>
        IsCategoryChoice
        && Player.HasAnswer
        && string.Equals(Player.Value, Coach.Value, StringComparison.OrdinalIgnoreCase);
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
/// <param name="Reflection">
/// The end-of-period reflection, in the order the form asked it. Empty when the question
/// set has no reflection section.
///
/// It sits beside the numbers rather than inside <see cref="Categories"/> because it is not
/// one: a chosen C and a sentence do not belong to a category average, and nothing here is
/// scored, compared or trended.
/// </param>
public sealed record PlayerFiveCComparison(
    int PlayerId,
    string PlayerCode,
    int RoundId,
    IReadOnlyList<CategoryComparison> Categories,
    DateTimeOffset? PlayerSubmittedAt,
    DateTimeOffset? GuardianSubmittedAt,
    DateTimeOffset? CoachSubmittedAt,
    DifferenceScores Differences,
    IReadOnlyList<ReflectionComparison> Reflection)
{
    /// <summary>True when at least one of the three wrote anything in the reflection.</summary>
    public bool HasReflection => Reflection.Any(r => !r.Unanswered);

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
