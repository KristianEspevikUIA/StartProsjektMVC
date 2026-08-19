using Microsoft.AspNetCore.Authorization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Krav om at innlogget bruker kan se et aggregert lagsnitt.
/// </summary>
public class CanViewTeamAggregateRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Nedre grense for hvor mange besvarelser som må ligge bak et snitt før det vises.
    /// Med færre enn dette kan tallet regnes tilbake til enkeltpersoner — særlig når
    /// treneren vet hvem som har svart.
    /// </summary>
    public const int MinimumResponses = 3;
}

/// <summary>
/// Ressursen policyen <see cref="Policies.CanViewTeamAggregate"/> vurderer.
/// Antallet besvarelser er en del av ressursen med vilje: den som ber om et snitt må
/// oppgi hvor mange svar det bygger på, slik at grensen ikke kan hoppes over ved at
/// noen glemmer å sjekke den i en controller.
/// </summary>
/// <param name="Team">Laget snittet gjelder.</param>
/// <param name="ResponseCount">Antall besvarelser som inngår i snittet.</param>
public sealed record TeamAggregateResource(Team Team, int ResponseCount);
