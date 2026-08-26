using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The landing page for the form: which round is open, and which forms this user is
/// expected to fill in.
///
/// One list with three meanings. For a player it is a single card about themselves; for a
/// guardian one per child; for a coach one per player on their teams. The view does not
/// branch on role -- <see cref="ISurveyAssignmentService"/> has already worked out what
/// belongs in the list.
/// </summary>
public class SurveyIndexViewModel
{
    /// <summary>The round being answered, or null when nothing is open right now.</summary>
    public RoundSummary? OpenRound { get; set; }

    /// <summary>The forms this user should fill in for <see cref="OpenRound"/>.</summary>
    public IReadOnlyList<SurveyAssignment> Assignments { get; set; } = Array.Empty<SurveyAssignment>();

    /// <summary>Earlier rounds, newest first. Listed as closed, without a link.</summary>
    public IReadOnlyList<RoundSummary> ClosedRounds { get; set; } = Array.Empty<RoundSummary>();

    /// <summary>
    /// Where submitted answers are being sent, e.g. "Supabase". Shown to admins only, so
    /// that "did that actually save anywhere" has an answer on the page rather than in a log.
    /// </summary>
    public string? StoreDescription { get; set; }

    public int AnsweredCount => Assignments.Count(a => a.HasAnswered);

    public int RemainingCount => Assignments.Count - AnsweredCount;

    /// <param name="Id">Round id.</param>
    /// <param name="Name">Round name, e.g. "Autumn 2026".</param>
    /// <param name="OpensAt">When answering opens.</param>
    /// <param name="ClosesAt">When answering closes. Always shown: "closed" without a date is a dead end.</param>
    public sealed record RoundSummary(
        int Id,
        string Name,
        DateTimeOffset OpensAt,
        DateTimeOffset ClosesAt);
}
