using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Owner: Brage.
///
/// The player's own page: their answers, where the conversation with the coach has got to,
/// and a way into the form.
///
/// The player does NOT see the coach's answers until the coach releases them. That used to
/// be an open question in this file; it is now decided and implemented -- see
/// <see cref="IFeedbackReleaseService"/> and FiveCFeedbackBuilder, which redacts the coach's
/// figures out of the model rather than relying on the view to hide them.
/// </summary>
[Authorize(Roles = Roles.Player + "," + Roles.Admin)]
public class PlayerController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IPeriodService _periods;
    private readonly IPeriodSelection _selection;
    private readonly IFiveCFeedbackBuilder _feedback;

    public PlayerController(
        AppDbContext db,
        IAuthorizationService authz,
        IPeriodService periods,
        IPeriodSelection selection,
        IFiveCFeedbackBuilder feedback)
    {
        _db = db;
        _authz = authz;
        _periods = periods;
        _selection = selection;
        _feedback = feedback;
    }

    /// <summary>
    /// The player's own page. The player is found through Player.UserId, never through an
    /// id in the URL -- there is no parameter here to tamper with.
    /// </summary>
    public async Task<IActionResult> Index(int? roundId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (player is null)
        {
            // Not an error. A player registered by the club before they were given an
            // account has no row to show, and so does an admin looking at this page.
            return View("NoPlayer");
        }

        // Remembers the period across pages -- see IPeriodSelection.
        var round = await _selection.ResolveAsync(roundId, cancellationToken);

        if (round is null)
        {
            return View("NoPlayer");
        }

        var model = await _feedback.BuildAsync(
            player,
            round,
            await RoundOptionsAsync(cancellationToken),
            viewerIsGuardian: false,
            cancellationToken);

        return View("FiveCFeedback", model);
    }

    /// <summary>
    /// Kept for the older ten-statement form. The 5C picture lives on <see cref="Index"/>.
    /// </summary>
    public async Task<IActionResult> MyResponses(int id, int? roundId, CancellationToken cancellationToken)
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

        return RedirectToAction(nameof(Index), new { roundId });
    }

    private async Task<IReadOnlyList<FiveCTeamViewModel.RoundOption>> RoundOptionsAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return (await _periods.GetAllAsync(cancellationToken))
            .Select(r => new FiveCTeamViewModel.RoundOption(r.Id, r.Name, r.IsOpenAt(now)))
            .ToList();
    }
}
