namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>
/// Navnene på de ressursbaserte policyene. Begge krever en ressurs og kan derfor
/// ikke brukes som [Authorize(Policy = ...)] alene — de kalles med
/// IAuthorizationService.AuthorizeAsync(User, resource, Policies.X).
/// </summary>
public static class Policies
{
    /// <summary>Krever <see cref="Models.Player"/> som ressurs.</summary>
    public const string CanViewPlayer = "CanViewPlayer";

    /// <summary>Krever <see cref="TeamAggregateResource"/> som ressurs.</summary>
    public const string CanViewTeamAggregate = "CanViewTeamAggregate";

    /// <summary>Krever <see cref="Models.Team"/> som ressurs.</summary>
    public const string CanViewTeam = "CanViewTeam";
}
