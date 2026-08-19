using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Regner ut avviket (D) mellom hva treneren tror spilleren svarer og hva spilleren
/// faktisk svarer.
///
/// D LAGRES ALDRI. Alt regnes ut fra råsvarene hver gang. Grunnen er at et lagret
/// avvik blir en påstand om en mindreårig som ligger igjen i basen etter at svarene
/// er rettet, samtykket er trukket eller runden er over.
/// </summary>
public interface IScoringService
{
    /// <summary>
    /// Skåren for et enkeltsvar, etter reversering. Reverserte påstander snus slik at
    /// høy verdi alltid betyr "bra".
    /// </summary>
    int ScoreOf(Item item, int rawValue);

    /// <summary>
    /// Avvik for én spiller i én runde. Null hvis spilleren eller treneren ikke har svart.
    /// </summary>
    Task<PlayerGap?> GetPlayerGapAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Avvik for alle spillere på et lag i én runde. Kalleren må selv sjekke
    /// CanViewPlayer per spiller før noe vises.
    /// </summary>
    Task<IReadOnlyList<PlayerGap>> GetTeamGapsAsync(
        int roundId,
        int teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregert lagbilde. Returnerer null når antall besvarelser er under
    /// CanViewTeamAggregateRequirement.MinimumResponses.
    /// </summary>
    Task<TeamAggregate?> GetTeamAggregateAsync(
        int roundId,
        int teamId,
        CancellationToken cancellationToken = default);
}
