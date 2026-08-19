using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Eier: Brage.
///
/// Foresatt ser sitt eget barn — og bare det. Rollen alene gir ingen tilgang;
/// koblingen må finnes i Guardianship. Se mønsteret i CoachController.PlayerDetail.
/// </summary>
[Authorize(Roles = Roles.Guardian + "," + Roles.Admin)]
public class GuardianController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IConsentService _consent;

    public GuardianController(AppDbContext db, IAuthorizationService authz, IConsentService consent)
    {
        _db = db;
        _authz = authz;
        _consent = consent;
    }

    /// <summary>
    /// Barna innlogget foresatt er registrert på.
    /// TODO (Brage): slå opp Guardianships på innlogget bruker-ID.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Detaljer om ett barn.
    /// TODO (Brage): fyll ut modellen. Autorisasjonsmønsteret ligger allerede her.
    /// </summary>
    public async Task<IActionResult> Player(int id)
    {
        var player = await _db.Players
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (player is null)
        {
            return NotFound();
        }

        var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var model = new PlayerDetailViewModel
        {
            PlayerId = player.Id,
            Code = player.Code,
            TeamName = player.Team?.Name ?? string.Empty,
            Consent = await _consent.GetCurrentLevelAsync(player.Id)
        };

        return View(model);
    }

    /// <summary>
    /// Samtykkebildet med historikk.
    /// TODO (Brage): bygg ConsentViewModel av GetCurrentLevelAsync + GetHistoryAsync.
    /// </summary>
    [HttpGet]
    public IActionResult Consent(int id)
    {
        return View();
    }

    /// <summary>
    /// Endring av samtykke.
    /// TODO (Brage): kall IConsentService.RecordAsync — det legger til en NY hendelse.
    /// Ingen rad skal endres eller slettes; AppDbContext kaster hvis du prøver.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Consent(int id, ConsentLevel level)
    {
        return RedirectToAction(nameof(Consent), new { id });
    }
}
