using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <inheritdoc cref="IQuestionOrder" />
///
/// <remarks>
/// Registered as a singleton next to <see cref="QuestionCatalog"/>. It holds no state per
/// request: the same two ids give the same order on every call, on every server, for as
/// long as the question set is unchanged.
/// </remarks>
public sealed class QuestionOrder : IQuestionOrder
{
    /// <summary>
    /// How many statements are shown together on the form. Five keeps the page the same
    /// size it was when the panels were the five C's -- the panels are now arbitrary
    /// blocks, but the amount of work per screen is what a respondent actually feels.
    /// </summary>
    public const int BlockSize = 5;

    private readonly IQuestionCatalog _catalog;

    public QuestionOrder(IQuestionCatalog catalog) => _catalog = catalog;

    /// <inheritdoc />
    public IReadOnlyList<OrderedQuestion> For(int playerId, int roundId)
    {
        // Built fresh rather than cached. Twenty-five items and one Fisher-Yates pass is
        // less work than the dictionary lookup a cache would need, and a cache keyed on
        // (player, round) in a club with three hundred players is a slow memory leak.
        var items = _catalog.Questions.Categories
            .SelectMany(category => category.Questions.Select(question => (question, category)))
            .ToArray();

        var random = new SplitMix64(SeedFor(playerId, roundId));

        // Fisher-Yates, walked from the end. The standard shuffle: every permutation is
        // equally likely, which the "sort by a random key" version is not.
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = (int)random.Next((uint)(i + 1));
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items
            .Select((item, index) => new OrderedQuestion(
                Question: item.question,
                Category: item.category,
                Color: _catalog.ColorForQuestion(item.question.Key),
                Number: index + 1))
            .ToList();
    }

    /// <summary>
    /// The seed for one (player, period). Both ids are folded in rather than added: player
    /// 3 in period 4 and player 4 in period 3 are different forms, and a plain sum would
    /// hand them the same order.
    ///
    /// The question set version is deliberately NOT part of the seed. Correcting a typo in
    /// a statement mid-period would otherwise reshuffle the form under everybody who had
    /// already answered it.
    /// </summary>
    private static ulong SeedFor(int playerId, int roundId) =>
        SplitMix64.Mix(((ulong)(uint)playerId << 32) | (uint)roundId);

    /// <summary>
    /// A small deterministic generator, written out rather than taken from
    /// <see cref="Random"/>.
    ///
    /// System.Random with a fixed seed does not promise the same sequence across .NET
    /// versions -- and it changed in .NET 6. The order here has to survive a runtime
    /// upgrade in the middle of a measurement period, or a respondent who came back to
    /// correct one answer meets a form whose statements have moved.
    ///
    /// SplitMix64 is the usual choice for this: a handful of constants, no state beyond a
    /// single ulong, and good enough distribution for shuffling twenty-five items.
    /// </summary>
    private struct SplitMix64
    {
        /// <summary>The odd constant SplitMix64 walks its counter by. Any odd value works.</summary>
        private const ulong Gamma = 0x9E3779B97F4A7C15UL;

        /// <summary>The counter. The OUTPUT is a mix of it, and is never fed back in.</summary>
        private ulong _counter;

        public SplitMix64(ulong seed) => _counter = seed;

        /// <summary>
        /// The avalanche step: scrambles one counter value into an output. Also used on its
        /// own to fold two ids into a seed.
        /// </summary>
        public static ulong Mix(ulong value)
        {
            var z = value;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>The next 64 bits. The counter advances; the output is a mix of it.</summary>
        private ulong NextRaw()
        {
            unchecked
            {
                _counter += Gamma;
            }

            return Mix(_counter);
        }

        /// <summary>
        /// A value in [0, bound). Rejection sampling rather than a plain modulo: 2^64 is not
        /// a multiple of 25, so a bare modulo makes the lowest indices very slightly more
        /// likely -- and a shuffle is exactly where a biased index shows up.
        ///
        /// Everything at or above 2^64 - (2^64 mod bound) is thrown away, which leaves a
        /// whole number of complete cycles. The loop runs more than once about as often as
        /// bound/2^64, which is to say never.
        /// </summary>
        public ulong Next(uint bound)
        {
            // 2^64 mod bound, in arithmetic that has no 2^64: -bound wraps to 2^64 - bound.
            var threshold = unchecked(0UL - bound) % bound;

            while (true)
            {
                var value = NextRaw();

                if (value >= threshold)
                {
                    return value % bound;
                }
            }
        }
    }
}
