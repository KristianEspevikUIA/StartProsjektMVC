using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;


namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Decides whether the signed-in user may see information about one particular player.
///
/// The rules, in the order they are evaluated:
///   Admin     -- always.
///   Player    -- their own record (player.UserId == the signed-in user).
///   Guardian  -- only where a Guardianship links this user to THIS player. The role
///                "Guardian" grants nothing on its own.
///   Coach     -- always. Not scoped to a team, and no longer scoped by consent.
///   Anyone else -- no.
///
/// CONSENT NO LONGER GATES COACHES. It used to: a coach needed ConsentLevel.Full before
/// they could see an individual player at all. The club asked for coaches to always be
/// able to open a player, and that is what this now does.
///
/// What replaces it is accountability rather than prevention: every coach or admin lookup
/// of an individual player's answers is written to PlayerAccessEvent, an append-only log.
/// See IPlayerAccessLog, and the note in docs/five-c.md. Consent still governs what may be
/// done with the data outside the app, and it still has to be right in the Sikt filing --
/// it just is not what stops a coach opening a page any more.
///
/// The handler never calls context.Fail(): it simply does not succeed. Fail() would block
/// other handlers for the same requirement and make the policy hard to extend.
/// </summary>
public class CanViewPlayerHandler : AuthorizationHandler<CanViewPlayerRequirement, Player>
{
    private readonly AppDbContext _db;

    public CanViewPlayerHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanViewPlayerRequirement requirement,
        Player resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        // Admin ser alt. Merk: admin-tilgang skal logges når revisjonsloggen kommer på plass.
        if (context.User.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        // Spilleren selv.
        if (resource.UserId is not null && resource.UserId == userId)
        {
            context.Succeed(requirement);
            return;
        }

        // Foresatt: må være registrert foresatt for nettopp denne spilleren.
        if (context.User.IsInRole(Roles.Guardian))
        {
            var isGuardian = await _db.Guardianships
                .AnyAsync(g => g.PlayerId == resource.Id && g.GuardianUserId == userId);

            if (isGuardian)
            {
                context.Succeed(requirement);
                return;
            }
        }

        // Coach: always. Not scoped to a team, and no longer scoped by consent.
        //
        // Nothing here prevents a coach from opening any player in the club. What stands in
        // for the old consent check is PlayerAccessEvent: the actions that show one player's
        // answers write a row saying who looked, at whom, from where and when. That log is
        // the safeguard now, so if it stops being written this rule is unguarded -- see
        // IPlayerAccessLog and docs/five-c.md.
        if (context.User.IsInRole(Roles.Coach))
        {
            context.Succeed(requirement);
            return;
        }

        // Ingen treff: ingen tilgang.
    }
}
