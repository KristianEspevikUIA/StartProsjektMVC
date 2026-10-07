using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Security;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// The accounts, as an administrator manages them: who has one, creating one, handing out a
/// new temporary password, and locking one.
///
/// Self-registration is closed and the application sends no e-mail, so this is the only way
/// anybody but the first administrator (create-admin) gets in. No authorisation here: the
/// controller decides who may ask, and that is administrators only.
///
/// WHAT AN ACCOUNT IS TIED TO
///   * A coach or an administrator: nothing. The role is the whole of it.
///   * A player: exactly one player row (players.user_id). The account is what lets that
///     player see their own answers and nobody else's.
///   * A guardian: one or more players (guardianships). A guardian of nobody sees nothing.
///
/// THREE RULES HELD HERE, so they hold whichever page calls:
///   * A player under <see cref="PlayerRules.GuardianRequiredBelowAge"/> gets an account only
///     when a guardian is registered for them, and does not lose their last guardian while
///     they have one. The guardian comes first.
///   * Nobody locks their own account, and the last administrator who can sign in is never
///     locked. Either would leave the club outside its own system.
///   * Passwords are generated, shown once and never stored or logged. See
///     <see cref="TemporaryPassword"/>.
///
/// Accounts are locked, not deleted. The logs name people by account id -- who looked at a
/// player, who changed a consent -- and an id that no longer resolves to anybody is a log
/// that no longer says who. A player's account goes when the player is deleted
/// (AdminController.Delete), and that is the one place it does.
/// </summary>
public interface IAccountAdministration
{
    Task<IReadOnlyList<AccountRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One account with what it is tied to. Null when there is no such account.</summary>
    Task<AccountDetails?> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Every player, for the lists a new account is tied to.</summary>
    Task<IReadOnlyList<PlayerChoice>> PlayersAsync(CancellationToken cancellationToken = default);

