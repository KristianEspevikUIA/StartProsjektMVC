using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Avgjør om innlogget bruker kan se ett bestemt lag.
///
///   Admin  — alltid.
///   Trener — bare lag hen har CoachTeam på.
///   Andre  — nei. En foresatt eller spiller ser sitt eget via CanViewPlayer.
/// </summary>
public class CanViewTeamHandler : AuthorizationHandler<CanViewTeamRequirement, Team>
{
    private readonly AppDbContext _db;

    public CanViewTeamHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanViewTeamRequirement requirement,
        Team resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        if (context.User.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.IsInRole(Roles.Coach))
        {
            var coachesTeam = await _db.CoachTeams
                .AnyAsync(ct => ct.TeamId == resource.Id && ct.CoachUserId == userId);

            if (coachesTeam)
            {
                context.Succeed(requirement);
            }
        }
    }
}
