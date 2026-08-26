using System.Security.Claims;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Records who opened an individual player's answers.
///
/// This is the counterweight to coaches no longer needing consent. Consent used to be what
/// stood between a coach and any given minor's answers; with that removed, the remaining
/// safeguard is that every lookup can be accounted for afterwards.
///
/// Call it from the action AFTER authorisation has succeeded, and only on pages that show
/// one player's answers. Logging a team list would drown the log in rows that are not about
/// anybody in particular.
/// </summary>
public interface IPlayerAccessLog
{
    /// <summary>
    /// Records one lookup. Never throws on the caller's behalf: a page that failed to log
    /// is a problem, but taking the page down in front of a coach does not fix it. Failures
    /// are logged as errors instead.
    /// </summary>
    /// <param name="user">Who is looking.</param>
    /// <param name="playerId">The player being looked at.</param>
    /// <param name="context">Which view, e.g. "Coach/FiveCPlayer".</param>
    /// <param name="roundId">The round on screen, when the page is about one.</param>
    Task RecordAsync(
        ClaimsPrincipal user,
        int playerId,
        string context,
        int? roundId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The log for one player, newest first. For the admin audit view and for a data
    /// subject access request -- a player is entitled to know who has looked at them.
    /// </summary>
    Task<IReadOnlyList<PlayerAccessEvent>> GetForPlayerAsync(
        int playerId,
        int take = 100,
        CancellationToken cancellationToken = default);
}
