namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The panel a planned-but-unbuilt page shows instead of a bare "Not built yet."
///
/// Five pages used to be linked from the menu and answer with one italic sentence: users
/// and roles, teams and coaches, the data access request, the player search, and consent.
/// Clicking any of them left the reader on a page with nothing on it and no way on except
/// the browser's back button.
///
/// Two things changed. Nothing links to an unbuilt page any more -- Views/Admin/Index.cshtml
/// lists them as planned rather than as links -- and the pages themselves now say who owns
/// the work, what the page will do, and where to go instead. A shared URL or a developer
/// following a TODO still lands somewhere that explains itself.
///
/// Delete the placeholder along with this model when the page is built. It is a signpost,
/// not a base class.
/// </summary>
public sealed class NotBuiltYetViewModel
{
    /// <summary>Who on the team owns the work. Matches the ownership table in the README.</summary>
    public required string Owner { get; init; }

    /// <summary>
    /// What the finished page is meant to do, one bullet per thing. Written for whoever
    /// picks the work up, so constraints belong here too -- not only features.
    /// </summary>
    public IReadOnlyList<string> Planned { get; init; } = Array.Empty<string>();

    /// <summary>Where the reader is sent instead. Every placeholder has to offer one.</summary>
    public string BackText { get; init; } = "Back to the front page";

    public string BackController { get; init; } = "Home";

    public string BackAction { get; init; } = "Index";
}
