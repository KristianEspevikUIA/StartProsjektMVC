using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Eier: Brage.
///
/// Spilleren ser sine egne svar. Merk at spilleren IKKE skal se trenerens gjetning
/// eller avviket før det er avklart hvordan den tilbakemeldingen skal gis —
/// et tall som sier "treneren tror du er utrygg" er ikke noe å møte alene i en app.
/// TODO: avklares med IK Start før PlayerDetail-visningen bygges ut.
/// </summary>
[Authorize(Roles = Roles.Player + "," + Roles.Admin)]
public class PlayerController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IConsentService _consent;

    public PlayerController(AppDbContext db, IAuthorizationService authz, IConsentService consent)
    {
        _db = db;
        _authz = authz;
        _consent = consent;
    }

    /// <summary>
    /// Spillerens egen forside.
    /// TODO (Brage): finn spilleren via Player.UserId == innlogget bruker.
    /// Husk at UserId kan være null for spillere uten konto — da finnes det ingen rad å vise.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Egne svar i en runde.
    /// TODO (Brage): hent spillerens Response for runden og vis svarene.
    /// </summary>
    public async Task<IActionResult> MyResponses(int id, int? roundId)
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
}
