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
    /// A difference score at or above this is worth a conversation. Half a point per
    /// statement is the point where the two are no longer rounding to the same answer.
    /// </summary>
    public const double AgreementThreshold = 0.5;

    /// <summary>
    /// A difference score at or above this is a full point apart on the average statement:
    /// the coach and the player are not describing the same season. This is the number the
    /// overview sorts on.
    /// </summary>
    public const double LargeDifferenceThreshold = 1.0;

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
    /// A mean at or above this reads as a strength. On a scale labelled Never / Rarely /
    /// Sometimes / Often / Always, 3.5 is the point where the average answer is "often" or
    /// better -- the first band that is worth saying something positive about rather than
    /// merely not worrying about.
    ///
    /// It is the upper of the two lines <see cref="LevelOfScore"/> bands on. The lower one
    /// is <see cref="FollowUpThreshold"/>, deliberately: a number the overview colours red
    /// and a category the system flags for follow-up have to be the same thing, or the page
    /// contradicts its own notice.
    /// </summary>
    public const double StrongScoreThreshold = 3.5;

    /// <summary>
    /// Turns a mean on the 1-5 scale into the three colour bands the overview reads it in.
    ///
    /// This is NOT <see cref="LevelOf"/>. That one bands a DIFFERENCE between two people;
    /// this one bands a SCORE. They are both shown as red, amber and green, on the same
    /// pages, and they are not the same measurement -- see <see cref="ScoreLevels"/> for
    /// the words that keep them apart on screen.
    /// </summary>
    /// <param name="mean">
    /// A mean on the 1-5 scale, after reversal. Rounded to one decimal first, for the same
    /// reason <see cref="LevelOf"/> does it: the band sits next to the number, and the
    /// number is printed to one decimal. Banding 3.46 as amber while printing "3.5" puts a
    /// visible contradiction on the page.
    /// </param>
    public static ScoreLevel LevelOfScore(double mean) =>
        Math.Round(mean, 1, MidpointRounding.AwayFromZero) switch
        {
            < FollowUpThreshold => ScoreLevel.Low,
            < StrongScoreThreshold => ScoreLevel.Middling,
            _ => ScoreLevel.Strong
        };

    /// <summary>
    /// Whether a category should be flagged for follow-up: a mean under
    /// <see cref="FollowUpThreshold" /> backed by at least
    /// <see cref="MinimumAnswersForFollowUp" /> answers.
    /// </summary>
    public static bool NeedsFollowUp(double? mean, int answeredQuestions) =>
        mean is { } value
        && answeredQuestions >= MinimumAnswersForFollowUp
        && value < FollowUpThreshold;

    /// <summary>
    /// Turns a difference score into the three bands the overview reads it in. Kept next to
    /// the thresholds so a view never compares against a bare number of its own.
    /// </summary>
    /// <param name="difference">
    /// A mean absolute difference per statement, as produced by
    /// <see cref="Services.FiveC.RespondentGap.Between"/>. It is rounded to one decimal
    /// first, deliberately: the band sits next to the number on screen, and that number is
    /// shown to one decimal. Banding 0.98 as "some difference" while printing it as "1,0"
    /// puts a visible contradiction on the page.
    /// </param>
    public static AgreementLevel LevelOf(double difference) =>
        // AwayFromZero, not the default banker's rounding: ToString("0.0") rounds halves
        // away from zero, and a band that rounds 0.45 down while the page prints "0,5" is
        // the exact contradiction this rounding exists to prevent.
        Math.Round(difference, 1, MidpointRounding.AwayFromZero) switch
    {
        < AgreementThreshold => AgreementLevel.Agree,
        < LargeDifferenceThreshold => AgreementLevel.SomeDifference,
        _ => AgreementLevel.LargeDifference
    };
}

/// <summary>
/// How far apart two respondents are, in the three bands the coach views read.
/// The numbers behind the bands are <see cref="FiveCRules.AgreementThreshold"/> and
/// <see cref="FiveCRules.LargeDifferenceThreshold"/>.
/// </summary>
public enum AgreementLevel
{
    /// <summary>Under half a point apart per statement. They are describing the same thing.</summary>
    Agree = 0,

    /// <summary>Between half a point and a full point apart. Worth reading, not worth alarm.</summary>
    SomeDifference = 1,

    /// <summary>A full point or more apart on the average statement. Worth a conversation.</summary>
    LargeDifference = 2
}

