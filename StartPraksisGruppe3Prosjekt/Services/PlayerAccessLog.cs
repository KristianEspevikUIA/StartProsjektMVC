using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <inheritdoc cref="IPlayerAccessLog" />
public sealed class PlayerAccessLog : IPlayerAccessLog
{
    private readonly AppDbContext _db;
    private readonly ILogger<PlayerAccessLog> _logger;

    public PlayerAccessLog(AppDbContext db, ILogger<PlayerAccessLog> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        ClaimsPrincipal user,
        int playerId,
        string context,
        int? roundId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        // The player's own visits are not logged. The log is about other people looking at
        // them; a row every time a 14-year-old opens their own page is noise that makes the
        // rows that matter harder to find.
        var isSelf = await _db.Players
            .AsNoTracking()
            .AnyAsync(p => p.Id == playerId && p.UserId == userId, cancellationToken);

        if (isSelf)
        {
            return;
        }

        try
        {
            _db.PlayerAccessEvents.Add(new PlayerAccessEvent
            {
                PlayerId = playerId,
                ViewedByUserId = userId,
                ViewedByRole = RoleOf(user),
                Context = context,
                RoundId = roundId,
                OccurredAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A page that failed to log is a problem worth an alert, but taking the page
            // down in front of a coach does not fix it and loses the work they were doing.
            _logger.LogError(
                ex,
                "Failed to record a player access event: user {UserId} viewed player {PlayerId} from {Context}.",
                userId,
                playerId,
                context);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerAccessEvent>> GetForPlayerAsync(
        int playerId,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        await _db.PlayerAccessEvents
            .AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The role the lookup happened under. Stored as text rather than looked up later,
    /// because roles change and the log has to describe the moment it happened.
    /// Admin is reported first: an admin who is also a coach is looking with the wider hat on.
    /// </summary>
    private static string RoleOf(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin)) return Roles.Admin;
        if (user.IsInRole(Roles.Coach)) return Roles.Coach;
        if (user.IsInRole(Roles.Guardian)) return Roles.Guardian;
        if (user.IsInRole(Roles.Player)) return Roles.Player;

        return "Unknown";
    }
}
