using System.Security.Cryptography;

namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// The password an administrator hands over when an account is created or reset.
///
/// The application sends no e-mail, so there is no link to click. Instead the administrator
/// is shown a password once, passes it on, and the person has to replace it with their own
/// the first time they sign in. Until they have, the account carries the claim
/// <see cref="ClaimType"/>, and <see cref="TemporaryPasswordGate"/> lets them reach nothing
/// but the page where the password is changed.
///
/// The password is generated here and never chosen by the administrator: a password a human
/// made up for somebody else is a password that gets reused for the next account.
/// </summary>
public static class TemporaryPassword
{
    /// <summary>On an account whose password is still the one an administrator was shown.</summary>
    public const string ClaimType = "startcompass:must_change_password";

    public const string ClaimValue = "true";

    // Without the look-alikes (0 and O, 1 and l and I): the password is read off a screen
    // and typed in by somebody else.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string All = Upper + Lower + Digits;

    private const int Groups = 4;
    private const int GroupLength = 5;

    /// <summary>
    /// Four groups of five, e.g. "Kf7qZ-p3mRt-8xWn2-cHd5v". Twenty random characters from 56,
    /// which is far more than a password that lives for one sign-in needs; the hyphens are the
    /// non-alphanumeric character the password rules ask for, and make it readable aloud.
    /// </summary>
    public static string Generate()
    {
        while (true)
        {
            var characters = new char[Groups * GroupLength];

            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = All[RandomNumberGenerator.GetInt32(All.Length)];
            }

            // The rules in Program.cs want an upper-case letter, a lower-case letter and a
            // digit. Almost every draw has all three; one that does not is drawn again.
            if (!characters.Any(Upper.Contains) || !characters.Any(Lower.Contains) || !characters.Any(Digits.Contains))
            {
                continue;
            }

            return string.Join('-', characters.Chunk(GroupLength).Select(group => new string(group)));
        }
    }
}

/// <summary>
/// Holds an account with a temporary password on the page where it is changed.
///
/// Runs after authentication and before authorization, so it applies whatever the account's
/// role is and whatever the page asks for. Signing out stays possible, and so do the error
/// pages -- a redirect loop is not a way to ask for a new password.
/// </summary>
public static class TemporaryPasswordGate
{
    public const string ChangePasswordPath = "/Identity/Account/Manage/ChangePassword";

    private static readonly string[] AllowedPaths =
    {
        ChangePasswordPath,
        "/Identity/Account/Logout",
        "/Home/Status",
        "/Home/Error"
    };

    public static IApplicationBuilder UseTemporaryPasswordGate(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && context.User.HasClaim(TemporaryPassword.ClaimType, TemporaryPassword.ClaimValue)
                && !AllowedPaths.Any(path => context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Redirect(ChangePasswordPath);
                return;
            }

            await next();
        });
}
