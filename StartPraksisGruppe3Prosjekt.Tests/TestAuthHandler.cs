using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Signs a request in as whoever the test says, without a password.
///
/// The point is to be able to ask "what does a guardian see on this URL" as a question the
/// build can answer. Doing that through the real sign-in form would mean handling passwords
/// in test code and would test Identity rather than our own rules.
///
/// The identity comes from two headers the test client sets. Nothing outside the test host
/// ever registers this handler, so there is no path from a real request to it.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    /// <summary>Identity user id to sign in as. No header means an anonymous request.</summary>
    public const string UserIdHeader = "X-Test-UserId";

    /// <summary>Comma-separated roles, e.g. "Coach" or "Coach,Admin".</summary>
    public const string RolesHeader = "X-Test-Roles";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId)
            || string.IsNullOrWhiteSpace(userId))
        {
            // No header: genuinely anonymous, so the fallback policy can be tested too.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userId.ToString())
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
