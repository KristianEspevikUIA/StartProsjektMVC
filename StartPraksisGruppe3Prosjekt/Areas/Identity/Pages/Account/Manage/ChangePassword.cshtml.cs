using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using StartPraksisGruppe3Prosjekt.Security;

namespace StartPraksisGruppe3Prosjekt.Areas.Identity.Pages.Account.Manage;

/// <summary>
/// Changing one's own password.
///
/// This page overrides the one in the Identity UI package, for one reason the packaged page
/// could not be made to serve: an account that was created or reset by an administrator has
/// a TEMPORARY password, and this is where it stops being temporary. When the change goes
/// through, the claim that holds the account on this page (see
/// <see cref="TemporaryPassword"/> and <see cref="TemporaryPasswordGate"/>) is removed and the
/// sign-in is refreshed, so the next request is let through.
///
/// Two things the packaged page does not do:
///   * The new password has to differ from the current one. "Changing" a temporary password
///     to itself would leave the account on a password an administrator has seen.
///   * It says why the person is here when they did not ask to be.
///
/// The password rules are the ones in Program.cs; Identity applies them and words the errors.
///
/// Rate limited as a sensitive action rather than as a sign-in attempt: the person is already
/// signed in, but the form does check a password, and should not be a place to guess one.
/// </summary>
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Sensitive)]
public class ChangePasswordModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<ChangePasswordModel> _logger;

    public ChangePasswordModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ILogger<ChangePasswordModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>The account is here because its password is temporary, not because it chose to be.</summary>
    public bool IsTemporary { get; private set; }

    public int MinimumLength { get; private set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter the password you signed in with.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string OldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a new password.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "New password, once more")]
        [Compare(nameof(NewPassword), ErrorMessage = "The two new passwords are not the same.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await _userManager.GetUserAsync(User) is null)
        {
            return NotFound();
        }

        Describe();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Describe();

        if (await _userManager.GetUserAsync(User) is not { } user)
        {
            return NotFound();
        }

        if (ModelState.IsValid && string.Equals(Input.OldPassword, Input.NewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError("Input.NewPassword", "The new password has to be different from the current one.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var changed = await _userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);

        if (!changed.Succeeded)
        {
            foreach (var error in changed.Errors)
            {
                // "Incorrect password." belongs to the current password; the rest are about the new one.
                ModelState.AddModelError(
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "Input.OldPassword" : "Input.NewPassword",
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "That is not your current password." : error.Description);
            }

            return Page();
        }

        var wasTemporary = false;

        foreach (var claim in (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == TemporaryPassword.ClaimType))
        {
            await _userManager.RemoveClaimAsync(user, claim);
            wasTemporary = true;
        }

        // The cookie carries the claim that held the account here, and the old security stamp.
        // Both are replaced by signing in again with what the account is now.
        await _signInManager.RefreshSignInAsync(user);

        _logger.LogInformation(
            "Account {UserId} changed its password{Temporary}.",
            user.Id,
            wasTemporary ? ", replacing a temporary one" : string.Empty);

        if (wasTemporary)
        {
            // Nothing else was reachable until now, so there is nowhere to go back to.
            return LocalRedirect("~/");
        }

        StatusMessage = "Your password is changed.";
        return RedirectToPage();
    }

    private void Describe()
    {
        IsTemporary = User.HasClaim(TemporaryPassword.ClaimType, TemporaryPassword.ClaimValue);
        MinimumLength = _userManager.Options.Password.RequiredLength;
    }
}