/// <summary>
/// The words and class names the three bands are shown with. They live here rather than in
/// a .cshtml file so that the team overview and the player page cannot describe the same
/// number differently.
/// </summary>
public static class AgreementLevels
{
    /// <summary>Heading text, e.g. "Large difference".</summary>
    public static string DisplayName(AgreementLevel level) => level switch
    {
        AgreementLevel.Agree => "Close agreement",
        AgreementLevel.SomeDifference => "Some difference",
        AgreementLevel.LargeDifference => "Large difference",
        _ => level.ToString()
    };

    /// <summary>"agree", "some" or "large" -- the suffix used in startcompass.css.</summary>
    public static string CssSuffix(AgreementLevel level) => level switch
    {
        AgreementLevel.Agree => "agree",
        AgreementLevel.LargeDifference => "large",
        _ => "some"
    };

    /// <summary>The full badge class for a level, greens through to red.</summary>
    public static string BadgeClass(AgreementLevel level) => level switch
    {
        AgreementLevel.Agree => "sc-badge sc-badge--ok",
        AgreementLevel.LargeDifference => "sc-badge sc-badge--alert",
        _ => "sc-badge sc-badge--warn"
    };
}

/// <summary>
/// How a mean on the 1-5 scale reads, in the three bands the overview colours it in.
/// The numbers behind the bands are <see cref="FiveCRules.FollowUpThreshold"/> and
/// <see cref="FiveCRules.StrongScoreThreshold"/>.
///
/// Kept apart from <see cref="AgreementLevel"/> on purpose. Both are red-amber-green and
/// both appear on the coach overview, but one describes how a squad answered and the other
/// how far apart two people are about the same player. A page that used one enum for both
/// would eventually colour a difference as though it were a score.
/// </summary>
public enum ScoreLevel
{
    /// <summary>Under two. The same line the follow-up flag is raised at.</summary>
    Low = 0,

    /// <summary>Between two and three and a half. The ordinary middle of the scale.</summary>
    Middling = 1,

    /// <summary>Three and a half or better -- "often" or above on the average statement.</summary>
    Strong = 2
}

/// <summary>
/// The words and class names the three SCORE bands are shown with, next to
/// <see cref="AgreementLevels"/>, which does the same job for difference scores.
///
/// The wording is deliberately not "good" and "bad". These are answers from a fourteen-year
/// old about themselves, read by their coach, and a number the system labels "poor" is a
/// label the player wears. "Needs work" is something a season can act on.
/// </summary>
public static class ScoreLevels
{
    /// <summary>Heading text, e.g. "Needs work".</summary>
    public static string DisplayName(ScoreLevel level) => level switch
    {
        ScoreLevel.Low => "Needs work",
        ScoreLevel.Middling => "On the way",
        ScoreLevel.Strong => "Strength",
        _ => level.ToString()
    };

    /// <summary>"low", "mid" or "strong" -- the suffix used in startcompass.css.</summary>
    public static string CssSuffix(ScoreLevel level) => level switch
    {
        ScoreLevel.Low => "low",
        ScoreLevel.Strong => "strong",
        _ => "mid"
    };

    /// <summary>
    /// The class for a number coloured by its band, e.g. "sc-mean sc-mean--low".
    ///
    /// "sc-mean" and not "sc-score", which is already taken by the difference score CARD on
    /// the player page -- and taken as a block, so a second block of the same name would
    /// have inherited its padding and its border. The two are different measurements as
    /// well as different elements; they should not share a name.
    ///
    /// A number, not a badge: these sit in table cells full of other numbers, and a column
    /// of uppercase badges is unreadable at twenty-five rows.
    /// </summary>
    public static string ScoreClass(ScoreLevel level) => $"sc-mean sc-mean--{CssSuffix(level)}";

    /// <summary>The same for a nullable mean. A mean nobody produced gets no colour.</summary>
    public static string ScoreClass(double? mean) =>
        mean is { } value ? ScoreClass(FiveCRules.LevelOfScore(value)) : "sc-mean sc-mean--none";

    /// <summary>The full badge class, for the one place a band is named rather than shown.</summary>
    public static string BadgeClass(ScoreLevel level) => level switch
    {
        ScoreLevel.Low => "sc-badge sc-badge--alert",
        ScoreLevel.Strong => "sc-badge sc-badge--ok",
        _ => "sc-badge sc-badge--warn"
    };
}
