using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Setter sikkerhetshoder på hvert svar, og lager en CSP-nonce per forespørsel.
///
/// Razor koder alt som skrives med <c>@</c>, så den vanlige XSS-veien er allerede
/// stengt. CSP er nettet under: skulle noen likevel få inn markup — via
/// <c>Html.Raw</c>, et attributt eller en pakke — har nettleseren fortsatt beskjed om
/// å nekte å kjøre skript som ikke kommer fra vårt eget domene med riktig nonce.
///
/// Middlewaren skal ligge først i pipelinen, slik at hodene også følger med på
/// statiske filer og på feilsvar.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>Nøkkelen nonce-en ligger på i <see cref="HttpContext.Items"/>.</summary>
    internal const string NonceKey = "csp-nonce";

    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var nonce = CreateNonce();
        context.Items[NonceKey] = nonce;

        var headers = context.Response.Headers;

        headers[_options.ReportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy"]
            = BuildContentSecurityPolicy(nonce, _options.UpgradeInsecureRequests);

        // Ingen MIME-gjetting: en opplastet fil skal aldri kunne tolkes som skript.
        headers["X-Content-Type-Options"] = "nosniff";

        // Klikkjacking. frame-ancestors i CSP-en gjør det samme for nyere nettlesere,
        // X-Frame-Options er med for de eldre.
        headers["X-Frame-Options"] = "DENY";

        // Ingen URL-er lekker ut i Referer. URL-ene her inneholder spiller-ID-er.
        headers["Referrer-Policy"] = "no-referrer";

        headers["Permissions-Policy"] =
            "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), " +
            "microphone=(), payment=(), usb=(), interest-cohort=()";

        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // Den gamle XSS-filteret i nettlesere innførte egne sårbarheter og er fjernet
        // fra alle moderne nettlesere. 0 slår det eksplisitt av; CSP-en er vernet.
        headers["X-XSS-Protection"] = "0";

        // Sider for innloggede brukere skal ikke ligge igjen i nettleserens cache.
        // Delt PC hjemme eller på klubbhuset er normalen her, ikke unntaket.
        // Sjekken må skje når svaret starter — da vet vi både hvem brukeren er og
        // om dette faktisk er en HTML-side.
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var isHtml = ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;

            if (isHtml && ctx.User.Identity?.IsAuthenticated == true)
            {
                ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
                ctx.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }

    private static string CreateNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private static string BuildContentSecurityPolicy(string nonce, bool upgradeInsecureRequests)
    {
        var directives = new List<string>
        {
            "default-src 'self'",
            "base-uri 'self'",
            "object-src 'none'",
            "frame-src 'none'",
            "frame-ancestors 'none'",
            "media-src 'none'",
            "form-action 'self'",          // et innsendt skjema kan bare gå til oss
            $"script-src 'self' 'nonce-{nonce}'",
            "style-src 'self'",
            "img-src 'self' data:",        // data: fordi Bootstrap legger ikoner i CSS
            "font-src 'self'",
            "connect-src 'self'",
            "manifest-src 'self'"
        };

        if (upgradeInsecureRequests)
        {
            directives.Add("upgrade-insecure-requests");
        }

        return string.Join("; ", directives);
    }
}
