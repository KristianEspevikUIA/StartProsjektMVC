using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// De eneste sidene som er åpne uten innlogging: forside, personvernerklæring og
/// feilside. Alt annet krever innlogging via FallbackPolicy i Program.cs, også
/// actions som glemmer [Authorize].
/// </summary>
[AllowAnonymous]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IPlayerWelcomeService _welcome;

    public HomeController(ILogger<HomeController> logger, IPlayerWelcomeService welcome)
    {
        _logger = logger;
        _welcome = welcome;
    }

    /// <summary>
    /// The front page, which is also where signing in lands. A player the club has entered a
    /// first name or a photo for is welcomed with them; everyone else sees the page as it was.
    ///
    /// Only for the player role, and only ever the signed-in player's own: the lookup goes
    /// through Player.UserId, so there is no id here for anybody to change.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new HomeViewModel();

        if (User.IsInRole(Roles.Player) && User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
        {
            model.Welcome = await _welcome.GetForUserAsync(userId, cancellationToken);
        }

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
