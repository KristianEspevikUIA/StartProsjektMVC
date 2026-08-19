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
/// Eier: Victor.
///
/// Utfylling av skjemaet for alle tre respondenttypene. Tre ting må sitte:
///   1. Runden må være åpen (SurveyRound.IsOpenAt) — ellers avvis.
///   2. Den som fyller ut må ha lov til å svare OM denne spilleren (CanViewPlayer).
///   3. "Vet ikke" lagres som null, ikke som 3.
/// </summary>
[Authorize]
public class SurveyController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;

    public SurveyController(AppDbContext db, IAuthorizationService authz)
    {
        _db = db;
        _authz = authz;
    }

    /// <summary>
    /// Åpne runder for innlogget bruker.
    /// TODO (Victor): list rundene, og for hver runde hvilke spillere brukeren skal svare om.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Skjemaet.
    /// TODO (Victor): bygg SurveyFormViewModel av Items, og sett Respondent ut fra rollen.
    /// Ledeteksten snus for trener og foresatt — Item.Text står uendret i basen.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Fill(int roundId, int playerId)
    {
        var round = await _db.SurveyRounds.FirstOrDefaultAsync(r => r.Id == roundId);
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);

        if (round is null || player is null)
        {
            return NotFound();
        }

        if (!round.IsOpenAt(DateTimeOffset.UtcNow))
        {
            // TODO (Victor): egen visning for stengt runde i stedet for BadRequest.
            return BadRequest("Runden er ikke åpen.");
        }

        var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var model = new SurveyFormViewModel
        {
            RoundId = round.Id,
            RoundName = round.Name,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            Respondent = RespondentType.Player
        };

        // TODO (Victor): fyll model.Answers med de ti påstandene, sortert på Number.

        return View(model);
    }

    /// <summary>
    /// Lagring.
    /// TODO (Victor): sjekk runde + autorisasjon på nytt (aldri stol på skjulte felt i skjemaet),
    /// opprett eller oppdater Response for (RoundId, PlayerId, RespondentUserId), og lagre
    /// svarene. Én besvarelse per person per spiller per runde — det finnes en unik indeks.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Fill(SurveyFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }
}
