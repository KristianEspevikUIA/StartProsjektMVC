using StartPraksisGruppe3Prosjekt.Models.Identity;
using StartPraksisGruppe3Prosjekt.Services.Identity;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Where each status starts and stops. The boundaries are the part worth writing down: "inside
/// the range" has to include both ends, and PPDA has to run the other way.
///
/// Synthetic markers with round numbers, so a boundary is a boundary and not a floating-point
/// accident. The shipped thresholds are held to the club's document in IdentityCatalogTests.
/// </summary>
public class IdentityStatusRulesTests
{
    private static readonly StatusRules Rules = new()
    {
        StrongAlignmentAtFractionOfMin = 0.90,
        DevelopingAtFractionOfMin = 0.75
    };

    // Elite range 40-80. Strong from 36, Developing from 30.
    private static readonly IdentityMarker HigherIsBetter = new()
    {
        Key = "test-higher",
        Target = new MarkerTarget { DirectionName = "higher-is-better", Min = 40, Max = 80 }
    };

    // The shape of PPDA: Exceptional at or below 5.04, Elite under 10.
    private static readonly IdentityMarker LowerIsBetter = new()
    {
        Key = "test-lower",
        Target = new MarkerTarget
        {
            DirectionName = "lower-is-better",
            Max = 10.0,
            ExceptionalAtOrBelow = 5.04,
            StrongAlignmentAtOrBelow = 11.0,
            DevelopingAtOrBelow = 12.5
        }
    };

    [Theory]
    [InlineData(80.5, IdentityStatus.Exceptional)]
    [InlineData(80, IdentityStatus.EliteAlignment)]      // the top of the range is inside it
    [InlineData(40, IdentityStatus.EliteAlignment)]      // and so is the bottom
    [InlineData(39.9, IdentityStatus.StrongAlignment)]
    [InlineData(36, IdentityStatus.StrongAlignment)]     // 90 % of the floor
    [InlineData(35.9, IdentityStatus.Developing)]
    [InlineData(30, IdentityStatus.Developing)]          // 75 % of the floor
    [InlineData(29.9, IdentityStatus.BelowTarget)]
    [InlineData(0, IdentityStatus.BelowTarget)]
    public void Higher_is_better(double value, IdentityStatus expected) =>
        Assert.Equal(expected, IdentityStatusRules.Evaluate(HigherIsBetter, value, Rules));

    [Theory]
    [InlineData(4.0, IdentityStatus.Exceptional)]
    [InlineData(5.04, IdentityStatus.Exceptional)]       // Barcelona's peak is the ceiling, and counts
    [InlineData(5.05, IdentityStatus.EliteAlignment)]
    [InlineData(9.99, IdentityStatus.EliteAlignment)]
    [InlineData(10.0, IdentityStatus.StrongAlignment)]   // "Under 10.0" -- 10 itself is not under
    [InlineData(11.0, IdentityStatus.StrongAlignment)]
    [InlineData(11.01, IdentityStatus.Developing)]
    [InlineData(12.5, IdentityStatus.Developing)]
    [InlineData(12.51, IdentityStatus.BelowTarget)]
    public void Lower_is_better(double value, IdentityStatus expected) =>
        Assert.Equal(expected, IdentityStatusRules.Evaluate(LowerIsBetter, value, Rules));

    [Fact]
    public void The_thresholds_come_from_the_rules_not_from_the_code()
    {
        var stricter = new StatusRules { StrongAlignmentAtFractionOfMin = 0.95, DevelopingAtFractionOfMin = 0.5 };

        Assert.Equal(IdentityStatus.Developing, IdentityStatusRules.Evaluate(HigherIsBetter, 37, stricter));
        Assert.Equal(IdentityStatus.Developing, IdentityStatusRules.Evaluate(HigherIsBetter, 20, stricter));
        Assert.Equal(IdentityStatus.BelowTarget, IdentityStatusRules.Evaluate(HigherIsBetter, 19.9, stricter));
    }

    [Fact]
    public void The_legend_describes_the_thresholds_in_use()
    {
        Assert.Equal("Up to 10% short of the range", IdentityStatusRules.Describe(IdentityStatus.StrongAlignment, Rules));
        Assert.Equal("10–25% short of the range", IdentityStatusRules.Describe(IdentityStatus.Developing, Rules));
        Assert.Equal("More than 25% short of the range", IdentityStatusRules.Describe(IdentityStatus.BelowTarget, Rules));
    }

    [Fact]
    public void Every_status_has_words_and_a_css_class()
    {
        // Status is never shown by colour alone, and the colours are a fixed set of classes.
        Assert.Equal(5, IdentityStatuses.All.Count);
        Assert.All(IdentityStatuses.All, status =>
        {
            Assert.False(string.IsNullOrWhiteSpace(IdentityStatuses.Label(status)));
            Assert.Matches("^[a-z-]+$", IdentityStatuses.CssKey(status));
        });
    }
}
