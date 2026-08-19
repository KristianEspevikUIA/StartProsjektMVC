using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Avgjør om innlogget bruker kan se et aggregert lagsnitt.
///
///   Trener — må ha CoachTeam på laget.
///   Admin  — alltid.
///   Begge  — snittet må bygge på minst
///            <see cref="CanViewTeamAggregateRequirement.MinimumResponses"/> besvarelser.
///
/// Grensen gjelder også for admin. Admin har andre veier til enkeltdata der det er
/// nødvendig; et snitt som lekker enkeltpersoner er det ingen som trenger.
/// </summary>
public class CanViewTeamAggregateHandler
    : AuthorizationHandler<CanViewTeamAggregateRequirement, TeamAggregateResource>
{
    private readonly AppDbContext _db;

    public CanViewTeamAggregateHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanViewTeamAggregateRequirement requirement,
        TeamAggregateResource resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        // For få svar bak snittet: ikke vis det, uansett rolle.
        if (resource.ResponseCount < CanViewTeamAggregateRequirement.MinimumResponses)
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
                .AnyAsync(ct => ct.TeamId == resource.Team.Id && ct.CoachUserId == userId);

            if (coachesTeam)
            {
                context.Succeed(requirement);
            }
        }
    }
}
