using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// The player's first name and photo: reading them for the welcome, and entering them.
///
/// Every read that does not need the photo leaves it in the database -- the queries below ask
/// whether there IS one, which is a column check, and never load the bytes by accident. Only
/// <see cref="GetPhotoAsync"/> reads them.
///
/// No authorisation here. The controllers decide who may ask: the player for their own, an
/// administrator for any.
/// </summary>
public interface IPlayerWelcomeService
{
    /// <summary>
    /// The welcome for the signed-in user, when that user is a player with a first name or a
    /// photo. Null for everyone else, including a player the club has not entered anything for.
    /// </summary>
    Task<PlayerWelcome?> GetForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>The stored photo, or null.</summary>
    Task<StoredPhoto?> GetPhotoAsync(int playerId, CancellationToken cancellationToken = default);

    /// <summary>What is entered for one player, without the photo bytes. Null when nothing is.</summary>
    Task<PersonalDetailsSummary?> GetSummaryAsync(int playerId, CancellationToken cancellationToken = default);

    /// <summary>Every player, with whether a name and a photo are entered. For the admin list.</summary>
    Task<IReadOnlyList<PersonalDetailsRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The first names entered for these players, by player id. A player with none is left out.
    /// For the coaches' best eleven, the one staff page that shows a name -- see
    /// SuccessionController.Formation. Never the photo.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> FirstNamesAsync(
        IReadOnlyCollection<int> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the first name and photo source, and replaces or removes the photo.
    /// </summary>
    /// <param name="photo">A photo already through <see cref="PlayerPhotoRules.Prepare"/>, or null to keep the current one.</param>
    /// <param name="removePhoto">Removes the current photo. Ignored when a new one is given.</param>
    Task SaveAsync(
        int playerId,
        string? firstName,
        string? photoSource,
        PhotoCheck? photo,
        bool removePhoto,
        string updatedByUserId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPlayerWelcomeService" />
public sealed class PlayerWelcomeService : IPlayerWelcomeService
{
    private readonly AppDbContext _db;

    public PlayerWelcomeService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> FirstNamesAsync(
        IReadOnlyCollection<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        if (playerIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var rows = await _db.PlayerPersonalDetails
            .AsNoTracking()
            .Where(d => playerIds.Contains(d.PlayerId) && d.FirstName != null && d.FirstName != "")
            .Select(d => new { d.PlayerId, d.FirstName })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.FirstName))
            .ToDictionary(r => r.PlayerId, r => r.FirstName!.Trim());
    }

    /// <inheritdoc />
    public async Task<PlayerWelcome?> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var found = await _db.Players
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.Id,
                Details = _db.PlayerPersonalDetails
                    .Where(d => d.PlayerId == p.Id)
                    .Select(d => new { d.FirstName, HasPhoto = d.Photo != null, d.PhotoUpdatedAt })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (found?.Details is not { } details
            || (string.IsNullOrWhiteSpace(details.FirstName) && !details.HasPhoto))
        {
            return null;
        }

        return new PlayerWelcome(
            found.Id,
            string.IsNullOrWhiteSpace(details.FirstName) ? null : details.FirstName.Trim(),
            details.HasPhoto,
            VersionOf(details.PhotoUpdatedAt));
    }

    /// <inheritdoc />
    public async Task<StoredPhoto?> GetPhotoAsync(int playerId, CancellationToken cancellationToken = default)
    {
        var photo = await _db.PlayerPersonalDetails
            .AsNoTracking()
            .Where(d => d.PlayerId == playerId && d.Photo != null)
            .Select(d => new { d.Photo, d.PhotoContentType, d.PhotoUpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return photo?.Photo is { } bytes
            ? new StoredPhoto(bytes, photo.PhotoContentType ?? PlayerPhotoRules.Jpeg, VersionOf(photo.PhotoUpdatedAt))
            : null;
    }

    /// <inheritdoc />
    public Task<PersonalDetailsSummary?> GetSummaryAsync(int playerId, CancellationToken cancellationToken = default) =>
        _db.PlayerPersonalDetails
            .AsNoTracking()
            .Where(d => d.PlayerId == playerId)
            .Select(d => new PersonalDetailsSummary(
                d.FirstName,
                d.PhotoSource,
                d.Photo != null,
                d.PhotoContentType,
                d.Photo != null ? d.Photo.Length : 0,
                d.PhotoUpdatedAt,
                d.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonalDetailsRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Players
            .AsNoTracking()
            .OrderBy(p => p.Team!.Name)
            .ThenBy(p => p.Code)
            .Select(p => new PersonalDetailsRow(
                p.Id,
                p.Code,
                p.Team!.Name,
                p.UserId != null,
                _db.PlayerPersonalDetails.Where(d => d.PlayerId == p.Id).Select(d => d.FirstName).FirstOrDefault(),
                _db.PlayerPersonalDetails.Any(d => d.PlayerId == p.Id && d.Photo != null)))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(
        int playerId,
        string? firstName,
        string? photoSource,
        PhotoCheck? photo,
        bool removePhoto,
        string updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var details = await _db.PlayerPersonalDetails
            .FirstOrDefaultAsync(d => d.PlayerId == playerId, cancellationToken);

        if (details is null)
        {
            details = new PlayerPersonalDetails { PlayerId = playerId };
            _db.PlayerPersonalDetails.Add(details);
        }

        var now = DateTimeOffset.UtcNow;

        details.FirstName = Blank(firstName);
        details.PhotoSource = Blank(photoSource);
        details.UpdatedByUserId = updatedByUserId;
        details.UpdatedAt = now;

        if (photo is { IsAccepted: true })
        {
            details.Photo = photo.Photo;
            details.PhotoContentType = photo.ContentType;
            details.PhotoUpdatedAt = now;
        }
        else if (removePhoto)
        {
            details.Photo = null;
            details.PhotoContentType = null;
            details.PhotoUpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A number that changes when the photo does, for the photo's URL. The photo is served with a
    /// private cache lifetime, and without this a replaced photo would keep showing the old one.
    /// </summary>
    private static long VersionOf(DateTimeOffset? updatedAt) => updatedAt?.ToUnixTimeSeconds() ?? 0;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What the signed-in player is welcomed with.</summary>
/// <param name="PhotoVersion">Changes when the photo does. See PlayerWelcomeService.VersionOf.</param>
public sealed record PlayerWelcome(int PlayerId, string? FirstName, bool HasPhoto, long PhotoVersion);

public sealed record StoredPhoto(byte[] Bytes, string ContentType, long Version);

public sealed record PersonalDetailsSummary(
    string? FirstName,
    string? PhotoSource,
    bool HasPhoto,
    string? PhotoContentType,
    int PhotoBytes,
    DateTimeOffset? PhotoUpdatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PersonalDetailsRow(
    int PlayerId,
    string Code,
    string TeamName,
    bool HasAccount,
    string? FirstName,
    bool HasPhoto);
