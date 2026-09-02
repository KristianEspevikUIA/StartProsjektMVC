using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Whether a period is open. It decides whether a form can be answered at all, so the two
/// ends of the window are worth pinning down rather than leaving to whoever reads the
/// comparison next.
/// </summary>
public class SurveyRoundTests
{
    private static readonly DateTimeOffset Opens = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Closes = new(2026, 11, 30, 23, 59, 59, TimeSpan.Zero);

    private static SurveyRound Round() => new()
    {
        Name = "Autumn 2026",
        OpensAt = Opens,
        ClosesAt = Closes
    };

    [Fact]
    public void Open_in_the_middle_of_the_window() =>
        Assert.True(Round().IsOpenAt(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Both_ends_are_inclusive()
    {
        // A period that closes at 23:59:59 has to accept an answer at 23:59:59. The admin
        // form relies on it: it builds the closing instant as the last second of the day.
        Assert.True(Round().IsOpenAt(Opens));
        Assert.True(Round().IsOpenAt(Closes));
    }

    [Fact]
    public void Closed_outside_the_window()
    {
        Assert.False(Round().IsOpenAt(Opens.AddTicks(-1)));
        Assert.False(Round().IsOpenAt(Closes.AddTicks(1)));
    }

    [Fact]
    public void Open_is_compared_across_offsets()
    {
        // Answers are stored in UTC and read in local time. An instant that is inside the
        // window is inside it whichever offset it is written with.
        var sameInstantInOslo = new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.FromHours(2));

        Assert.True(Round().IsOpenAt(sameInstantInOslo));
    }

    [Fact]
    public void A_period_result_only_succeeds_with_a_round()
    {
        Assert.True(PeriodResult.Ok(Round()).Succeeded);
        Assert.False(PeriodResult.Failed("nope").Succeeded);
    }
}
