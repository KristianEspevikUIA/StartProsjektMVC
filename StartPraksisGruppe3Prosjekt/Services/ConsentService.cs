using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
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
    public async Task RecordAsync(
        int playerId,
        ConsentLevel level,
        string changedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changedByUserId))
        {
            throw new ArgumentException("A user id is required when recording consent.", nameof(changedByUserId));
        }

        if (!await CanRecordConsentAsync(changedByUserId, playerId, cancellationToken))
        {
            throw new InvalidOperationException("User is not allowed to change this consent level.");
        }

        _db.ConsentEvents.Add(new ConsentEvent
        {
            PlayerId = playerId,
            Level = level,
            ChangedByUserId = changedByUserId,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEvent>> GetHistoryAsync(
        int playerId,
        CancellationToken cancellationToken = default) =>
        await _db.ConsentEvents
            .AsNoTracking()
            .Where(c => c.PlayerId == playerId)
            .OrderByDescending(c => c.OccurredAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CanRecordConsentAsync(
        string userId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);

        if (player is null)
        {
            return false;
        }

        if (player.UserId == userId)
        {
            var age = player.AgeAt(DateOnly.FromDateTime(DateTime.UtcNow));
            return age >= PlayerRules.GuardianRequiredBelowAge;
        }

        var isGuardian = await _db.Guardianships
            .AsNoTracking()
            .AnyAsync(g => g.PlayerId == playerId && g.GuardianUserId == userId, cancellationToken);

        if (isGuardian)
        {
            return true;
        }

        return await _db.UserRoles
            .AsNoTracking()
            .Join(
                _db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, r.Name })
            .AnyAsync(x => x.UserId == userId && x.Name == Roles.Admin, cancellationToken);
    }
}
