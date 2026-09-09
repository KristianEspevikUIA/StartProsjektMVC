using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Security;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Eier: Kristian.
///
/// Brukere, lag og GDPR-oppgavene: innsyn (utlevering av det som er registrert om en
/// spiller) og sletting. Admin ser alt — derfor logges admin-oppslag på enkeltspillere i
/// revisjonsloggen (<see cref="IPlayerAccessLog"/>).
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConsentService _consent;
    private readonly IPeriodService _periods;
    private readonly IPlayerAccessLog _accessLog;
    private readonly UserManager<IdentityUser> _userManager;

    public AdminController(
        AppDbContext db,
        IConsentService consent,
        IPeriodService periods,
        IPlayerAccessLog accessLog,
        UserManager<IdentityUser> userManager)
    {
        _db = db;
        _consent = consent;
        _periods = periods;
        _accessLog = accessLog;
        _userManager = userManager;
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
    /// Innsyn: alt systemet har registrert om én spiller, som en nedlastbar JSON-fil.
    ///
    /// Samler Player, Guardianships, Responses med Answers, FiveCSubmissions med sine svar,
    /// hele ConsentEvent-historikken, revisjonsloggen og frigivelsene. Avviket er ikke med —
    /// det er ikke lagret, det regnes ut hver gang (se ScoringService).
    ///
    /// Oppslaget logges før dokumentet bygges. Et innsyn er nettopp den typen oppslag
    /// revisjonsloggen finnes for, og raden skal stå der også om nedlastingen ryker etterpå.
    ///
    /// Identity-ID-ene til ANDRE personer er byttet ut med pseudonymer — se
    /// <see cref="Pseudonyms"/> for hvorfor.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
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

        // Hentes ferdig sortert på Id, og hentes ut FØR pseudonymiseringen: løpenumrene under
        // skal være de samme hver gang den samme spilleren eksporteres, slik at to eksporter
        // kan sammenlignes.
        var guardianships = await _db.Guardianships
            .AsNoTracking()
            .Where(g => g.PlayerId == id)
            .OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.GuardianUserId })
            .ToListAsync(cancellationToken);

        var responses = await _db.Responses
            .AsNoTracking()
            .Where(r => r.PlayerId == id)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.RoundId,
                r.Respondent,
                r.RespondentUserId,
                r.SubmittedAt,
                Answers = r.Answers
                    .OrderBy(a => a.Id)
                    .Select(a => new { a.Id, a.ItemId, a.Value })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var submissions = await _db.FiveCSubmissions
            .AsNoTracking()
            .Where(s => s.PlayerId == id)
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.RoundId,
                s.PlayerCode,
                s.RespondentRole,
                s.RespondentUserId,
                s.QuestionSetVersion,
                s.SubmittedAt,
                Answers = s.Answers
                    .OrderBy(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.QuestionKey,
                        a.CategoryKey,
                        a.Value
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var consentEvents = await _db.ConsentEvents
            .AsNoTracking()
            .Where(c => c.PlayerId == id)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Level, c.ChangedByUserId, c.OccurredAt })
            .ToListAsync(cancellationToken);

        var accessEvents = await _db.PlayerAccessEvents
            .AsNoTracking()
            .Where(a => a.PlayerId == id)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.ViewedByUserId,
                a.ViewedByRole,
                a.Context,
                a.RoundId,
                a.OccurredAt
            })
            .ToListAsync(cancellationToken);

        var feedbackReleases = await _db.FeedbackReleases
            .AsNoTracking()
            .Where(f => f.PlayerId == id)
            .OrderBy(f => f.Id)
            .Select(f => new
            {
                f.Id,
                f.RoundId,
                f.CoachUserId,
                f.IsReleased,
                f.OccurredAt
            })
            .ToListAsync(cancellationToken);

        var people = new Pseudonyms(player.UserId);

        // Rekkefølgen HER, ikke rekkefølgen i dokumentet, bestemmer løpenumrene. Kildene som
        // vet hvilken rolle personen hadde kommer først, slik at en foresatt som også har
        // endret et samtykke blir «Guardian 1» begge steder — ConsentEvent lagrer ingen rolle
        // og ville ellers gitt den samme personen to navn.
        foreach (var g in guardianships) people.For(g.GuardianUserId, Roles.Guardian);
        foreach (var r in responses) people.For(r.RespondentUserId, r.Respondent.ToString());
        foreach (var s in submissions) people.For(s.RespondentUserId, RoleOfSubmission(s.RespondentRole));
        foreach (var f in feedbackReleases) people.For(f.CoachUserId, Roles.Coach);
        foreach (var a in accessEvents) people.For(a.ViewedByUserId, a.ViewedByRole);
        foreach (var c in consentEvents) people.For(c.ChangedByUserId, Pseudonyms.UnknownRole);

        var export = new
        {
            ExportedAt = DateTimeOffset.UtcNow,
            About = Pseudonyms.Explanation,
            Player = new
            {
                player.Id,
                player.Code,
                player.UserId,
                player.TeamId,
                player.BirthDate,
                player.Position
            },
            Guardianships = guardianships
                .Select(g => new { g.Id, Guardian = people.For(g.GuardianUserId, Roles.Guardian) })
                .ToList(),
            Responses = responses
                .Select(r => new
                {
                    r.Id,
                    r.RoundId,
                    r.Respondent,
                    AnsweredBy = people.For(r.RespondentUserId, r.Respondent.ToString()),
                    r.SubmittedAt,
                    r.Answers
                })
                .ToList(),
            FiveCSubmissions = submissions
                .Select(s => new
                {
                    s.Id,
                    s.RoundId,
                    s.PlayerCode,
                    s.RespondentRole,
                    AnsweredBy = people.For(s.RespondentUserId, RoleOfSubmission(s.RespondentRole)),
                    s.QuestionSetVersion,
                    s.SubmittedAt,
                    s.Answers
                })
                .ToList(),
            ConsentEvents = consentEvents
                .Select(c => new
                {
                    c.Id,
                    c.Level,
                    ChangedBy = people.For(c.ChangedByUserId, Pseudonyms.UnknownRole),
                    c.OccurredAt
                })
                .ToList(),
            AccessEvents = accessEvents
                .Select(a => new
                {
                    a.Id,
                    ViewedBy = people.For(a.ViewedByUserId, a.ViewedByRole),
                    a.ViewedByRole,
                    a.Context,
                    a.RoundId,
                    a.OccurredAt
                })
                .ToList(),
            FeedbackReleases = feedbackReleases
                .Select(f => new
                {
                    f.Id,
                    f.RoundId,
                    ReleasedBy = people.For(f.CoachUserId, Roles.Coach),
                    f.IsReleased,
                    f.OccurredAt
                })
                .ToList()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return File(json, "application/json", $"player-{player.Code}-export.json");
    }

    /// <summary>
    /// Bekreftelsessteget foran en sletting: hva som forsvinner, og feltet der spillerkoden
    /// skrives av for hånd.
    ///
    /// Slettingen er den ene operasjonen som fjerner ConsentEvent-rader, og den kan ikke
    /// angres. Den skal derfor koste en bevisst handling og ikke ett klikk — se
    /// <see cref="AdminDeletePlayerViewModel.ConfirmCode"/>.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var view = await BuildDeleteViewAsync(id, cancellationToken);

        return view is null ? NotFound() : View(view);
    }

    /// <summary>
    /// Sletting av en spiller og alt som hører til.
    ///
    /// Cascade i databasen tar svar, 5C-innsendinger, samtykkelogg, foresattkoblinger,
    /// revisjonslogg og frigivelser. Identity-brukeren håndteres for seg, i samme
    /// transaksjon, fordi den ligger utenfor spillerens fremmednøkler.
    ///
    /// Sporet av selve slettingen skrives til <see cref="PlayerDeletionEvent"/> og ikke til
    /// revisjonsloggen — se modellen for hvorfor.
    /// </summary>
    [HttpPost]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> DeleteConfirmed(
        int id,
        string? confirmCode,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        // Sammenligningen ser bort fra ytre mellomrom og store/små bokstaver. Koden skrives
        // av for hånd fra siden foran, og en Caps Lock-tast skal ikke koste et nytt forsøk på
        // en irreversibel handling — det som skal stanses er å treffe feil spiller, ikke å
        // skrive «ts-08-16».
        if (!string.Equals(confirmCode?.Trim(), player.Code, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(AdminDeletePlayerViewModel.ConfirmCode),
                "Type the player code exactly as it is shown above to confirm the deletion.");

            var again = await BuildDeleteViewAsync(id, cancellationToken);

            return again is null ? NotFound() : View(nameof(Delete), again);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Sporet legges inn i SAMME transaksjon som slettingen: enten skjer begge, eller
        // ingen av dem. En sletting som ruller tilbake skal ikke etterlate en kvittering på
        // noe som ikke skjedde, og en som går gjennom skal aldri mangle en.
        _db.PlayerDeletionEvents.Add(new PlayerDeletionEvent
        {
            PlayerId = player.Id,
            DeletedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            OccurredAt = DateTimeOffset.UtcNow
        });

        _db.Players.Remove(player);
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(player.UserId))
        {
            var user = await _userManager.FindByIdAsync(player.UserId);
            if (user is not null)
            {
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Could not delete the Identity account: {errors}");
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Koden, ikke et navn: det er den spilleren het i grensesnittet, og den sier
        // ingenting om hvem det var.
        TempData["AdminMessage"] =
            $"Player \"{player.Code}\" and everything held about them has been deleted. "
            + "The deletion itself is logged.";

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Tallene bekreftelsessiden viser. Null når spilleren ikke finnes, slik at både GET og
    /// POST kan svare 404 fra samme sted.
    /// </summary>
    private async Task<AdminDeletePlayerViewModel?> BuildDeleteViewAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return null;
        }

        return new AdminDeletePlayerViewModel
        {
            PlayerId = player.Id,
            PlayerCode = player.Code,
            HasAccount = !string.IsNullOrWhiteSpace(player.UserId),
            GuardianshipCount = await _db.Guardianships
                .CountAsync(g => g.PlayerId == id, cancellationToken),
            ResponseCount = await _db.Responses
                .CountAsync(r => r.PlayerId == id, cancellationToken),
            AnswerCount = await _db.Answers
                .CountAsync(a => a.Response!.PlayerId == id, cancellationToken),
            FiveCSubmissionCount = await _db.FiveCSubmissions
                .CountAsync(s => s.PlayerId == id, cancellationToken),
            ConsentEventCount = await _db.ConsentEvents
                .CountAsync(c => c.PlayerId == id, cancellationToken),
            AccessEventCount = await _db.PlayerAccessEvents
                .CountAsync(a => a.PlayerId == id, cancellationToken),
            FeedbackReleaseCount = await _db.FeedbackReleases
                .CountAsync(f => f.PlayerId == id, cancellationToken)
        };
    }

    /// <summary>
    /// FiveCSubmission lagrer rollen som wire-verdien ("player"), resten av systemet som
    /// rollenavnet ("Player"). Oversettes her, ellers får den samme personen ett pseudonym
    /// per skrivemåte.
    /// </summary>
    private static string RoleOfSubmission(string respondentRole) => respondentRole switch
    {
        Contracts.FiveC.SurveySubmission.Roles.Player => Roles.Player,
        Contracts.FiveC.SurveySubmission.Roles.Coach => Roles.Coach,
        Contracts.FiveC.SurveySubmission.Roles.Guardian => Roles.Guardian,
        _ => Pseudonyms.UnknownRole
    };

    /// <summary>
    /// Bytter Identity-ID-ene til ANDRE personer ut med «rolle + løpenummer».
    ///
    /// Et innsynsdokument handler om én spiller, men radene om den spilleren bærer
    /// identifikatorene til foresatte, trenere og administratorer. Utlevert som de er, blir
    /// dokumentet også en utlevering om dem: en Identity-GUID er en nøkkel rett inn i
    /// brukertabellen, og den som ber om innsyn har ikke krav på den.
    ///
    /// Pseudonym framfor utelatelse, fordi strukturen er en del av svaret. «Coach 1 har sett
    /// på deg fire ganger, Coach 2 én gang» er noe spilleren har krav på å kunne lese ut; med
    /// feltet fjernet ville de fem oppslagene sett like ut, og det er en dårligere utlevering
    /// enn en pseudonymisert en.
    ///
    /// Spillerens egen ID står igjen som den er. Den er spillerens egen opplysning, og dette
    /// er spillerens eget innsyn.
    /// </summary>
    private sealed class Pseudonyms
    {
        /// <summary>Rollen for en kilde som ikke lagrer hvilken rolle personen hadde.</summary>
        public const string UnknownRole = "User";

        public const string Explanation =
            "Identity user ids belonging to other people (guardians, coaches, administrators) "
            + "have been replaced by a role and a number, e.g. \"Coach 1\". The same person keeps "
            + "the same label throughout this document, and the numbering means nothing outside it.";

        private readonly Dictionary<string, string> _byUserId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _usedPerRole = new(StringComparer.Ordinal);
        private readonly string? _subjectUserId;

        public Pseudonyms(string? subjectUserId) => _subjectUserId = subjectUserId;

        /// <summary>
        /// The label for one user id. The first call decides it; a later call with a
        /// different role hint gets the label already handed out, so one person reads as
        /// one person.
        /// </summary>
        public string? For(string? userId, string role)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            if (_subjectUserId is not null
                && string.Equals(userId, _subjectUserId, StringComparison.Ordinal))
            {
                return "The player themselves";
            }

            if (_byUserId.TryGetValue(userId, out var existing))
            {
                return existing;
            }

            var next = _usedPerRole.TryGetValue(role, out var used) ? used + 1 : 1;
            _usedPerRole[role] = next;

            var label = $"{role} {next}";
            _byUserId[userId] = label;

            return label;
        }
    }
}
