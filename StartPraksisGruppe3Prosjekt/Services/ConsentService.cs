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
    /// <remarks>
    /// Implementert i forbindelse med 5C-treneroversikten, som lister et helt lag og
    /// ellers ville gjort ett oppslag per spiller. Samme regel som
    /// <see cref="GetCurrentLevelAsync"/>: nyeste hendelse vinner, og ingen hendelse
    /// i det hele tatt er <see cref="ConsentLevel.None"/> — manglende samtykke er ikke
    /// det samme som samtykke. Resten av tjenesten er fortsatt Brages TODO-er.
    /// </remarks>
    public async Task<IReadOnlyDictionary<int, ConsentLevel>> GetCurrentLevelsAsync(
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = playerIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, ConsentLevel>();
        }

        // Grupperingen gjøres i minnet: SQLite har ingen DISTINCT ON, og en runde med
        // spillere er noen titalls rader.
        var events = await _db.ConsentEvents
            .AsNoTracking()
            .Where(c => ids.Contains(c.PlayerId))
            .Select(c => new { c.PlayerId, c.Level, c.OccurredAt, c.Id })
            .ToListAsync(cancellationToken);

        var latest = events
            .GroupBy(e => e.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(e => e.OccurredAt)
                    .ThenByDescending(e => e.Id)
                    .First()
                    .Level);

        // Spillere uten hendelser skal ha en rad i svaret, ikke mangle. En manglende
        // nøkkel ville tvunget kalleren til å gjette, og gjetningen ville blitt feil vei.
        return ids.ToDictionary(
            id => id,
            id => latest.TryGetValue(id, out var level) ? level : ConsentLevel.None);
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
