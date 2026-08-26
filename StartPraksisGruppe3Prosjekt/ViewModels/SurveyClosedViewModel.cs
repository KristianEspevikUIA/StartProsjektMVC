namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// Shown instead of the form when the round is not open.
///
/// This replaces a bare 400. Someone who clicks a link from an old message is not doing
/// anything wrong, and a status code is not something to hand a 14-year-old. The date is
/// the point: "closed" on its own is a dead end, "closed on 12 May" is an explanation.
///
/// It applies to POST as well. Somebody with the form open when the round closes has to
/// meet this page on save -- not an error, and not a silent accept either.
/// </summary>
public class SurveyClosedViewModel
{
    public string RoundName { get; set; } = string.Empty;

    public DateTimeOffset OpensAt { get; set; }

    public DateTimeOffset ClosesAt { get; set; }

    /// <summary>True when the round has not started yet, rather than already being over.</summary>
    public bool NotOpenYet { get; set; }

    /// <summary>True when this user did answer while the round was open.</summary>
    public bool HasAnswered { get; set; }

    public string PlayerCode { get; set; } = string.Empty;

    public string Headline => NotOpenYet ? "This round has not opened yet" : "This round is closed";

    public string Explanation => NotOpenYet
        ? $"Answering opens on {OpensAt.ToLocalTime():d MMMM yyyy}."
        : $"This round closed on {ClosesAt.ToLocalTime():d MMMM yyyy}. Answers cannot be changed after a round has closed.";
}
