using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Measurement periods -- <see cref="SurveyRound"/> in the model, "period" on screen.
///
/// Creating one goes through here whether it comes from the admin page or from seeding,
/// so the rules that make a period usable live in one place instead of being re-argued in
/// each caller. A period created by hand and a period created by seed data are the same
/// kind of thing.
/// </summary>
public interface IPeriodService
{
    /// <summary>Every period, newest closing date first.</summary>
    Task<IReadOnlyList<SurveyRound>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The period the form should land on: the open one closing furthest out, or -- when
    /// nothing is open -- the most recent. Null only when no period exists at all.
    ///
    /// More than one period can be open at once, which is normal when a new one starts
    /// before the previous has closed. Picking the one that closes last means a new period
    /// takes over as soon as it opens.
    /// </summary>
    Task<SurveyRound?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>How many submissions exist per period. Used to show that a period is empty.</summary>
    Task<IReadOnlyDictionary<int, int>> GetSubmissionCountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a period. Returns the new round, or a list of problems if it could not be
    /// created -- a name already taken, or a window that ends before it starts.
    /// </summary>
    Task<PeriodResult> CreateAsync(
        string name,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a period now by moving its end to this instant. Answers already given are
    /// kept; the period simply stops accepting new ones.
    /// </summary>
    Task<PeriodResult> CloseNowAsync(int roundId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of creating or changing a period.</summary>
/// <param name="Round">The period, when it worked.</param>
/// <param name="Problems">What was wrong, when it did not. Empty on success.</param>
public sealed record PeriodResult(SurveyRound? Round, IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0 && Round is not null;

    public static PeriodResult Ok(SurveyRound round) => new(round, Array.Empty<string>());

    public static PeriodResult Failed(params string[] problems) => new(null, problems);
}
