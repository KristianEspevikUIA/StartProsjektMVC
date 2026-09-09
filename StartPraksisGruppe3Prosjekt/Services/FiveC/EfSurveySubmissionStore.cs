using Microsoft.Data.Sqlite;
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
        var candidate = await FindRowAsync(submission, cancellationToken);

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

        Apply(submission, toPersist);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Someone else wrote this respondent's submission between the lookup above and
            // this save -- two open tabs, or a submit button pressed twice. The index did
            // its job; the row that won the race is the one to correct.
            //
            // What was staged for the insert has to go first. A failed SaveChangesAsync
            // leaves the new submission and its answers in the change tracker as Added, and
            // saving again would insert them a second time -- into the same index, for the
            // same failure. Nothing reached the database, so clearing loses nothing.
            _db.ChangeTracker.Clear();

            var winner = await FindRowAsync(submission, cancellationToken);

            if (winner is null)
            {
                // No row to correct, so this was some other unique index refusing. Let it
                // be seen rather than retried into a second identical failure.
                throw;
            }

            Apply(submission, winner);

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// This respondent's submission for this player in this round -- the rated answers and
    /// the written reflection both, so that a correction replaces the whole form rather
    /// than half of it.
    /// </summary>
    private Task<FiveCSubmission?> FindRowAsync(
        SurveySubmission submission,
        CancellationToken cancellationToken) =>
        _db.FiveCSubmissions
            .Include(s => s.Answers)
            .Include(s => s.Reflection)
            .FirstOrDefaultAsync(
                s => s.RoundId == submission.RoundId
                     && s.PlayerId == submission.PlayerId
                     && s.RespondentUserId == submission.RespondentUserId,
                cancellationToken);

    /// <summary>
    /// Writes the submitted form onto a row, whether that row is a new one or the one
    /// already in the database. One place, so a first attempt and its retry cannot come to
    /// disagree about what a saved submission contains.
    /// </summary>
    private void Apply(SurveySubmission submission, FiveCSubmission row)
    {
        // Answering again is a correction, not a second opinion: what the previous
        // submission left behind goes, answers and reflection both, rather than being added
        // to. A wholesale replace rather than a diff -- it cannot leave standing an answer
        // the respondent has since cleared, which is the point of letting them correct one.
        // On a new row there is nothing to remove, and this does nothing.
        _db.FiveCAnswers.RemoveRange(row.Answers);
        row.Answers.Clear();

        _db.FiveCReflectionAnswers.RemoveRange(row.Reflection);
        row.Reflection.Clear();

        row.PlayerCode = submission.PlayerCode;
        row.RespondentRole = submission.RespondentRole;
        row.QuestionSetVersion = submission.QuestionSetVersion;
        row.SubmittedAt = submission.SubmittedAt.ToUniversalTime();

        foreach (var answer in submission.Answers)
        {
            row.Answers.Add(new FiveCAnswer
            {
                QuestionKey = answer.QuestionKey,
                CategoryKey = answer.CategoryKey,
                Value = answer.Value
            });
        }

        // Only what was actually written. A blank reflection question is left out entirely
        // rather than stored as an empty row -- "not answered" is the absence of a row here,
        // exactly as a null value means it for a statement.
        foreach (var answer in submission.Reflection)
        {
            if (string.IsNullOrWhiteSpace(answer.Value))
            {
                continue;
            }

            row.Reflection.Add(new FiveCReflectionAnswer
            {
                QuestionKey = answer.QuestionKey,
                Value = answer.Value
            });
        }
    }

    /// <summary>SQLITE_CONSTRAINT_UNIQUE. Microsoft.Data.Sqlite names no constant for it.</summary>
    private const int SqliteUniqueViolation = 2067;

    /// <summary>
    /// Was that a unique index refusing the row? Postgres is what production runs on and
    /// says so in the SQLSTATE; SQLite, which the tests run on, leaves SqlState null and
    /// reports the same refusal as an extended result code.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException switch
        {
            PostgresException postgres =>
                postgres.SqlState == PostgresErrorCodes.UniqueViolation,
            SqliteException sqlite =>
                sqlite.SqliteExtendedErrorCode == SqliteUniqueViolation,
            _ => false
        };

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
            .Include(s => s.Reflection)
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
            .Include(s => s.Reflection)
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
            .Include(s => s.Reflection)
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
            .ToList(),
        Reflection = row.Reflection
            .Select(a => new ReflectionAnswer
            {
                QuestionKey = a.QuestionKey,
                Value = a.Value
            })
            .ToList()
    };
}
