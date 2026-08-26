using Microsoft.AspNetCore.Authorization;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Avgjør om innlogget bruker kan se et aggregert lagsnitt.
///
///   Trener — alltid. Trenerrollen er ikke lagavgrenset.
///   Admin  — alltid.
///   Begge  — snittet må bygge på minst
///            <see cref="CanViewTeamAggregateRequirement.MinimumResponses"/> besvarelser.
///
/// Grensen gjelder også for admin. Admin har andre veier til enkeltdata der det er
/// nødvendig; et snitt som lekker enkeltpersoner er det ingen som trenger. Nå som
/// trenerrollen ikke er knyttet til lag, er antallskravet den eneste grensen som står
/// igjen her -- og derfor den som må holde.
/// </summary>
public class CanViewTeamAggregateHandler
    : AuthorizationHandler<CanViewTeamAggregateRequirement, TeamAggregateResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanViewTeamAggregateRequirement requirement,
        TeamAggregateResource resource)
    {
        // For få svar bak snittet: ikke vis det, uansett rolle.
        if (resource.ResponseCount < CanViewTeamAggregateRequirement.MinimumResponses)
        {
            return Task.CompletedTask;
        }

        if (context.User.IsInRole(Roles.Admin) || context.User.IsInRole(Roles.Coach))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
