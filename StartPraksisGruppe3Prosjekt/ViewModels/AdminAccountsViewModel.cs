using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Authorization;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// One account, as the administrator sees it: who it is, what it may do, and whether it can
/// sign in at all. Never the password or its hash -- only whether there is one.
/// </summary>
public sealed class AdminAccountRow
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public bool HasPassword { get; init; }

    public bool IsLocked { get; init; }

    /// <summary>On a temporary password: the owner has not chosen their own yet.</summary>
    public bool MustChangePassword { get; init; }

    public bool IsCoach => Roles.Contains(Authorization.Roles.Coach);

    public bool CanSignIn => HasPassword && !IsLocked;

    /// <summary>
    /// One phrase for the five states an account can be in. "No access yet" is an account that
    /// was made for somebody -- a coach's, from the workbook import -- and never opened.
    /// </summary>
    public string StatusText => (IsLocked, HasPassword, MustChangePassword) switch
    {
        (true, false, _) => "No access yet",
        (true, true, _) => "Locked",
        (false, false, _) => "No password",
        (false, true, true) => "Temporary password",
        _ => "Can sign in"
    };

    public string StatusClass => (IsLocked, HasPassword, MustChangePassword) switch
    {
        (true, true, _) => "sc-badge sc-badge--alert",
        (false, true, true) => "sc-badge sc-badge--warn",
        (false, true, false) => "sc-badge sc-badge--ok",
        _ => "sc-badge sc-badge--muted"
    };
}

/// <summary>Admin/Users: the accounts, and the form for a new one.</summary>
public sealed class AdminAccountsViewModel
{
    public const string Staff = "staff";
    public const string Players = "players";
    public const string Guardians = "guardians";

    /// <summary><see cref="Staff"/>, <see cref="Players"/> or <see cref="Guardians"/>.</summary>
    public string Show { get; init; } = Staff;

    public IReadOnlyList<AdminAccountRow> Accounts { get; init; } = Array.Empty<AdminAccountRow>();

    public int StaffCount { get; init; }

    public int PlayerCount { get; init; }

    public int GuardianCount { get; init; }

    public NewAccountForm New { get; init; } = new();
}

/// <summary>A new staff account. Players come with the squads, not from this form.</summary>
public sealed class NewAccountForm
{
    [Required(ErrorMessage = "Enter the email address the person will sign in with.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    /// <summary><see cref="Roles.Coach"/> or <see cref="Roles.Admin"/>.</summary>
    [Required]
    [Display(Name = "Role")]
    public string Role { get; set; } = Roles.Coach;
}

/// <summary>Admin/Account/{id}: one account and its access.</summary>
public sealed class AdminAccountViewModel
{
    public required AdminAccountRow Account { get; init; }

    /// <summary>The administrator's own account: it cannot be locked or reset from here.</summary>
    public bool IsSelf { get; init; }
}

/// <summary>
/// The temporary password, on the one page that ever shows it. It is not stored anywhere
/// readable and cannot be shown again: lose it, and the administrator makes a new one.
/// </summary>
public sealed record AdminAccountPasswordViewModel(string AccountId, string Email, string TemporaryPassword);
