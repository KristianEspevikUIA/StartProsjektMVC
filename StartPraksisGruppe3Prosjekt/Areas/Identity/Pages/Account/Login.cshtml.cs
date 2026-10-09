using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StartPraksisGruppe3Prosjekt.Areas.Identity.Pages.Account;

/// <summary>
/// Sign-in.
///
/// This page overrides the one in the Identity UI package. It exists for two reasons that
/// could not be fixed from the outside:
///
///   * The packaged page is a bare Bootstrap form that looks nothing like the rest of the
///     app, and its markup cannot be restyled far enough with CSS alone.
///   * It offers "Register as a new user", which in this app is a link to a 404 — accounts
///     are created by the club, and self-registration is closed in middleware.
///     See Security/ClosedRegistrationExtensions.cs.
///
/// External providers are left out entirely: none are configured, and the packaged page's
/// "no external services are configured" block is not something to show a coach.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<IdentityUser> signInManager,
        IHostEnvironment environment,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _environment = environment;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "That does not look like an email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Stay signed in on this device")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        // Clears any half-finished sign-in left over from an earlier attempt.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // The development password is in a public repository, so outside Development it opens
        // nothing -- whichever account still has it, because the database was copied from a
        // development one. Refused before the account is looked up, with the same answer as
        // any wrong password: it must not say which accounts have it.
        // See AccountRules.PublishedDevPassword.
        if (!_environment.IsDevelopment()
            && string.Equals(Input.Password, Security.AccountRules.PublishedDevPassword, StringComparison.Ordinal))
        {
            _logger.LogWarning("Sign-in refused: the published development password was tried.");
            ModelState.AddModelError(string.Empty, "That email address and password do not match an account.");
            return Page();
        }

        // lockoutOnFailure: true. Accounts lock after five failed attempts for fifteen
        // minutes -- configured in Program.cs. That protects one account; the rate limit on
        // /Identity/Account/* is what stops one password being tried against a hundred.
        var result = await _signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User signed in.");
            return LocalRedirect(returnUrl);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Account locked out.");
            return RedirectToPage("./Lockout");
        }

        // Deliberately vague: saying whether it was the address or the password that was
        // wrong tells anyone who asks which accounts exist.
        ModelState.AddModelError(string.Empty, "That email address and password do not match an account.");
        return Page();
    }
}
