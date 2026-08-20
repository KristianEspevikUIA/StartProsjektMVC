using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Rate limiting. To lag:
///
/// 1. En global grense per IP-adresse, som demper skraping og støy.
/// 2. En mye strammere grense på POST mot Identity-sidene. Kontolåsing i Identity
///    beskytter én konto om gangen; den hindrer ikke at noen prøver ett passord mot
///    hundre kontoer. Det gjør denne.
///
/// Grensene teller per IP-adresse. Kjører appen bak en proxy eller lastbalanserer må
/// <c>ForwardedHeaders</c> settes opp først, ellers havner alle bak samme adresse.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddSpeiletRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"all:{ClientKey(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 240,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        })),

                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsAuthenticationAttempt(context)
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"auth:{ClientKey(context)}",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 10,
                                Window = TimeSpan.FromMinutes(5),
                                QueueLimit = 0
                            })
                        : RateLimitPartition.GetNoLimiter<string>("auth:none")));

            options.AddPolicy(RateLimitPolicies.Sensitive, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"sensitive:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? value
                    : TimeSpan.FromMinutes(1);

                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                // IP-adressen logges fordi gjentatte avvisninger er det eneste sporet
                // vi har av et påloggingsforsøk i stor skala. Ingen brukerdata.
                logger.LogWarning(
                    "Forespørsel avvist av rate limit: {Method} {Path} fra {RemoteIp}",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    context.HttpContext.Connection.RemoteIpAddress);

                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    "For mange forespørsler. Vent litt og prøv igjen.", cancellationToken);
            };
        });

        return services;
    }

    private static string ClientKey(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "ukjent";

    /// <summary>
    /// Alt som postes mot Identity: innlogging, registrering, glemt passord,
    /// tofaktor. Vanlige GET-visninger av de samme sidene rammes ikke.
    /// </summary>
    private static bool IsAuthenticationAttempt(HttpContext context)
        => HttpMethods.IsPost(context.Request.Method)
           && context.Request.Path.StartsWithSegments("/Identity/Account");
}
