using Microsoft.EntityFrameworkCore;
using Npgsql;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Stores 5C submissions in the application's own database -- which, since the switch to
/// Npgsql, is the Supabase Postgres database.
///
/// This is the default store. It replaced the in-memory one, where answers vanished on
/// every restart, and it is preferred over the PostgREST store because the process is
/// already connected to this database: one credential, one connection, real foreign keys to
/// Players and SurveyRounds, and a save that either lands completely or not at all.
/// </summary>
public sealed class EfSurveySubmissionStore : ISurveySubmissionStore
{
    private readonly AppDbContext _db;

    public EfSurveySubmissionStore(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public string Description => "The application database (Supabase Postgres)";

    /// <inheritdoc />
    public async Task SaveAsync(
        SurveySubmission submission,
        CancellationToken cancellationToken = default)
    {
        var candidate = await _db.FiveCSubmissions
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(
                s => s.RoundId == submission.RoundId
                     && s.PlayerId == submission.PlayerId
                     && s.RespondentUserId == submission.RespondentUserId,
                cancellationToken);

        var toPersist = candidate ?? new FiveCSubmission
        {
            RoundId = submission.RoundId,
            PlayerId = submission.PlayerId,
            RespondentUserId = submission.RespondentUserId
        };

        if (candidate is null)
        {
            _db.FiveCSubmissions.Add(toPersist);
        }
        else
        {
            _db.FiveCAnswers.RemoveRange(candidate.Answers);
            candidate.Answers.Clear();
        }

        toPersist.PlayerCode = submission.PlayerCode;
        toPersist.RespondentRole = submission.RespondentRole;
        toPersist.QuestionSetVersion = submission.QuestionSetVersion;
        toPersist.SubmittedAt = submission.SubmittedAt.ToUniversalTime();

        foreach (var answer in submission.Answers)
        {
            toPersist.Answers.Add(new FiveCAnswer
            {
                QuestionKey = answer.QuestionKey,
                CategoryKey = answer.CategoryKey,
                Value = answer.Value
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            var retry = await _db.FiveCSubmissions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(
                    s => s.RoundId == submission.RoundId
                         && s.PlayerId == submission.PlayerId
                         && s.RespondentUserId == submission.RespondentUserId,
                    cancellationToken);

            if (retry is null)
            {
                throw;
            }

            _db.FiveCAnswers.RemoveRange(retry.Answers);
            retry.Answers.Clear();
            retry.PlayerCode = submission.PlayerCode;
            retry.RespondentRole = submission.RespondentRole;
            retry.QuestionSetVersion = submission.QuestionSetVersion;
            retry.SubmittedAt = submission.SubmittedAt.ToUniversalTime();

            foreach (var answer in submission.Answers)
            {
                retry.Answers.Add(new FiveCAnswer
                {
                    QuestionKey = answer.QuestionKey,
                    CategoryKey = answer.CategoryKey,
                    Value = answer.Value
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <inheritdoc />
    public async Task<SurveySubmission?> FindAsync(
        int roundId,
        int playerId,
        string respondentUserId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.FiveCSubmissions
            .AsNoTracking()
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(
                s => s.RoundId == roundId
                     && s.PlayerId == playerId
                     && s.RespondentUserId == respondentUserId,
                cancellationToken);

        return row is null ? null : ToContract(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.FiveCSubmissions
            .AsNoTracking()
            .Include(s => s.Answers)
            .Where(s => s.RoundId == roundId && s.PlayerId == playerId)
            .ToListAsync(cancellationToken);

        return rows.Select(ToContract).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = playerIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return Array.Empty<SurveySubmission>();
        }

        // One query for the whole squad. One per player would be N+1 round trips to a
        // database that is not on this machine.
        var rows = await _db.FiveCSubmissions
            .AsNoTracking()
            .Include(s => s.Answers)
            .Where(s => s.RoundId == roundId && ids.Contains(s.PlayerId))
            .ToListAsync(cancellationToken);

        return rows.Select(ToContract).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> CountByRoundAsync(
        IEnumerable<int> roundIds,
        CancellationToken cancellationToken = default)
    {
        var ids = roundIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        // Counted in the database. The rows never leave it, and neither do the answers
        // hanging off them.
        var counts = await _db.FiveCSubmissions
            .AsNoTracking()
            .Where(s => ids.Contains(s.RoundId))
            .GroupBy(s => s.RoundId)
            .Select(group => new { RoundId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RoundId, row => row.Count, cancellationToken);

        return ids.ToDictionary(
            id => id,
            id => counts.TryGetValue(id, out var count) ? count : 0);
    }

    private static SurveySubmission ToContract(FiveCSubmission row) => new()
    {
        RoundId = row.RoundId,
        PlayerId = row.PlayerId,
        PlayerCode = row.PlayerCode,
        RespondentRole = row.RespondentRole,
        RespondentUserId = row.RespondentUserId,
        QuestionSetVersion = row.QuestionSetVersion,
        SubmittedAt = row.SubmittedAt,
        Answers = row.Answers
            .Select(a => new SurveyAnswer
            {
                QuestionKey = a.QuestionKey,
                CategoryKey = a.CategoryKey,
                Value = a.Value
            })
            .ToList()
    };
}
