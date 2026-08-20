namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Navn på rate limit-policyer, på samme måte som <c>Authorization/Policies.cs</c>.
/// Bruk konstantene, ikke strengene.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Strammere grense for handlinger som endrer data eller er dyre å kjøre —
    /// innsending av skjema, eksport, søk. Settes på en action med
    /// <c>[EnableRateLimiting(RateLimitPolicies.Sensitive)]</c>.
    /// </summary>
    public const string Sensitive = "sensitive";
}
