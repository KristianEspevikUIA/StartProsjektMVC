using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// All lesing og skriving av samtykke skal gå gjennom denne tjenesten, slik at
/// append-only-regelen ikke er avhengig av at hver enkelt controller husker den.
/// </summary>
public interface IConsentService
{
    /// <summary>
    /// Gjeldende samtykke for spilleren = nyeste ConsentEvent.
    /// Ingen hendelser i det hele tatt betyr <see cref="ConsentLevel.None"/> —
    /// manglende samtykke er ikke det samme som samtykke.
    /// </summary>
    Task<ConsentLevel> GetCurrentLevelAsync(int playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gjeldende samtykke for flere spillere i ett kall. Brukes i lagoversikten,
    /// der ett kall per spiller ville blitt N+1 spørringer.
    /// </summary>
    Task<IReadOnlyDictionary<int, ConsentLevel>> GetCurrentLevelsAsync(
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legger til en ny samtykkehendelse. Endrer aldri en eksisterende rad.
    /// </summary>
    Task RecordAsync(
        int playerId,
        ConsentLevel level,
        string changedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Hele historikken for en spiller, nyeste først. Vises til foresatt og admin.</summary>
    Task<IReadOnlyList<ConsentEvent>> GetHistoryAsync(
        int playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Om en bruker har lov til å endre samtykket for spilleren:
    /// foresatt for spilleren, admin, eller spilleren selv når hen er myndig.
    /// </summary>
    Task<bool> CanRecordConsentAsync(
        string userId,
        int playerId,
        CancellationToken cancellationToken = default);
}
