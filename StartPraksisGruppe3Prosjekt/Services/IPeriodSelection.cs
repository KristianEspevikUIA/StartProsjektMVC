using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Which period the user is looking at, remembered across pages.
///
/// Without this, picking a period on the forms page and then opening the team overview
/// silently threw the choice away and snapped back to the current period. Every page went
/// its own way, and the only fix was to thread roundId through every link in the app.
///
/// The choice is kept in a cookie rather than in the URL, so it survives a plain menu click.
/// A URL that names a period still wins -- a shared link has to mean what it says -- and
/// following one updates what is remembered.
/// </summary>
public interface IPeriodSelection
{
    /// <summary>
    /// The period to show.
    ///
    /// In order: the one asked for in the URL, then the one remembered from last time, then
    /// the current one. A remembered period that has since been deleted is ignored rather
    /// than turning into a 404 on a page the user did not ask for.
    /// </summary>
    Task<SurveyRound?> ResolveAsync(
        int? requestedRoundId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same decision, made against periods the caller has already loaded.
    ///
    /// A page that shows a period picker has the whole list in hand before it needs to know
    /// which one is selected. <see cref="ResolveAsync"/> would fetch it again -- the form
    /// list did exactly that, two round trips for one list. Nothing else differs: the order
    /// is still URL, then remembered, then current, and following a URL still updates what
    /// is remembered.
    /// </summary>
    /// <param name="rounds">Every period. Order does not matter.</param>
    /// <param name="requestedRoundId">The period named in the URL, if any.</param>
    SurveyRound? Select(IReadOnlyList<SurveyRound> rounds, int? requestedRoundId);
}
