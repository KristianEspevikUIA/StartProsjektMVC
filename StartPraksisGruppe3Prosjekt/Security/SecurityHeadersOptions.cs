namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Innstillinger for <see cref="SecurityHeadersMiddleware"/>. Kan overstyres fra
/// konfigurasjon under seksjonen <c>Security:Headers</c>.
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "Security:Headers";

    /// <summary>
    /// Sender CSP-en som <c>Content-Security-Policy-Report-Only</c> i stedet for å
    /// håndheve den. Bare til feilsøking — standard er å håndheve, også i utvikling,
    /// slik at brudd oppdages mens koden skrives og ikke i produksjon.
    /// </summary>
    public bool ReportOnly { get; set; }

    /// <summary>
    /// Legger til <c>upgrade-insecure-requests</c>. Av i utvikling, fordi
    /// http-profilen i launchSettings ellers får ressursene sine oppgradert til https
    /// mot en port som ikke svarer.
    /// </summary>
    public bool UpgradeInsecureRequests { get; set; } = true;
}