    Task<AccountResult> CreateAsync(NewAccount request, string administratorUserId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the password with a new temporary one, which has to be changed at the next sign-in.</summary>
    Task<AccountResult> IssueTemporaryPasswordAsync(string userId, string administratorUserId, CancellationToken cancellationToken = default);

    Task<AccountResult> LockAsync(string userId, string administratorUserId, CancellationToken cancellationToken = default);

    Task<AccountResult> UnlockAsync(string userId, string administratorUserId, CancellationToken cancellationToken = default);

    Task<AccountResult> AddGuardianLinkAsync(string userId, int playerId, string administratorUserId, CancellationToken cancellationToken = default);

    Task<AccountResult> RemoveGuardianLinkAsync(string userId, int playerId, string administratorUserId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAccountAdministration" />
public sealed class AccountAdministration : IAccountAdministration
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly ILogger<AccountAdministration> _logger;

    public AccountAdministration(
        AppDbContext db,
        UserManager<IdentityUser> users,
        ILogger<AccountAdministration> logger)
    {
        _db = db;
        _users = users;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.LockoutEnd })
            .ToListAsync(cancellationToken);

        var roles = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var players = await _db.Players
            .AsNoTracking()
            .Where(p => p.UserId != null)
            .Select(p => new { p.UserId, p.Name })
            .ToListAsync(cancellationToken);

        var children = await _db.Guardianships
            .AsNoTracking()
            .Select(g => new { g.GuardianUserId, g.Player!.Name })
            .ToListAsync(cancellationToken);

        var temporary = (await _db.UserClaims
                .AsNoTracking()
                .Where(c => c.ClaimType == TemporaryPassword.ClaimType)
                .Select(c => c.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var rolesByUser = roles.ToLookup(r => r.UserId, r => r.Name!);
        var playerByUser = players.ToLookup(p => p.UserId!, p => p.Name);
        var childrenByUser = children.ToLookup(c => c.GuardianUserId, c => c.Name);
        var now = DateTimeOffset.UtcNow;

        return users
            .Select(u => new AccountRow(
                u.Id,
                u.Email ?? string.Empty,
                rolesByUser[u.Id].OrderBy(r => r, StringComparer.Ordinal).ToList(),
                playerByUser[u.Id].FirstOrDefault(),
                childrenByUser[u.Id].OrderBy(n => n, StringComparer.Ordinal).ToList(),
                StateOf(u.LockoutEnd, temporary.Contains(u.Id), now)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AccountDetails?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var roles = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                orderby role.Name
                select role.Name!)
            .ToListAsync(cancellationToken);

        var player = await _db.Players
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new PlayerChoice(p.Id, p.Name, p.Team!.Name, true, 0))
            .FirstOrDefaultAsync(cancellationToken);

        var children = await _db.Guardianships
            .AsNoTracking()
            .Where(g => g.GuardianUserId == userId)
            .OrderBy(g => g.Player!.Name)
            .Select(g => new PlayerChoice(g.PlayerId, g.Player!.Name, g.Player.Team!.Name, g.Player.UserId != null, 0))
            .ToListAsync(cancellationToken);

        var temporary = await _db.UserClaims
            .AsNoTracking()
            .AnyAsync(c => c.UserId == userId && c.ClaimType == TemporaryPassword.ClaimType, cancellationToken);

        return new AccountDetails(
            user.Id,
            user.Email ?? string.Empty,
            roles,
            player,
            children,
            StateOf(user.LockoutEnd, temporary, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerChoice>> PlayersAsync(CancellationToken cancellationToken = default) =>
        await _db.Players
            .AsNoTracking()
            .OrderBy(p => p.Team!.Name)
            .ThenBy(p => p.Name)
            .Select(p => new PlayerChoice(
                p.Id,
                p.Name,
                p.Team!.Name,
                p.UserId != null,
                p.Guardianships.Count))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AccountResult> CreateAsync(
        NewAccount request,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim() ?? string.Empty;

        if (email.Length == 0 || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            return AccountResult.Refused("Enter the e-mail address the person signs in with.");
        }

        if (!Roles.All.Contains(request.Role))
        {
            return AccountResult.Refused("Choose a role.");
        }

        if (await _users.FindByEmailAsync(email) is not null)
        {
            return AccountResult.Refused("There is already an account with that e-mail address.");
        }

        Player? player = null;

        if (request.Role is Roles.Player or Roles.Guardian)
        {
            player = request.PlayerId is { } playerId
                ? await _db.Players.Include(p => p.Guardianships).FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
                : null;

            if (player is null)
            {
                return AccountResult.Refused(request.Role == Roles.Player
                    ? "Choose the player the account belongs to."
                    : "Choose the player this person is a guardian of.");
            }
        }

        if (request.Role == Roles.Player)
        {
            if (player!.UserId is not null)
            {
                return AccountResult.Refused($"{player.Name} already has an account.");
            }

            if (NeedsGuardian(player) && player.Guardianships.Count == 0)
            {
                return AccountResult.Refused(
                    $"{player.Name} is under {PlayerRules.GuardianRequiredBelowAge} and has no guardian registered. " +
                    "Create the guardian's account first, then the player's.");
            }
        }

        var password = TemporaryPassword.Generate();

        // The account, its role, the flag and what it is tied to: all of it or none of it. An
        // account left behind without its player would block the address for the next attempt.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,

            // No e-mail is sent, so there is no confirmation to wait for.
            EmailConfirmed = true
        };

        var steps = new Func<Task<IdentityResult>>[]
        {
            () => _users.CreateAsync(user, password),
            () => _users.AddToRoleAsync(user, request.Role),
            () => _users.AddClaimAsync(user, new Claim(TemporaryPassword.ClaimType, TemporaryPassword.ClaimValue))
        };

        foreach (var step in steps)
        {
            var result = await step();

            if (!result.Succeeded)
            {
                return AccountResult.Refused(result.Errors.Select(e => e.Description).ToArray());
            }
        }

        if (request.Role == Roles.Player)
        {
            player!.UserId = user.Id;
        }
        else if (request.Role == Roles.Guardian)
        {
            _db.Guardianships.Add(new Guardianship { PlayerId = player!.Id, GuardianUserId = user.Id });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Ids, not addresses: the log is read by more people than the account list is.
        _logger.LogInformation(
            "Account {UserId} created with the role {Role} by administrator {AdministratorId}.",
            user.Id,
            request.Role,
            administratorUserId);

        return AccountResult.WithPassword(user.Id, email, password);
    }

    /// <inheritdoc />
    public async Task<AccountResult> IssueTemporaryPasswordAsync(
        string userId,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (await _users.FindByIdAsync(userId) is not { } user)
        {
            return AccountResult.Refused("There is no such account.");
        }

        if (string.Equals(userId, administratorUserId, StringComparison.Ordinal))
        {
            return AccountResult.Refused("Change your own password under My account.");
        }

        var password = TemporaryPassword.Generate();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Through the reset token rather than remove-and-add: one step, and it also changes
        // the security stamp, which signs the account out everywhere it is signed in.
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, password);

        if (!reset.Succeeded)
        {
            return AccountResult.Refused(reset.Errors.Select(e => e.Description).ToArray());
        }

        if (!(await _users.GetClaimsAsync(user)).Any(c => c.Type == TemporaryPassword.ClaimType))
        {
            await _users.AddClaimAsync(user, new Claim(TemporaryPassword.ClaimType, TemporaryPassword.ClaimValue));
        }

        // Five wrong guesses lock an account for a while. A new password is the answer to
        // that as well; a lock an administrator set is left alone.
        if (user.LockoutEnd is { } lockedUntil && !IsAdministratorLock(lockedUntil))
        {
            await _users.SetLockoutEndDateAsync(user, null);
        }

        await _users.ResetAccessFailedCountAsync(user);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Account {UserId} was given a new temporary password by administrator {AdministratorId}.",
            user.Id,
            administratorUserId);

        return AccountResult.WithPassword(user.Id, user.Email ?? string.Empty, password);
    }

    /// <inheritdoc />
    public async Task<AccountResult> LockAsync(
        string userId,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (await _users.FindByIdAsync(userId) is not { } user)
        {
            return AccountResult.Refused("There is no such account.");
        }

        if (string.Equals(userId, administratorUserId, StringComparison.Ordinal))
        {
            return AccountResult.Refused("You cannot lock the account you are signed in with.");
        }

        if (await _users.IsInRoleAsync(user, Roles.Admin) && await ActiveAdministratorsAsync(except: userId) == 0)
        {
            return AccountResult.Refused("This is the only administrator who can sign in. Create another one first.");
        }

        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        // Without this the account stays signed in where it already is until the cookie is
        // checked again; with it, that check fails.
        await _users.UpdateSecurityStampAsync(user);

        _logger.LogInformation(
            "Account {UserId} locked by administrator {AdministratorId}.",
            user.Id,
            administratorUserId);

        return AccountResult.Done(user.Id, user.Email ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<AccountResult> UnlockAsync(
        string userId,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (await _users.FindByIdAsync(userId) is not { } user)
        {
            return AccountResult.Refused("There is no such account.");
        }

        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);

        _logger.LogInformation(
            "Account {UserId} unlocked by administrator {AdministratorId}.",
            user.Id,
            administratorUserId);

        return AccountResult.Done(user.Id, user.Email ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<AccountResult> AddGuardianLinkAsync(
        string userId,
        int playerId,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (await _users.FindByIdAsync(userId) is not { } user || !await _users.IsInRoleAsync(user, Roles.Guardian))
        {
            return AccountResult.Refused("Only a guardian's account can be tied to a player this way.");
        }

        if (!await _db.Players.AnyAsync(p => p.Id == playerId, cancellationToken))
        {
            return AccountResult.Refused("Choose a player.");
        }

        if (await _db.Guardianships.AnyAsync(g => g.GuardianUserId == userId && g.PlayerId == playerId, cancellationToken))
        {
            return AccountResult.Refused("This account is already a guardian of that player.");
        }

        _db.Guardianships.Add(new Guardianship { PlayerId = playerId, GuardianUserId = userId });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Account {UserId} made guardian of player {PlayerId} by administrator {AdministratorId}.",
            userId,
            playerId,
            administratorUserId);

        return AccountResult.Done(user.Id, user.Email ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<AccountResult> RemoveGuardianLinkAsync(
        string userId,
        int playerId,
        string administratorUserId,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.Guardianships
            .Include(g => g.Player)
            .FirstOrDefaultAsync(g => g.GuardianUserId == userId && g.PlayerId == playerId, cancellationToken);

        if (link is null)
        {
            return AccountResult.Refused("This account is not a guardian of that player.");
        }

        var player = link.Player!;

        // The same rule as when the player's account was created, from the other side.
        if (player.UserId is not null
            && NeedsGuardian(player)
            && await _db.Guardianships.CountAsync(g => g.PlayerId == playerId, cancellationToken) == 1)
        {
            return AccountResult.Refused(
                $"{player.Name} is under {PlayerRules.GuardianRequiredBelowAge}, has an account, and this is their only guardian. " +
                "Register another guardian first.");
        }

        _db.Guardianships.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Account {UserId} is no longer guardian of player {PlayerId}, by administrator {AdministratorId}.",
            userId,
            playerId,
            administratorUserId);

        return AccountResult.Done(userId, string.Empty);
    }

    private static bool NeedsGuardian(Player player) =>
        player.AgeAt(DateOnly.FromDateTime(DateTime.UtcNow)) < PlayerRules.GuardianRequiredBelowAge;

    /// <summary>Administrators who are not locked, leaving one account out of the count.</summary>
    private async Task<int> ActiveAdministratorsAsync(string except)
    {
        var now = DateTimeOffset.UtcNow;

        return (await _users.GetUsersInRoleAsync(Roles.Admin))
            .Count(u => u.Id != except && !(u.LockoutEnd is { } until && until > now));
    }

    /// <summary>
    /// A lock an administrator set runs to the end of time; one from wrong guesses runs for
    /// minutes. That is the only thing that tells them apart, and it is enough.
    /// </summary>
    private static bool IsAdministratorLock(DateTimeOffset lockedUntil) => lockedUntil.Year >= 9000;

    private static AccountState StateOf(DateTimeOffset? lockoutEnd, bool temporaryPassword, DateTimeOffset now)
    {
        if (lockoutEnd is { } until && until > now)
        {
            return IsAdministratorLock(until) ? AccountState.Locked : AccountState.TooManyAttempts;
        }

        return temporaryPassword ? AccountState.TemporaryPassword : AccountState.Active;
    }
}

public enum AccountState
{
    Active,

    /// <summary>Created or reset, and the person has not yet signed in and chosen their own password.</summary>
    TemporaryPassword,

    /// <summary>Locked for a few minutes after too many wrong passwords. Clears by itself.</summary>
    TooManyAttempts,

    /// <summary>Locked by an administrator, until an administrator unlocks it.</summary>
    Locked
}

/// <param name="PlayerName">For a player's account: the player it belongs to.</param>
/// <param name="GuardianOf">For a guardian's account: the players it is tied to.</param>
public sealed record AccountRow(
    string UserId,
    string Email,
    IReadOnlyList<string> Roles,
    string? PlayerName,
    IReadOnlyList<string> GuardianOf,
    AccountState State);

public sealed record AccountDetails(
    string UserId,
    string Email,
    IReadOnlyList<string> Roles,
    PlayerChoice? Player,
    IReadOnlyList<PlayerChoice> GuardianOf,
    AccountState State);

/// <param name="GuardianCount">How many guardians are registered for the player. Only filled in by PlayersAsync.</param>
public sealed record PlayerChoice(int PlayerId, string Name, string TeamName, bool HasAccount, int GuardianCount);

/// <param name="PlayerId">The player a player's account belongs to, or the first player a guardian is tied to.</param>
public sealed record NewAccount(string? Email, string Role, int? PlayerId);

/// <summary>
/// What an administrative action came to. <see cref="TemporaryPassword"/> is set once, here,
/// when an account was created or reset: it is shown to the administrator and exists nowhere
/// else afterwards.
/// </summary>
public sealed record AccountResult(
    bool Succeeded,
    IReadOnlyList<string> Problems,
    string? UserId,
    string? Email,
    string? TemporaryPassword)
{
    public static AccountResult Refused(params string[] problems) => new(false, problems, null, null, null);

    public static AccountResult Done(string userId, string email) => new(true, Array.Empty<string>(), userId, email, null);

    public static AccountResult WithPassword(string userId, string email, string password) =>
        new(true, Array.Empty<string>(), userId, email, password);
}
