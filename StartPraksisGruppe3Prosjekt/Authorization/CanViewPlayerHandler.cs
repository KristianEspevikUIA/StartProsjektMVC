using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;


namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Avgjør om innlogget bruker kan se opplysninger om én bestemt spiller.
///
/// Reglene, i den rekkefølgen de vurderes:
///   Admin      — alltid tilgang.
///   Spilleren  — tilgang til seg selv (player.UserId == innlogget bruker).
///   Foresatt   — tilgang bare hvis det finnes en Guardianship mellom brukeren og
///                DENNE spilleren. Rollen "Guardian" gir ingen tilgang i seg selv.
///   Trener     — tilgang hvis nyeste ConsentEvent for spilleren er Full. Trenerrollen
///                er ikke knyttet til lag: en trener er trener, og CoachTeam avgjør
///                ingenting her.
///   Alle andre — ingen tilgang.
///
/// Handleren kaller aldri context.Fail(): den lar bare være å lykkes. Fail() ville
/// blokkert andre handlere for samme krav og gjort policyen vanskelig å utvide.
/// </summary>
public class CanViewPlayerHandler : AuthorizationHandler<CanViewPlayerRequirement, Player>
{
    private readonly AppDbContext _db;
    private readonly IConsentService _consent;

    public CanViewPlayerHandler(AppDbContext db, IConsentService consent)
    {
        _db = db;
        _consent = consent;
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

        // Trener: fullt samtykke er det eneste kravet. Rollen er ikke lagavgrenset --
        // enhver trener er trener for enhver spiller. Samtykket er dermed den ENESTE
        // grensen som står igjen mot at en trener ser en enkeltspillers svar, så det er
        // også den eneste som må holde. Se docs/five-c.md.
        if (context.User.IsInRole(Roles.Coach))
        {
            var level = await _consent.GetCurrentLevelAsync(resource.Id);
            if (level == ConsentLevel.Full)
            {
                context.Succeed(requirement);
                return;
            }
        }

        // Ingen treff: ingen tilgang.
    }
}
