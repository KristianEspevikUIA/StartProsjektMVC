using Microsoft.AspNetCore.Authorization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Avgjør om innlogget bruker kan se ett bestemt lag.
///
///   Admin  — alltid.
///   Trener — alltid. Trenerrollen er ikke knyttet til lag: en trener er trener, ikke
///            trener for et bestemt lag.
///   Andre  — nei. En foresatt eller spiller ser sitt eget via CanViewPlayer.
///
/// Merk at et lag i seg selv bare er et navn og en liste med spillerkoder. Selve svarene
/// til en enkeltspiller er fortsatt vernet av CanViewPlayer, som krever fullt samtykke.
///
/// Handleren slår ikke lenger opp i databasen, men er fortsatt registrert som scoped i
/// Program.cs sammen med de to andre. Det koster ingenting og holder oppsettet likt.
/// </summary>
public class CanViewTeamHandler : AuthorizationHandler<CanViewTeamRequirement, Team>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanViewTeamRequirement requirement,
        Team resource)
    {
        if (context.User.IsInRole(Roles.Admin) || context.User.IsInRole(Roles.Coach))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
