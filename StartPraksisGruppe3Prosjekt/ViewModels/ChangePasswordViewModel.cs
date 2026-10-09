using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>Account/ChangePassword: the signed-in user's own password, changed by themselves.</summary>
public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter the password you signed in with.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a new password.")]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Use at least 12 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type the new password once more.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The two new passwords are not the same.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password, once more")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    /// The account is on a temporary password and goes nowhere else until it is changed. Set by
    /// the controller from the signed-in user, never from the form.
    /// </summary>
    public bool IsRequired { get; set; }
}
