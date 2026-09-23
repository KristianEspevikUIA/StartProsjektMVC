namespace StartPraksisGruppe3Prosjekt.Models.Identity;

/// <summary>
/// How a value sits against a marker's Elite range. The same five categories, and the same
/// colours, as the club's U14 Identity Comparison document -- and nothing else: that document
/// also used "Strong Intensity" and "Strong Press", which are not categories.
///
/// Declared worst to best, so the order is also the legend's.
/// </summary>
public enum IdentityStatus
{
    BelowTarget,
    Developing,
    StrongAlignment,
    EliteAlignment,
    Exceptional
}

public static class IdentityStatuses
{
    public static IReadOnlyList<IdentityStatus> All { get; } = Enum.GetValues<IdentityStatus>();

    /// <summary>The words in the badge. Status is never shown by colour alone.</summary>
    public static string Label(IdentityStatus status) => status switch
    {
        IdentityStatus.BelowTarget => "Below Target",
        IdentityStatus.Developing => "Developing",
        IdentityStatus.StrongAlignment => "Strong Alignment",
        IdentityStatus.EliteAlignment => "Elite Alignment",
        IdentityStatus.Exceptional => "Exceptional",
        _ => status.ToString()
    };

    /// <summary>
    /// The CSS modifier, e.g. "elite-alignment" for sc-id-status--elite-alignment. A fixed set
    /// of classes rather than a colour in the markup: the CSP has no unsafe-inline.
    /// </summary>
    public static string CssKey(IdentityStatus status) => status switch
    {
        IdentityStatus.BelowTarget => "below-target",
        IdentityStatus.Developing => "developing",
        IdentityStatus.StrongAlignment => "strong-alignment",
        IdentityStatus.EliteAlignment => "elite-alignment",
        IdentityStatus.Exceptional => "exceptional",
        _ => "below-target"
    };
}
