using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// The end-of-period reflection as one block, for <c>Views/Shared/_FiveCReflection.cshtml</c>.
///
/// Two pages show the same thing to different readers: the coach sees all three reflections
/// on <c>/Coach/FiveCPlayer</c>, and the player or their guardian sees their own -- plus the
/// coach's, once the coach has shared -- on the feedback page. Only the labels differ, which
/// is what this carries. One partial rather than two, for the same reason the bar chart and
/// the tab strip are shared: two copies of a page that shows what somebody wrote about a
/// child are two places to get the rule wrong.
///
/// Nothing here decides what may be seen. The coach's answers are removed from the model
/// before this is built -- see <see cref="FiveCFeedbackViewModel.Redact"/>. This type only
/// says how to caption what is left, and <see cref="CoachWithheld"/> explains a gap that
/// somebody has already made.
/// </summary>
/// <param name="Questions">The reflection questions, in the order the form asked them.</param>
/// <param name="PlayerLabel">What to call the player's column, e.g. "Player" or "You".</param>
/// <param name="GuardianLabel">The same for the guardian, e.g. "Guardian" or "Your guardian".</param>
/// <param name="CoachLabel">The same for the coach, e.g. "Coach" or "You".</param>
/// <param name="CoachWithheld">
/// Shown in place of the coach's answers when the coach has written something the reader is
/// not being shown yet. Null when there is nothing to explain -- either the coach answered
/// and it is visible, or they did not answer at all. Saying "not shared yet" when the coach
/// simply has not written anything would invent a paragraph that does not exist.
/// </param>
public sealed record FiveCReflectionViewModel(
    IReadOnlyList<ReflectionComparison> Questions,
    string PlayerLabel,
    string GuardianLabel,
    string CoachLabel,
    string? CoachWithheld = null)
{
    /// <summary>True when at least one of them wrote something the reader can see.</summary>
    public bool HasAnything => Questions.Any(q => !q.Unanswered);
}
