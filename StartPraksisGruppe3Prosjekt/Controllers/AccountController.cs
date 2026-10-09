using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Owner: Kristian.
///
/// The signed-in user's own account. One thing so far: changing the password.
///
/// It is here, and not only on the Identity package's own Manage pages, because of the
/// temporary password. An administrator gives one out (AdminController.AccountPassword), the
/// account is marked with <see cref="AccountRules.MustChangePasswordClaim"/>, and every request
/// is sent to this page until the owner has chosen a password nobody else has seen
/// (<see cref="MustChangePasswordExtensions"/>). The package's page cannot take that mark off.
/// </summary>
[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    private bool MustChange => User.HasClaim(AccountRules.MustChangePasswordClaim, bool.TrueString);

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel { IsRequired = MustChange });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel form)
    {
        form.IsRequired = MustChange;

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (string.Equals(form.NewPassword, form.CurrentPassword, StringComparison.Ordinal))
        {
            // The whole point of the change is a password the administrator has not seen.
            ModelState.AddModelError(nameof(form.NewPassword), "Choose a password that is not the one you have now.");
            return View(form);
        }

        var result = await _userManager.ChangePasswordAsync(user, form.CurrentPassword, form.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                if (error.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                {
                    ModelState.AddModelError(nameof(form.CurrentPassword), "That is not the password you signed in with.");
                }
                else
                {
                    ModelState.AddModelError(nameof(form.NewPassword), error.Description);
                }
            }

            return View(form);
        }

        foreach (var claim in (await _userManager.GetClaimsAsync(user))
                 .Where(c => c.Type == AccountRules.MustChangePasswordClaim))
        {
            await _userManager.RemoveClaimAsync(user, claim);
        }

        // A new cookie: the old one still carries the claim, and its security stamp is the
        // one the password change just replaced.
        await _signInManager.RefreshSignInAsync(user);

        _logger.LogInformation("A user changed their own password.");

        TempData["AccountMessage"] = "Your password is changed.";

        return RedirectToAction(nameof(ChangePassword));
    }
}
