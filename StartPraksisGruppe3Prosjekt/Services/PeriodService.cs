using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <inheritdoc cref="IPeriodService" />
public sealed class PeriodService : IPeriodService
{
    private readonly AppDbContext _db;
    private readonly ISurveySubmissionStore _store;

    public PeriodService(AppDbContext db, ISurveySubmissionStore store)
    {
        _db = db;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveyRound>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await _db.SurveyRounds
            .AsNoTracking()
            .OrderByDescending(r => r.ClosesAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<SurveyRound?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var rounds = await _db.SurveyRounds
            .AsNoTracking()
            .OrderByDescending(r => r.ClosesAt)
            .ToListAsync(cancellationToken);

        return rounds.FirstOrDefault(r => r.IsOpenAt(now)) ?? rounds.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> GetSubmissionCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var roundIds = await _db.SurveyRounds
            .AsNoTracking()
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        // Counts come from the submission store rather than from the database, because that
        // is where 5C answers live -- and it may not be the database. See docs/five-c.md.
        //
        // One call for every period. This used to read every submission for every player,
        // one period at a time: with the Supabase store that was two HTTP requests per
        // period, dragging back twenty-five answers per respondent only to call .Count on
        // the list. The store counts now, and nothing but the numbers comes back.
        return await _store.CountByRoundAsync(roundIds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PeriodResult> CreateAsync(
        string name,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt,
        CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();

        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("The period needs a name.");
        }
        else if (await _db.SurveyRounds.AnyAsync(r => r.Name == name, cancellationToken))
        {
            // Two periods with the same name are indistinguishable in every list in the app,
            // and answers would silently split between them.
            problems.Add($"A period called \"{name}\" already exists.");
        }

        if (closesAt <= opensAt)
        {
            problems.Add("The closing date has to be after the opening date.");
        }

        if (problems.Count > 0)
        {
            return PeriodResult.Failed(problems.ToArray());
        }

        var round = new SurveyRound
        {
            Name = name,
            // Stored as UTC. Npgsql maps DateTimeOffset to timestamptz and rejects any
            // offset other than zero, so the conversion happens here rather than failing
            // at the driver with a message about offsets.
            OpensAt = opensAt.ToUniversalTime(),
            ClosesAt = closesAt.ToUniversalTime()
        };

        _db.SurveyRounds.Add(round);
        await _db.SaveChangesAsync(cancellationToken);

        return PeriodResult.Ok(round);
    }

    /// <inheritdoc />
    public async Task<PeriodResult> CloseNowAsync(
        int roundId,
        CancellationToken cancellationToken = default)
    {
        var round = await _db.SurveyRounds.FirstOrDefaultAsync(r => r.Id == roundId, cancellationToken);

        if (round is null)
        {
            return PeriodResult.Failed("That period does not exist.");
        }

        var now = DateTimeOffset.UtcNow;

        if (!round.IsOpenAt(now))
        {
            return PeriodResult.Failed($"\"{round.Name}\" is not open.");
        }

        round.ClosesAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return PeriodResult.Ok(round);
    }
}
