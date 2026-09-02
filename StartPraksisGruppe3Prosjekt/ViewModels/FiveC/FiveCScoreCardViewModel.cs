using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// One difference score, as the card on the page shows it: the number, how big it is in
/// words, and one sentence saying what it means.
///
/// The sentences are built here rather than in the .cshtml so that the same score is never
/// described two different ways on two different pages. Nothing here is stored -- it is a
/// rendering of <see cref="RespondentGap"/>, which is itself recalculated per request.
///
/// The wording deliberately names the roles ("The coach rated ...") rather than addressing
/// the reader as "you". An administrator opens the same page, and for them "you" would be
/// simply wrong.
/// </summary>
public sealed record FiveCScoreCardViewModel
{
    /// <summary>Heading, e.g. "Player and coach".</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The score: mean absolute difference per statement, 0 to 4. Null when the two sides
    /// share no answered statement, which the card shows as "not comparable" rather than
    /// as a zero.
    /// </summary>
    public double? Score { get; init; }

    public AgreementLevel? Level { get; init; }

    /// <summary>How many statements the score is built on. Zero when there is no score.</summary>
    public int ComparedAnswers { get; init; }

    /// <summary>The sentence under the number. Either what it means, or why it is missing.</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>"coach", "guardian" or "all" -- picks the accent stripe on the card.</summary>
    public string Accent { get; init; } = "all";

    /// <summary>True when there is a number to show.</summary>
    public bool HasScore => Score.HasValue;

    /// <summary>
    /// The number as a share of the widest possible difference, for the meter. A score of 4
    /// is the two of them at opposite ends of the scale on every single statement.
    /// </summary>
    public int MeterWidth => Score is { } score
        ? (int)Math.Round(Math.Clamp(score / MaximumDifference, 0, 1) * 100)
        : 0;

    /// <summary>The widest a difference can be: the full 1-5 scale, end to end.</summary>
    public const double MaximumDifference = FiveCRules.ScaleMax - FiveCRules.ScaleMin;

    /// <summary>
    /// A card for one pair, e.g. coach against player.
    /// </summary>
    /// <param name="gap">The measured gap, or null when one of the two has not answered.</param>
    /// <param name="playerCode">Player code for the sentence. Codes, not names.</param>
    /// <param name="missingReason">What to say when <paramref name="gap"/> is null.</param>
    /// <param name="accent">Accent stripe: "coach", "guardian" or "all".</param>
    public static FiveCScoreCardViewModel ForGap(
        RespondentGap? gap,
        RespondentType left,
        RespondentType right,
        string playerCode,
        string missingReason,
        string accent) =>
        gap is null
            ? new FiveCScoreCardViewModel
            {
                // Built from the pair, not from the accent string. The title has to be the
                // same before and after the second person answers, and deriving it from a
                // css class name meant every new pair needed another branch here.
                Title = RespondentGap.LabelFor(left, right),
                Explanation = missingReason,
                Accent = accent
            }
            : new FiveCScoreCardViewModel
            {
                Title = gap.Label,
                Score = gap.Difference,
                Level = gap.Level,
                ComparedAnswers = gap.ComparedAnswers,
                Explanation = Describe(gap, playerCode),
                Accent = accent
            };

    /// <summary>
    /// The card for the score between everyone who answered.
    /// </summary>
    public static FiveCScoreCardViewModel ForOverall(DifferenceScores scores, string playerCode)
    {
        if (scores.Overall is not { } overall)
        {
            return new FiveCScoreCardViewModel
            {
                Title = "Between all three",
                Explanation =
                    $"Only one form has been submitted about {playerCode}, so there is " +
                    "nothing to compare it against yet.",
                Accent = "all"
            };
        }

        var level = FiveCRules.LevelOf(overall);

        var explanation = scores.IsCompleteTriangle
            ? $"The average distance across all three pairs — {playerCode} and the coach, " +
              $"{playerCode} and the guardian, and the coach and the guardian."
            : "Only two of the three have answered, so this is that one pair. It becomes a " +
              "three-way score once the third form is in.";

        // Name the widest pair only when it is actually the widest AS SHOWN. Two pairs that
        // both print as "0,8" have no widest one as far as the reader is concerned, and
        // singling one out reads as an error on the page.
        if (scores.Pairs.Count > 1 && scores.Largest is { } largest)
        {
            var runnerUp = scores.Pairs
                .OrderByDescending(p => p.Difference)
                .Skip(1)
                .First();

            if (Shown(largest.Difference) > Shown(runnerUp.Difference))
            {
                explanation += $" The widest pair is {largest.Label.ToLowerInvariant()}, " +
                               $"at {largest.Difference.ToString("0.0")}.";
            }
        }

        return new FiveCScoreCardViewModel
        {
            Title = scores.IsCompleteTriangle ? "Between all three" : "Between all who answered",
            Score = overall,
            Level = level,
            ComparedAnswers = scores.Pairs.Max(p => p.ComparedAnswers),
            Explanation = explanation,
            Accent = "all"
        };
    }

    /// <summary>
    /// A difference rounded the way the page prints it. Comparisons that the reader is
    /// meant to be able to check against the numbers on screen go through here.
    /// </summary>
    private static double Shown(double difference) =>
        Math.Round(difference, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What a pairwise gap means, in one sentence.
    ///
    /// Both numbers are used and they say different things. The signed average is the
    /// direction -- consistently rating the player higher or lower. The unsigned score is
    /// the distance. When the distance is much larger than the direction, the two of them
    /// are not simply optimistic or pessimistic about the player; they disagree in both
    /// directions, which reads very differently in a conversation.
    /// </summary>
    private static string Describe(RespondentGap gap, string playerCode)
    {
        var left = RespondentGap.DisplayName(gap.Left).ToLowerInvariant();
        var right = RespondentGap.DisplayName(gap.Right).ToLowerInvariant();

        // "than they rated themselves" only parses when the right-hand side IS the player.
        var against = gap.Right == RespondentType.Player
            ? "than the player rated themselves"
            : $"than the {right} did";

        var signed = gap.SignedDifference;
        var hasDirection = Math.Abs(signed) >= 0.05;

        // A direction near zero on top of a real distance is disagreement that CANCELS OUT,
        // not agreement: they disagree statement by statement, in both directions, and the
        // averages happen to meet in the middle. Reporting only the direction would hide it.
        var cancelsOut = gap.Difference >= FiveCRules.AgreementThreshold
                         && Math.Abs(signed) < gap.Difference / 2;

        string sentence;

        if (hasDirection)
        {
            var word = signed > 0 ? "higher" : "lower";

            sentence = $"On average the {left} rated {playerCode} " +
                       $"{Math.Abs(signed).ToString("0.0")} {word} {against}";

            sentence += cancelsOut
                ? " — but they disagree in both directions statement by statement, so the " +
                  "averages hide more than they show."
                : ".";
        }
        else if (cancelsOut)
        {
            sentence = $"The {left} and the {right} average out to the same place, but " +
                       "disagree in both directions statement by statement, so the averages " +
                       "hide more than they show.";
        }
        else
        {
            sentence = $"The {left} and the {right} answered much the same way.";
        }

        return $"{sentence} Measured over {gap.ComparedAnswers} " +
               $"statement{(gap.ComparedAnswers == 1 ? "" : "s")} they both answered.";
    }
}
