namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Stenger selvregistrering. Identity UI legger ut /Identity/Account/Register som
/// åpen side; her skal kontoer opprettes av klubben, ikke av den som finner URL-en.
/// En åpen registrering på et system med opplysninger om mindreårige er et hull
/// uansett hvor god autorisasjonen bak er.
///
/// Sidene blokkeres i middleware og ikke med en policy, fordi Identity UI-sidene har
/// [AllowAnonymous] i selve pakken — og AllowAnonymous slår enhver policy vi legger
/// på utenfra. 404 er med vilje: en side som er stengt trenger ikke å bekrefte at
/// den finnes.
///
/// Sidene for glemt passord er stengt av en annen grunn: de sender en lenke på e-post, og
/// appen sender ikke e-post. Pakkens sider svarer likevel «sjekk innboksen», og det er verre
/// enn ingen side. Et glemt passord løses av en administrator, som gir kontoen et nytt
/// midlertidig -- se AdminController.IssuePassword.
/// </summary>
public static class ClosedRegistrationExtensions
{
    private static readonly string[] BlockedPaths =
    {
        "/Identity/Account/Register",
        "/Identity/Account/RegisterConfirmation",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/ExternalLogin",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ForgotPasswordConfirmation",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResetPasswordConfirmation"
    };

    public static IApplicationBuilder UseClosedSelfRegistration(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            foreach (var blocked in BlockedPaths)
            {
                if (context.Request.Path.StartsWithSegments(blocked, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            await next();
        });
}
