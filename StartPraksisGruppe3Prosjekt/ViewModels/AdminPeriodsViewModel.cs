using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The admin page for measurement periods: what exists, how full each one is, and the form
/// for adding another.
///
/// This is the reusable way to add a period. Seeding uses the same service, so a period
/// added here is the same kind of thing as one that shipped with the app.
/// </summary>
public class AdminPeriodsViewModel
{
    public IReadOnlyList<PeriodRow> Periods { get; set; } = Array.Empty<PeriodRow>();

    /// <summary>The form for a new period. Bound on POST.</summary>
    public NewPeriodInput NewPeriod { get; set; } = new();

    public sealed record PeriodRow(
        int Id,
        string Name,
        DateTimeOffset OpensAt,
        DateTimeOffset ClosesAt,
        bool IsOpen,
        int SubmissionCount)
    {
        public bool NotOpenYet => DateTimeOffset.UtcNow < OpensAt;

        public string Status => IsOpen ? "Open" : NotOpenYet ? "Not open yet" : "Closed";

        /// <summary>An empty period is a normal state, not a problem. Worth saying plainly.</summary>
        public bool IsEmpty => SubmissionCount == 0;
    }

    public class NewPeriodInput
    {
        [Required(ErrorMessage = "The period needs a name.")]
        [StringLength(100)]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose when the period opens.")]
        [DataType(DataType.Date)]
        [Display(Name = "Opens")]
        public DateTime OpensAt { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Choose when the period closes.")]
        [DataType(DataType.Date)]
        [Display(Name = "Closes")]
        public DateTime ClosesAt { get; set; } = DateTime.UtcNow.Date.AddDays(60);
    }
}
