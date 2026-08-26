namespace StartPraksisGruppe3Prosjekt.Models.FiveC;

/// <summary>
/// The rules the 5C questionnaire is scored and read by. They live here rather than in
/// a controller or a view so that there is one place to change them.
///
/// The scale itself is deliberately not redefined here -- it is the same 1-5 scale the
/// rest of the system already uses, and <see cref="PlayerRules"/> owns it.
/// </summary>
public static class FiveCRules
{
    /// <inheritdoc cref="PlayerRules.ScaleMin" />
    public const int ScaleMin = PlayerRules.ScaleMin;

    /// <inheritdoc cref="PlayerRules.ScaleMax" />
    public const int ScaleMax = PlayerRules.ScaleMax;

    /// <summary>
    /// A category mean strictly below this is treated as "needs follow-up" in the coach
    /// overview. Two is the "Disagree" point on the scale: a player sitting under it across
    /// a whole category is disagreeing with every positive statement in it.
    /// </summary>
    public const double FollowUpThreshold = 2.0;

    /// <summary>
    /// How many questions in a category must actually be answered before the follow-up
    /// flag is raised. One low answer is a bad day; the requirement is that the player
    /// scores low *consistently*, so a mean over too few answers is not enough to act on.
    /// </summary>
    public const int MinimumAnswersForFollowUp = 3;

    /// <summary>
    /// Turns a raw 1-5 answer into a score where a high number always means "good".
    /// Negatively worded statements are flipped, (6 - value), exactly as
    /// <see cref="Services.ScoringService.ScoreOf"/> does it for the ten-statement form.
    /// Do not write "6 -" anywhere else.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 1-5.</exception>
    public static int Score(int rawValue, bool reversed)
    {
        if (rawValue < ScaleMin || rawValue > ScaleMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawValue),
                rawValue,
                $"An answer must be between {ScaleMin} and {ScaleMax}.");
        }

        return reversed ? PlayerRules.ReverseScoreBase - rawValue : rawValue;
    }

    /// <summary>
    /// Whether a category should be flagged for follow-up: a mean under
    /// <see cref="FollowUpThreshold" /> backed by at least
    /// <see cref="MinimumAnswersForFollowUp" /> answers.
    /// </summary>
    public static bool NeedsFollowUp(double? mean, int answeredQuestions) =>
        mean is { } value
        && answeredQuestions >= MinimumAnswersForFollowUp
        && value < FollowUpThreshold;
}
