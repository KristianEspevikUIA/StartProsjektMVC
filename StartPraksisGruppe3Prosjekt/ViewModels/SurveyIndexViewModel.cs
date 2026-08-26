using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The forms page: which period is being answered, and which forms this user still owes.
///
/// One list with three meanings. For a player it is a single card about themselves; for a
/// guardian one per child; for a coach one per player in the club. The view does not branch
/// on role -- <see cref="ISurveyAssignmentService"/> has already worked out what belongs in
/// the list.
///
/// The coach case is why <see cref="Filter"/> exists. A coach is no longer scoped to a team,
/// so their list is every player there is, and a list of two hundred cards is unusable long
/// before it is wrong.
/// </summary>
public class SurveyIndexViewModel
{
    /// <summary>The period being answered.</summary>
    public RoundSummary? SelectedRound { get; set; }

    /// <summary>Every period, for the picker. Closed ones can be viewed, not answered.</summary>
    public IReadOnlyList<RoundSummary> Rounds { get; set; } = Array.Empty<RoundSummary>();

    /// <summary>Whether the selected period still accepts answers.</summary>
    public bool SelectedRoundIsOpen { get; set; }

    /// <summary>The forms after filtering. What the page actually lists.</summary>
    public IReadOnlyList<SurveyAssignment> Assignments { get; set; } = Array.Empty<SurveyAssignment>();

    /// <summary>Totals before filtering, so the counts do not move when a filter is applied.</summary>
    public int TotalCount { get; set; }

    public int TotalAnswered { get; set; }

    public int TotalRemaining => TotalCount - TotalAnswered;

    /// <summary>Teams present in this user's forms, for the team filter.</summary>
    public IReadOnlyList<string> Teams { get; set; } = Array.Empty<string>();

    /// <summary>Roles present in this user's forms. A player only ever answers as themselves.</summary>
    public IReadOnlyList<RespondentType> Roles { get; set; } = Array.Empty<RespondentType>();

    public FilterInput Filter { get; set; } = new();

    /// <summary>
    /// Where submitted answers are being sent, e.g. "Supabase". Admins only, so "did that
    /// actually save anywhere" has an answer on the page rather than in a log file.
    /// </summary>
    public string? StoreDescription { get; set; }

    public bool HasActiveFilter =>
        Filter.Status != FormStatus.All
        || !string.IsNullOrWhiteSpace(Filter.Team)
        || Filter.Role is not null
        || !string.IsNullOrWhiteSpace(Filter.Query);

    /// <param name="Id">Period id.</param>
    /// <param name="Name">Period name, e.g. "Winter 2026".</param>
    /// <param name="OpensAt">When answering opens.</param>
    /// <param name="ClosesAt">When answering closes. Always shown: "closed" without a date is a dead end.</param>
    /// <param name="IsOpen">Whether it accepts answers right now.</param>
    public sealed record RoundSummary(
        int Id,
        string Name,
        DateTimeOffset OpensAt,
        DateTimeOffset ClosesAt,
        bool IsOpen)
    {
        public bool NotOpenYet => DateTimeOffset.UtcNow < OpensAt;

        public string Status => IsOpen ? "Open" : NotOpenYet ? "Not open yet" : "Closed";
    }

    /// <summary>
    /// The filters, bound from the query string so a filtered list is a shareable URL and
    /// survives the back button.
    /// </summary>
    public class FilterInput
    {
        /// <summary>The period. Null means the current one.</summary>
        public int? RoundId { get; set; }

        /// <summary>Team name, or null for all teams.</summary>
        public string? Team { get; set; }

        /// <summary>Which role the form is answered in, or null for all.</summary>
        public RespondentType? Role { get; set; }

        public FormStatus Status { get; set; } = FormStatus.All;

        /// <summary>Free text against the player code. Codes, never names.</summary>
        public string? Query { get; set; }
    }
}

/// <summary>Whether a form has been submitted.</summary>
public enum FormStatus
{
    All = 0,
    Pending = 1,
    Completed = 2
}
