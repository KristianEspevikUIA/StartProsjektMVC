using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Services.Identity;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Identity Benchmarking: a team's StatsBomb match numbers against IK Start's Gold Standard.
///
/// Coaches and administrators only, by role. There is no resource check beyond that, and none
/// is missing: the page is about teams, not about a player in the database, and the coach role
/// already sees every team (see CoachController). The Player Highlight does name players --
/// which is exactly why the role gate is the whole controller and not one action.
///
/// Read-only. Both actions are GET, so the match picker works as a plain form without
/// JavaScript and a view can be bookmarked or sent to another coach.
/// </summary>
[Authorize(Roles = Roles.Coach + "," + Roles.Admin)]
public class IdentityController : Controller
{
    private readonly IIdentityBenchmarkBuilder _builder;
    private readonly IIdentityCatalog _catalog;

    public IdentityController(IIdentityBenchmarkBuilder builder, IIdentityCatalog catalog)
    {
        _builder = builder;
        _catalog = catalog;
    }

    /// <param name="team">"U14". Left out, the first team with data.</param>
    /// <param name="match">A match id, or "all" / left out for the average of every match.</param>
    public IActionResult Index(string? team, string? match)
    {
        var model = _builder.Build(team, match);

        return model is null ? NotFound() : View(model);
    }

    /// <summary>The Gold Standard itself -- marker, elite range, best at it -- as a reference.</summary>
    public IActionResult GoldStandard() => View(_catalog.GoldStandard);
}
