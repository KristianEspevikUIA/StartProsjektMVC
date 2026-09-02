using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// How far apart two of the three respondents are about one player.
///
/// The answers are paired ON THE QUESTION KEY, not on the category average. Two people can
/// land on the same average from opposite answers -- a 5 and a 1 average the same as two 3s
/// -- and a difference built from averages would call that agreement. Only statements BOTH
/// of them answered are counted; there is no distance between an answer and a blank.
///
/// Nothing here is stored. It is recalculated from the raw answers on every request, the
/// same rule the rest of the 5C picture follows.
/// </summary>
/// <param name="Left">The respondent on the left of the comparison, e.g. the coach.</param>
/// <param name="Right">The respondent being compared against, normally the player.</param>
/// <param name="Difference">
/// The score. Mean absolute difference per statement, on the same 1-5 scale the answers
/// use, so it runs from 0 (identical answers) to 4 (opposite ends on every statement).
/// Direction is stripped out on purpose: rating a player too high and rating them too low
/// are both being wrong about them by the same amount.
/// </param>
/// <param name="SignedDifference">
/// The same distance with its direction kept: the mean of (left - right). Positive means
/// the left respondent rated the player HIGHER than the right one did. Disagreement in both
/// directions cancels out here while <see cref="Difference"/> does not, which is exactly
/// what tells "consistently rates higher" apart from "disagrees all over the place".
/// </param>
/// <param name="LargestSingleDifference">The widest gap on any single statement.</param>
/// <param name="ComparedAnswers">How many statements both of them answered.</param>
public sealed record RespondentGap(
    RespondentType Left,
    RespondentType Right,
    double Difference,
    double SignedDifference,
    double LargestSingleDifference,
    int ComparedAnswers)
{
    /// <summary>How big the difference is, in words. See <see cref="FiveCRules.LevelOf"/>.</summary>
    public AgreementLevel Level => FiveCRules.LevelOf(Difference);

    /// <summary>
    /// Heading for the score card, e.g. "Player and coach".
    ///
    /// "And", not "vs" -- the coaching team asked for it, and they are right. The number is
    /// how far apart two readings of the same player are, not a contest between two people,
    /// and the heading sits directly above a conversation the coach is about to have.
    ///
    /// The player is named first whenever they are one of the pair. Their own answers are
    /// the thing everything else is read against.
    /// </summary>
    public string Label
    {
        get
        {
            var (first, second) = Left == RespondentType.Player || Right == RespondentType.Player
                ? (RespondentType.Player, Left == RespondentType.Player ? Right : Left)
                : (Left, Right);

            return $"{DisplayName(first)} and {DisplayName(second).ToLowerInvariant()}";
        }
    }

    /// <summary>Display name for a respondent. One place, so the charts and the cards agree.</summary>
    public static string DisplayName(RespondentType respondent) => respondent switch
    {
        RespondentType.Player => "Player",
        RespondentType.Coach => "Coach",
        RespondentType.Guardian => "Guardian",
        _ => respondent.ToString()
    };

    /// <summary>
    /// Measures the distance between two sets of scored answers, keyed by question.
    ///
    /// Returns null when the two share no answered statement -- which covers the normal
    /// case of one of them simply not having filled the form in. Null is "nothing to
    /// compare", and is not the same as a difference of zero.
    /// </summary>
    /// <param name="leftRespondent">Who the left scores belong to.</param>
    /// <param name="left">Scores for the left respondent: question key to score.</param>
    /// <param name="rightRespondent">Who the right scores belong to.</param>
    /// <param name="right">Scores for the right respondent: question key to score.</param>
    public static RespondentGap? Between(
        RespondentType leftRespondent,
        IReadOnlyDictionary<string, int> left,
        RespondentType rightRespondent,
        IReadOnlyDictionary<string, int> right)
    {
        double total = 0;
        double signedTotal = 0;
        double largest = 0;
        var compared = 0;

        foreach (var (questionKey, leftScore) in left)
        {
            if (!right.TryGetValue(questionKey, out var rightScore))
            {
                continue;
            }

            var signed = leftScore - rightScore;
            var distance = Math.Abs(signed);

            total += distance;
            signedTotal += signed;
            largest = Math.Max(largest, distance);
            compared++;
        }

        if (compared == 0)
        {
            return null;
        }

        return new RespondentGap(
            Left: leftRespondent,
            Right: rightRespondent,
            Difference: total / compared,
            SignedDifference: signedTotal / compared,
            LargestSingleDifference: largest,
            ComparedAnswers: compared);
    }
}

/// <summary>
/// The three difference scores for one player: coach against player, guardian against
/// player, and one across everyone who answered.
///
/// Any of the pairs can be null. A guardian who has not filled the form in produces no
/// guardian score, and the page says so rather than showing a zero -- "they agree perfectly"
/// and "one of them never answered" are not the same result.
/// </summary>
/// <param name="CoachVsPlayer">Coach against the player's own answers.</param>
/// <param name="GuardianVsPlayer">Guardian against the player's own answers.</param>
/// <param name="CoachVsGuardian">
/// Coach against guardian. Not one of the two headline scores, but it is what makes
/// <see cref="Overall"/> a score "between all" rather than two separate scores about
/// the player.
/// </param>
public sealed record DifferenceScores(
    RespondentGap? CoachVsPlayer,
    RespondentGap? GuardianVsPlayer,
    RespondentGap? CoachVsGuardian)
{
    /// <summary>Nothing to compare. Used for a category or a player with no answers at all.</summary>
    public static readonly DifferenceScores None = new(null, null, null);

    /// <summary>The pairs that could actually be measured, in display order.</summary>
    public IReadOnlyList<RespondentGap> Pairs
    {
        get
        {
            var pairs = new List<RespondentGap>(3);

            if (CoachVsPlayer is { } coachPlayer) pairs.Add(coachPlayer);
            if (GuardianVsPlayer is { } guardianPlayer) pairs.Add(guardianPlayer);
            if (CoachVsGuardian is { } coachGuardian) pairs.Add(coachGuardian);

            return pairs;
        }
    }

    public bool HasAny => Pairs.Count > 0;

    /// <summary>
    /// The score between all of them: the mean of whichever pairwise differences exist.
    ///
    /// Averaging the PAIRS rather than the raw answers is what makes this fall back
    /// sensibly. With all three it is the average of coach-player, guardian-player and
    /// coach-guardian, so no pair weighs more just because two of them answered more
    /// statements than the third. With only two of them it is that single pair, which is
    /// the truthful answer to "how far apart is everyone" when everyone is two people.
    /// </summary>
    public double? Overall
    {
        get
        {
            var pairs = Pairs;
            return pairs.Count == 0 ? null : pairs.Average(p => p.Difference);
        }
    }

    public AgreementLevel? OverallLevel =>
        Overall is { } value ? FiveCRules.LevelOf(value) : null;

    /// <summary>
    /// True when all three answered, so <see cref="Overall"/> covers the whole triangle.
    /// The view says which of the two it is rather than letting a two-way number pass for
    /// a three-way one.
    /// </summary>
    public bool IsCompleteTriangle => Pairs.Count == 3;

    /// <summary>The pair furthest apart, or null when nothing could be compared.</summary>
    public RespondentGap? Largest =>
        Pairs.OrderByDescending(p => p.Difference).FirstOrDefault();
}
