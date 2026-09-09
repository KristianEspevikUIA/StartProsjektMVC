using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// The order the twenty-five statements are put in front of one respondent.
///
/// The catalog order -- five C's, five statements each, always the same -- is a form you
/// can answer without reading. Five statements about commitment in a row train the reader
/// that the next one is about commitment too, and by the third period a player is clicking
/// down a column. Shuffling them breaks that, which is the whole point.
///
/// Two properties matter, and they pull against each other:
///
///   STABLE within a period. A respondent who saves, comes back and corrects one answer has
///   to meet the same form, in the same order. An order that was random per REQUEST would
///   renumber the statements under them mid-correction.
///
///   DIFFERENT between periods. Otherwise the shuffle is decoration: the same order every
///   September is the same autopilot as the catalog order, one permutation along.
///
/// Both fall out of seeding on (player, period) and deriving the permutation from that seed
/// rather than storing it. Nothing is written down, nothing has to be migrated, and the
/// same order can be reproduced later from the two ids alone.
///
/// Seeded on the PLAYER and not on the respondent, deliberately. It means the player, their
/// guardian and their coach all answer about that player in the same sequence, which is what
/// makes "you said 5 on the fourth one" a sentence two people can have. A coach with twenty
/// players still gets twenty different orders, so the coach's own autopilot is broken too.
/// </summary>
public interface IQuestionOrder
{
    /// <summary>
    /// The whole question set for one player in one period, shuffled, with each statement's
    /// category and colour alongside it. Every question in the catalog appears exactly once.
    /// </summary>
    IReadOnlyList<OrderedQuestion> For(int playerId, int roundId);
}

/// <summary>One statement in the order a respondent meets it.</summary>
/// <param name="Question">The statement itself, from the catalog.</param>
/// <param name="Category">
/// The category it belongs to. Carried alongside because the sequence no longer groups by
/// category -- the answer is still stored under this category key, and the reader still
/// needs to be able to tell that two statements belong together.
/// </param>
/// <param name="Color">
/// The palette name the statement is marked with, from
/// <see cref="IQuestionCatalog.ColorForQuestion"/>.
/// </param>
/// <param name="Number">
/// Position in THIS respondent's sequence, from 1. Display only, and no longer the same
/// number for two people: it is where the statement sits on this form, not what the
/// statement is called. The stable name is <see cref="Models.FiveC.Question.Key"/>.
/// </param>
public sealed record OrderedQuestion(
    Question Question,
    QuestionCategory Category,
    string Color,
    int Number);
