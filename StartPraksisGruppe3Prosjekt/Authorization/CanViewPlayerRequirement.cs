using Microsoft.AspNetCore.Authorization;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Krav om at innlogget bruker har lov til å se opplysninger om ÉN bestemt spiller.
/// Rollen alene avgjør ingenting: en trener har ikke tilgang til alle spillere,
/// bare til spillere på egne lag og bare når samtykket tillater det.
/// </summary>
public class CanViewPlayerRequirement : IAuthorizationRequirement
{
}
