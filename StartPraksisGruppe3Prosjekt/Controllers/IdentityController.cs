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
/// Read-only. Every action is GET, so the match picker works as a plain form without
/// JavaScript and a view can be bookmarked or sent to another coach.
///
/// The benchmark is three pages -- Overview, Key Insights, Development over time -- over the
/// same team and match, with buttons between them (Views/Identity/_IdentityLayout.cshtml).
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

    /// <summary>Overview: both benchmark tables and the status legend.</summary>
    /// <param name="team">"U14". Left out, the first team with data.</param>
    /// <param name="match">A match id, or "all" / left out for the average of every match.</param>
    public IActionResult Index(string? team, string? match) => Benchmark(team, match);

    /// <summary>Key Insights: Key Tactical Insights and the Player Highlight.</summary>
    public IActionResult Insights(string? team, string? match) => Benchmark(team, match);

    /// <summary>Development over time: every match, marker by marker.</summary>
    public IActionResult Development(string? team, string? match) => Benchmark(team, match);

    /// <summary>The same page model for all three; the view of the action picks what to show.</summary>
    private IActionResult Benchmark(string? team, string? match)
    {
        var model = _builder.Build(team, match);

        return model is null ? NotFound() : View(model);
    }

    /// <summary>The Gold Standard itself -- marker, elite range, best at it -- as a reference.</summary>
    public IActionResult GoldStandard() => View(_catalog.GoldStandard);
}
