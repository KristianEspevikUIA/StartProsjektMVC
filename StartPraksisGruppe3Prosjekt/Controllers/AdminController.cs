using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Eier: Kristian.
///
/// Brukere, lag og GDPR-oppgavene: innsyn (utlevering av det som er registrert om en
/// spiller) og sletting. Admin ser alt — derfor skal admin-oppslag på enkeltspillere
/// logges når revisjonsloggen kommer på plass.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConsentService _consent;

    public AdminController(AppDbContext db, IConsentService consent)
    {
        _db = db;
        _consent = consent;
    }

    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Brukere og rolletildeling.
    /// TODO (Kristian): list brukere med roller, og la admin gi/fjerne roller.
    /// </summary>
    public IActionResult Users()
    {
        return View();
    }

    /// <summary>
    /// Lag, trenerkoblinger og spillerlister.
    /// TODO (Kristian): CRUD på Team og CoachTeam.
    /// </summary>
    public IActionResult Teams()
    {
        return View();
    }

    /// <summary>
    /// Innsyn: alt systemet har registrert om én spiller, i lesbar form.
    /// TODO (Kristian): samle Player, Guardianships, Responses med Answers og hele
    /// ConsentEvent-historikken. Avviket er ikke med — det er ikke lagret, det regnes ut.
    /// </summary>
    public IActionResult Export(int id)
    {
        return View();
    }

    /// <summary>
    /// Sletting av en spiller og alt som hører til.
    /// TODO (Kristian): dette er den ene operasjonen som fjerner ConsentEvent-rader, og
    /// den skal kreve en eksplisitt bekreftelse. Cascade tar svar, samtykkelogg og
    /// foresattkoblinger; Identity-brukeren må håndteres for seg.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        return RedirectToAction(nameof(Index));
    }
}
