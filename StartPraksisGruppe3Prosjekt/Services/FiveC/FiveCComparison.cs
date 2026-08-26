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
public sealed record CategoryComparison(
    string CategoryKey,
    string CategoryName,
    double? PlayerMean,
    int PlayerAnswered,
    double? GuardianMean,
    int GuardianAnswered,
    double? CoachMean,
    int CoachAnswered)
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

    /// <summary>Every mean that exists for this category, in display order.</summary>
    public IEnumerable<(RespondentType Respondent, double Mean)> PresentMeans
    {
        get
        {
            if (PlayerMean is { } player) yield return (RespondentType.Player, player);
            if (GuardianMean is { } guardian) yield return (RespondentType.Guardian, guardian);
            if (CoachMean is { } coach) yield return (RespondentType.Coach, coach);
        }
    }

    /// <summary>True when at least one of the three answered anything in this category.</summary>
    public bool HasAnyAnswers => PlayerMean.HasValue || GuardianMean.HasValue || CoachMean.HasValue;

    /// <summary>
    /// The spread between the highest and the lowest mean, across whoever answered.
    /// This is the disagreement the coach overview is for. Null when fewer than two of
    /// them answered -- there is nothing to disagree about.
    /// </summary>
    public double? Spread
    {
        get
        {
            var means = PresentMeans.Select(m => m.Mean).ToList();
            return means.Count < 2 ? null : means.Max() - means.Min();
        }
    }

    /// <summary>
    /// Coach mean minus player mean. Positive means the coach rated the player higher than
    /// the player rated themselves. Null unless both answered.
    /// </summary>
    public double? CoachMinusPlayer =>
        CoachMean is { } coach && PlayerMean is { } player ? coach - player : null;
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
public sealed record PlayerFiveCComparison(
    int PlayerId,
    string PlayerCode,
    int RoundId,
    IReadOnlyList<CategoryComparison> Categories,
    DateTimeOffset? PlayerSubmittedAt,
    DateTimeOffset? GuardianSubmittedAt,
    DateTimeOffset? CoachSubmittedAt)
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

    /// <summary>
    /// The largest disagreement between any two respondents, across all five categories.
    /// Used to sort a team list by "who is furthest from agreeing".
    /// </summary>
    public double? LargestSpread =>
        Categories.Select(c => c.Spread).Where(s => s.HasValue).DefaultIfEmpty(null).Max();

    /// <summary>The category that disagreement sits in, or null when there is none.</summary>
    public CategoryComparison? MostDisagreedCategory =>
        Categories.Where(c => c.Spread.HasValue)
                  .OrderByDescending(c => c.Spread)
                  .FirstOrDefault();
}
