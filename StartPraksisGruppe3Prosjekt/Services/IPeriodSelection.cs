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
}
