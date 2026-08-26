using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Owner: Brage.
///
/// A guardian sees their own child, and only that. The role grants nothing on its own --
/// the link has to exist in Guardianship, which is what CanViewPlayer checks.
///
/// A guardian sees exactly what the player sees, including when the coach's answers are
/// released. Same page, same redaction: <see cref="IFiveCFeedbackBuilder"/> builds both.
/// </summary>
[Authorize(Roles = Roles.Guardian + "," + Roles.Admin)]
public class GuardianController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IConsentService _consent;
    private readonly IPeriodService _periods;
    private readonly IPeriodSelection _selection;
    private readonly IFiveCFeedbackBuilder _feedback;

    public GuardianController(
        AppDbContext db,
        IAuthorizationService authz,
        IConsentService consent,
        IPeriodService periods,
        IPeriodSelection selection,
        IFiveCFeedbackBuilder feedback)
    {
        _db = db;
        _authz = authz;
        _consent = consent;
        _periods = periods;
        _selection = selection;
        _feedback = feedback;
    }

    /// <summary>
    /// The children this guardian is registered on. One child goes straight to their page --
    /// a list of one is a click that asks nothing.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var children = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .Where(p => p.Guardianships.Any(g => g.GuardianUserId == userId))
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        if (children.Count == 1)
        {
            return RedirectToAction(nameof(Player), new { id = children[0].Id });
        }

        return View(children);
    }

    /// <summary>One child's 5C page. Same view the player gets.</summary>
    public async Task<IActionResult> Player(int id, int? roundId, CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        var authorized = await _authz.AuthorizeAsync(User, player, Policies.CanViewPlayer);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var round = await _selection.ResolveAsync(roundId, cancellationToken);

        if (round is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;

        var rounds = (await _periods.GetAllAsync(cancellationToken))
            .Select(r => new FiveCTeamViewModel.RoundOption(r.Id, r.Name, r.IsOpenAt(now)))
            .ToList();

        var model = await _feedback.BuildAsync(
            player,
            round,
            rounds,
            viewerIsGuardian: true,
            cancellationToken);

        return View("FiveCFeedback", model);
    }

    /// <summary>
    /// The consent picture with its history.
    /// TODO (Brage): build ConsentViewModel from GetCurrentLevelAsync + GetHistoryAsync.
    /// </summary>
    [HttpGet]
    public IActionResult Consent(int id)
    {
        return View();
    }

    /// <summary>
    /// Changing consent.
    /// TODO (Brage): call IConsentService.RecordAsync -- it adds a NEW event. No row is
    /// edited or deleted; AppDbContext throws if you try.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Consent(int id, ConsentLevel level)
    {
        return RedirectToAction(nameof(Consent), new { id });
    }
}
