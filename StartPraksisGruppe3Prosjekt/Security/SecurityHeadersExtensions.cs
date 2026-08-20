namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Registrering og bruk av <see cref="SecurityHeadersMiddleware"/>, og tilgang til
/// CSP-nonce-en fra views.
/// </summary>
public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.Configure<SecurityHeadersOptions>(options =>
        {
            options.UpgradeInsecureRequests = !environment.IsDevelopment();
        });

        // Konfigurasjon vinner over standardverdiene over.
        services.Configure<SecurityHeadersOptions>(
            configuration.GetSection(SecurityHeadersOptions.SectionName));

        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();

    /// <summary>
    /// Nonce-en for denne forespørselen. Et <c>&lt;script&gt;</c> skrevet rett i en
    /// view må ha <c>nonce="@Context.GetCspNonce()"</c> for å få lov til å kjøre.
    /// Skript i egne filer under <c>wwwroot</c> trenger den ikke.
    /// </summary>
    public static string GetCspNonce(this HttpContext context)
        => context.Items[SecurityHeadersMiddleware.NonceKey] as string ?? string.Empty;
}
