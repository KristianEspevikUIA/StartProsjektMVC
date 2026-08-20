using Microsoft.AspNetCore.Authorization;

namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Krav om at innlogget bruker har lov til å se ETT bestemt lag i det hele tatt.
///
/// Dette er et annet spørsmål enn <see cref="CanViewTeamAggregateRequirement"/>:
/// aggregatet handler om snittet skal vises, dette handler om siden skal vises.
/// Rollen "Coach" gir tilgang til egne lag, ikke til alle lag — uten denne kunne en
/// hvilken som helst trener bla seg gjennom lag-ID-er og få bekreftet hvilke lag som
/// finnes og hva de heter.
/// </summary>
public class CanViewTeamRequirement : IAuthorizationRequirement
{
}
