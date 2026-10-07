using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The accounts page: every account, and the three forms that create a new one.
///
/// Three forms rather than one with fields that appear and disappear. What an account is tied
/// to depends on its role -- nothing, one player, or a player it is a guardian of -- and a
/// form per case says so without a line of script.
/// </summary>
public sealed class AdminUsersViewModel
{
    public IReadOnlyList<AccountRow> Accounts { get; init; } = Array.Empty<AccountRow>();

    /// <summary>Every player, for the two lists. A player's own account is offered only where there is none.</summary>
    public IReadOnlyList<PlayerChoice> Players { get; init; } = Array.Empty<PlayerChoice>();

    /// <summary>What was typed, when a form comes back with a problem.</summary>
    public NewAccount? Attempt { get; init; }

    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    public IEnumerable<PlayerChoice> PlayersWithoutAccount => Players.Where(p => !p.HasAccount);

    public string? EmailFor(string role) => Attempt?.Role == role ? Attempt.Email : null;
}

/// <summary>One account: what it is, what it is tied to, and what an administrator can do with it.</summary>
public sealed class AdminAccountViewModel
{
    public required AccountDetails Account { get; init; }

    /// <summary>Players a guardian is not yet tied to. Empty for other roles.</summary>
    public IReadOnlyList<PlayerChoice> OtherPlayers { get; init; } = Array.Empty<PlayerChoice>();

    /// <summary>The administrator looking at the page is looking at their own account.</summary>
    public bool IsOwnAccount { get; init; }

    public bool IsGuardian => Account.Roles.Contains(Authorization.Roles.Guardian);

    public static string Describe(AccountState state) => state switch
    {
        AccountState.Active => "Active",
        AccountState.TemporaryPassword => "Temporary password",
        AccountState.TooManyAttempts => "Too many attempts",
        AccountState.Locked => "Locked",
        _ => state.ToString()
    };

    public static string BadgeFor(AccountState state) => state switch
    {
        AccountState.Active => "sc-badge--ok",
        AccountState.Locked => "sc-badge--alert",
        _ => "sc-badge--warn"
    };
}

/// <summary>
/// The page that shows a temporary password, once. It is rendered straight from the POST that
/// made the password and is never redirected to: a redirect would have to carry the password
/// somewhere in between, and there is nowhere it should be kept.
/// </summary>
public sealed class TemporaryPasswordViewModel
{
    public required string UserId { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    /// <summary>True for a new account, false for a new password on an existing one.</summary>
    public bool IsNewAccount { get; init; }
}
