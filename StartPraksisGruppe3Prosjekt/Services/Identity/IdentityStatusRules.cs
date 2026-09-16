using System.Globalization;
using StartPraksisGruppe3Prosjekt.Models.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>
/// Turns a value into one of the five statuses. Every threshold comes from gold-standard.json;
/// nothing here is a number of its own.
///
/// Higher is better (nine of the ten markers):
///   above max                                  Exceptional
///   min .. max, both included                  Elite Alignment
///   at least strongAlignmentAtFractionOfMin*min Strong Alignment
///   at least developingAtFractionOfMin*min     Developing
///   lower                                      Below Target
///
/// Lower is better (PPDA), with absolute thresholds on the marker itself:
///   at or below exceptionalAtOrBelow           Exceptional
///   below max                                  Elite Alignment
///   at or below strongAlignmentAtOrBelow       Strong Alignment
///   at or below developingAtOrBelow            Developing
///   higher                                     Below Target
///
/// A marker that is not measured has no status at all. That is the caller's decision -- there
/// is no value to pass in.
/// </summary>
public static class IdentityStatusRules
{
    public static IdentityStatus Evaluate(IdentityMarker marker, double value, StatusRules rules)
    {
        var target = marker.Target;

        return target.Direction switch
        {
            TargetDirection.HigherIsBetter => HigherIsBetter(value, target.Min!.Value, target.Max!.Value, rules),
            TargetDirection.LowerIsBetter => LowerIsBetter(value, target),
            _ => throw new InvalidOperationException(
                $"Marker '{marker.Key}' has no known direction ('{target.DirectionName}').")
        };
    }

    private static IdentityStatus HigherIsBetter(double value, double min, double max, StatusRules rules)
    {
        if (value > max)
        {
            return IdentityStatus.Exceptional;
        }

        if (value >= min)
        {
            return IdentityStatus.EliteAlignment;
        }

        if (value >= min * rules.StrongAlignmentAtFractionOfMin)
        {
            return IdentityStatus.StrongAlignment;
        }

        return value >= min * rules.DevelopingAtFractionOfMin
            ? IdentityStatus.Developing
            : IdentityStatus.BelowTarget;
    }

    private static IdentityStatus LowerIsBetter(double value, MarkerTarget target)
    {
        if (value <= target.ExceptionalAtOrBelow!.Value)
        {
            return IdentityStatus.Exceptional;
        }

        if (value < target.Max!.Value)
        {
            return IdentityStatus.EliteAlignment;
        }

        if (value <= target.StrongAlignmentAtOrBelow!.Value)
        {
            return IdentityStatus.StrongAlignment;
        }

        return value <= target.DevelopingAtOrBelow!.Value
            ? IdentityStatus.Developing
            : IdentityStatus.BelowTarget;
    }

    /// <summary>
    /// What each status means, in words, for the legend -- worked out from the same rules, so
    /// the legend cannot describe thresholds the page is not using.
    /// </summary>
    public static string Describe(IdentityStatus status, StatusRules rules) => status switch
    {
        IdentityStatus.Exceptional => "Beyond the elite range",
        IdentityStatus.EliteAlignment => "Inside the elite range",
        IdentityStatus.StrongAlignment =>
            $"Up to {Percent(1 - rules.StrongAlignmentAtFractionOfMin)} short of the range",
        IdentityStatus.Developing =>
            $"{Percent(1 - rules.StrongAlignmentAtFractionOfMin, withSign: false)}–" +
            $"{Percent(1 - rules.DevelopingAtFractionOfMin)} short of the range",
        IdentityStatus.BelowTarget =>
            $"More than {Percent(1 - rules.DevelopingAtFractionOfMin)} short of the range",
        _ => string.Empty
    };

    private static string Percent(double share, bool withSign = true) =>
        (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + (withSign ? "%" : string.Empty);
}
