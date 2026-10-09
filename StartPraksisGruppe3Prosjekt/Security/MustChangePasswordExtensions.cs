namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// En konto med et midlertidig passord kommer ingen steder før passordet er byttet.
///
/// Administratoren som ga ut passordet, har sett det. Så lenge det virker, er det to som kan
/// logge inn som treneren -- og revisjonsloggen kan ikke si hvem av dem som åpnet en spiller.
/// Derfor sendes hver forespørsel fra en slik konto til Account/ChangePassword, og bare dit og
/// til utlogging, til claimet er borte.
///
/// I middleware og ikke som et filter, av samme grunn som den stengte registreringen: Identity
/// UI-sidene er Razor Pages i en pakke, og et MVC-filter ser dem ikke.
/// </summary>
public static class MustChangePasswordExtensions
{
    public const string ChangePasswordPath = "/Account/ChangePassword";

    private static readonly string[] AllowedPaths =
    {
        ChangePasswordPath,
        "/Identity/Account/Logout",

        // Statuskodesidene kjøres om igjen gjennom pipelinen. Uten dem ville en 404 fra en slik
        // konto blitt en omdirigering i stedet for en side.
        "/Home/Status",
        "/Home/Error"
    };

    /// <summary>Etter UseAuthentication: den leser claimet fra den innloggede brukeren.</summary>
    public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && context.User.HasClaim(AccountRules.MustChangePasswordClaim, bool.TrueString)
                && !AllowedPaths.Any(allowed =>
                    context.Request.Path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Redirect(context.Request.PathBase + ChangePasswordPath);
                return;
            }

            await next();
        });
}
