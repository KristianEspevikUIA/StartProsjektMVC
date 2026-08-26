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
public sealed record CategoryComparison(
    string CategoryKey,
    string CategoryName,
    double? PlayerMean,
    int PlayerAnswered,
    double? GuardianMean,
    int GuardianAnswered,
    double? CoachMean,
    int CoachAnswered,
    DifferenceScores Differences)
{
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
