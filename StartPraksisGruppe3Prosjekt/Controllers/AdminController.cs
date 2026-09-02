using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

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
    private readonly IPeriodService _periods;
    private readonly IPlayerAccessLog _accessLog;

    public AdminController(
        AppDbContext db,
        IConsentService consent,
        IPeriodService periods,
        IPlayerAccessLog accessLog)
    {
        _db = db;
        _consent = consent;
        _periods = periods;
        _accessLog = accessLog;
    }

    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Measurement periods: what exists, how full each one is, and a form for adding another.
    ///
    /// This is the supported way to create a period. It goes through the same
    /// <see cref="IPeriodService"/> the seeding uses, so a period added here behaves exactly
    /// like one that shipped with the app -- no separate path, no one-off insert.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Periods(CancellationToken cancellationToken)
    {
        return View(await BuildPeriodsViewAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> CreatePeriod(
        AdminPeriodsViewModel model,
        CancellationToken cancellationToken)
    {
        var input = model.NewPeriod;

        if (ModelState.IsValid)
        {
            // The form gives dates; a period runs to the end of its closing day rather than
            // to midnight at the start of it, which would close it a day early.
            var result = await _periods.CreateAsync(
                input.Name,
                new DateTimeOffset(input.OpensAt.Date, TimeSpan.Zero),
                new DateTimeOffset(input.ClosesAt.Date.AddDays(1).AddSeconds(-1), TimeSpan.Zero),
                cancellationToken);

            if (result.Succeeded)
            {
                TempData["AdminMessage"] =
                    $"Period \"{result.Round!.Name}\" created. It has no submissions yet.";

                return RedirectToAction(nameof(Periods));
            }

            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(string.Empty, problem);
            }
        }

        var view = await BuildPeriodsViewAsync(cancellationToken);
        view.NewPeriod = input;

        return View(nameof(Periods), view);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> ClosePeriod(int id, CancellationToken cancellationToken)
    {
        var result = await _periods.CloseNowAsync(id, cancellationToken);

        TempData["AdminMessage"] = result.Succeeded
            ? $"Period \"{result.Round!.Name}\" is now closed. Existing answers are kept."
            : string.Join(" ", result.Problems);

        return RedirectToAction(nameof(Periods));
    }

    private async Task<AdminPeriodsViewModel> BuildPeriodsViewAsync(CancellationToken cancellationToken)
    {
        var rounds = await _periods.GetAllAsync(cancellationToken);
        var counts = await _periods.GetSubmissionCountsAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return new AdminPeriodsViewModel
        {
            Periods = rounds
                .Select(r => new AdminPeriodsViewModel.PeriodRow(
                    r.Id,
                    r.Name,
                    r.OpensAt,
                    r.ClosesAt,
                    r.IsOpenAt(now),
                    counts.TryGetValue(r.Id, out var count) ? count : 0))
                .ToList()
        };
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
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        await _accessLog.RecordAsync(User, player.Id, "Admin/Export", cancellationToken: cancellationToken);

        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}
