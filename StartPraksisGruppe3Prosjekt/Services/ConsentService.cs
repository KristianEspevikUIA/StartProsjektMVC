using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Eier: Brage.
///
/// MERK: <see cref="GetCurrentLevelAsync"/> er ferdig implementert med vilje —
/// CanViewPlayerHandler bruker den til å avgjøre om en trener får se en spiller.
/// Hvis den kaster, faller autorisasjonen. Ikke gjør den om til en stub.
/// Resten er signaturer med TODO.
/// </summary>
public class ConsentService : IConsentService
{
    private readonly AppDbContext _db;

    public ConsentService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ConsentLevel> GetCurrentLevelAsync(
        int playerId,
        CancellationToken cancellationToken = default)
    {
        // Nyeste hendelse vinner. Id som sekundærsortering, i tilfelle to hendelser
        // havner på samme tidspunkt.
        var latest = await _db.ConsentEvents
            .AsNoTracking()
            .Where(c => c.PlayerId == playerId)
            .OrderByDescending(c => c.OccurredAt)
            .ThenByDescending(c => c.Id)
            .Select(c => (ConsentLevel?)c.Level)
            .FirstOrDefaultAsync(cancellationToken);

        // Ingen registrert hendelse = ingen samtykke.
        return latest ?? ConsentLevel.None;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, ConsentLevel>> GetCurrentLevelsAsync(
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        // TODO (Brage): grupper ConsentEvents på PlayerId, ta nyeste per gruppe,
        // og fyll inn ConsentLevel.None for spillere uten hendelser.
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task RecordAsync(
        int playerId,
        ConsentLevel level,
        string changedByUserId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Brage): sjekk CanRecordConsentAsync først, legg deretter til en NY
        // ConsentEvent med OccurredAt = DateTimeOffset.UtcNow. Aldri Update/Remove —
        // AppDbContext kaster hvis du prøver.
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ConsentEvent>> GetHistoryAsync(
        int playerId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Brage): hele loggen for spilleren, nyeste først.
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<bool> CanRecordConsentAsync(
        string userId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Brage): true for admin, for registrert foresatt til spilleren, og for
        // spilleren selv når hen er myndig (se PlayerRules.GuardianRequiredBelowAge).
        throw new NotImplementedException();
    }
}
