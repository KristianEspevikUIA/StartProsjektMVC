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
    private readonly IPlayerWelcomeService _welcome;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext db,
        IConsentService consent,
        IPeriodService periods,
        IPlayerAccessLog accessLog,
        UserManager<IdentityUser> userManager,
        IPlayerWelcomeService welcome,
        ILogger<AdminController> logger)
    {
        _db = db;
        _consent = consent;
        _periods = periods;
        _accessLog = accessLog;
        _userManager = userManager;
        _welcome = welcome;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    // -----------------------------------------------------------------------------------
    // Navn og bilde til velkomsten
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Alle spillerne, med om fornavn og bilde er lagt inn -- og veien videre til skjemaet,
    /// innsyn og sletting for hver av dem. Den siste delen er det «plukk en spiller»-trinnet
    /// Admin-forsiden har manglet: innsyn og sletting fantes, men bare for den som visste ID-en.
    ///
    /// Lista navngir spillerne der fornavn er lagt inn. Det er admin, på siden der navnene
    /// legges inn, og ikke logget per spiller -- det er ingen svar, vurderinger eller bilder her.
    /// </summary>
    public async Task<IActionResult> Players(CancellationToken cancellationToken) =>
        View(await _welcome.ListAsync(cancellationToken));

    /// <summary>Skjemaet for én spillers fornavn og bilde.</summary>
    [HttpGet]
    public async Task<IActionResult> PlayerDetails(int id, CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        // Et navn og et bilde av en mindreårig er nettopp den typen oppslag loggen finnes for.
        await _accessLog.RecordAsync(User, id, "Admin/PlayerDetails", cancellationToken: cancellationToken);

        var summary = await _welcome.GetSummaryAsync(id, cancellationToken);

        return View(AdminPlayerDetailsViewModel.For(player, summary));
    }

    /// <summary>
    /// Lagrer fornavn og kilde, og bytter eller fjerner bildet.
    ///
    /// Bildet sjekkes og renses av <see cref="PlayerPhotoRules"/> før noe lagres: formatet leses
    /// av filens egne bytes, og metadata som GPS og bildetekst tas ut. Et bilde som ikke går
    /// gjennom, gir skjemaet tilbake med beskjeden -- og ingenting av skjemaet lagres, slik at
    /// admin ikke tror et nytt navn er på plass når bare halve innsendingen gikk gjennom.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [RequestSizeLimit(PlayerPhotoRules.MaxBytes + 256 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = PlayerPhotoRules.MaxBytes + 256 * 1024)]
    public async Task<IActionResult> PlayerDetails(
        int id,
        AdminPlayerDetailsViewModel form,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (player is null)
        {
            return NotFound();
        }

        PhotoCheck? checkedPhoto = null;

        if (photo is { Length: > 0 })
        {
            if (photo.Length > PlayerPhotoRules.MaxBytes)
            {
                ModelState.AddModelError(nameof(photo), $"The photo is too large. The largest that can be used is {PlayerPhotoRules.MaxBytes / 1024 / 1024} MB.");
            }
            else
            {
                using var buffer = new MemoryStream((int)photo.Length);
                await photo.CopyToAsync(buffer, cancellationToken);

                checkedPhoto = PlayerPhotoRules.Prepare(buffer.ToArray());

                if (!checkedPhoto.IsAccepted)
                {
                    ModelState.AddModelError(nameof(photo), checkedPhoto.Error!);
                }
            }
        }

        if (!ModelState.IsValid)
        {
            var again = AdminPlayerDetailsViewModel.For(player, await _welcome.GetSummaryAsync(id, cancellationToken));
            again.FirstName = form.FirstName;
            again.PhotoSource = form.PhotoSource;

            return View(again);
        }

        await _welcome.SaveAsync(
            id,
            form.FirstName,
            form.PhotoSource,
            checkedPhoto,
            form.RemovePhoto,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            cancellationToken);

        TempData["AdminMessage"] = $"Name and photo for {player.Code} are saved.";

        return RedirectToAction(nameof(PlayerDetails), new { id });
    }

    /// <summary>
    /// Bildet slik det er lagret, til forhåndsvisningen i skjemaet. Ikke lagret i noen cache:
    /// dette er admin på en delt maskin, og bildet skal ikke bli liggende etter at siden er lukket.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PlayerPhoto(int id, CancellationToken cancellationToken)
    {
        if (await _welcome.GetPhotoAsync(id, cancellationToken) is not { } photo)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";

        return File(photo.Bytes, photo.ContentType);
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

    // -----------------------------------------------------------------------------------
    // Kontoer: hvem som kan logge inn
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Kontoene, og skjemaet for en ny stabskonto. Det er her en trener får tilgang: appen har
    /// ingen e-post, så et passord gis ut av en administrator og byttes av eieren ved første
    /// innlogging. Se <see cref="AccountRules"/>.
    ///
    /// Staben vises først. Spillerne og de foresatte er mange, og har hver sin fane.
    /// Adressene navngir spillerne; det er admin, på siden der kontoene styres, og ikke logget
    /// per spiller -- det er ingen svar, vurderinger eller bilder her.
    ///
    /// Ikke bygget ennå: å gi og fjerne roller, og å opprette spillere og foresatte her. Det
    /// siste må kreve minst én foresatt for en spiller under 19 -- se PlayerRules.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Users(string? show, CancellationToken cancellationToken) =>
        View(await BuildAccountsViewAsync(show, new NewAccountForm(), cancellationToken));

    /// <summary>
    /// En ny trener- eller administratorkonto. Uten passord og låst: den kan ikke logge inn før
    /// en administrator gir den tilgang på kontosiden, og det er et eget, bevisst trykk.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> CreateAccount(
        [Bind(Prefix = nameof(AdminAccountsViewModel.New))] NewAccountForm form,
        CancellationToken cancellationToken)
    {
        const string emailField = nameof(AdminAccountsViewModel.New) + "." + nameof(NewAccountForm.Email);
        const string roleField = nameof(AdminAccountsViewModel.New) + "." + nameof(NewAccountForm.Role);

        var email = form.Email?.Trim() ?? string.Empty;

        if (form.Role is not (Roles.Coach or Roles.Admin))
        {
            ModelState.AddModelError(roleField, "Choose Coach or Administrator.");
        }

        if (ModelState.IsValid && await _userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(emailField, "There is already an account with that address.");
        }

        if (ModelState.IsValid)
        {
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true,
                LockoutEnd = DateTimeOffset.MaxValue
            };

            var result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                result = await _userManager.AddToRoleAsync(user, form.Role);
            }

            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "An administrator created the {Role} account {AccountId}.", form.Role, user.Id);

                TempData["AdminMessage"] = $"The account {email} is created. It cannot sign in until you give it access.";

                return RedirectToAction(nameof(Account), new { id = user.Id });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(emailField, error.Description);
            }
        }

        return View(nameof(Users), await BuildAccountsViewAsync(AdminAccountsViewModel.Staff, form, cancellationToken));
    }

    /// <summary>
    /// Én konto, og om den kan logge inn. Ingen lag her: en trener er trener for hele klubben,
    /// ikke for et bestemt lag -- se CanViewTeamHandler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Account(string id, CancellationToken cancellationToken)
    {
        var rows = await LoadAccountsAsync(cancellationToken);
        var account = rows.FirstOrDefault(r => r.Id == id);

        if (account is null)
        {
            return NotFound();
        }

        return View(new AdminAccountViewModel
        {
            Account = account,
            IsSelf = id == CurrentUserId
        });
    }

    /// <summary>
    /// Gir kontoen et midlertidig passord og åpner den. Passordet vises på siden som kommer
    /// tilbake, og bare der: det lagres ikke lesbart noe sted, logges ikke, og siden ber
    /// nettleseren om ikke å ta vare på den. Kontoen merkes slik at eieren må velge sitt eget
    /// ved første innlogging (<see cref="AccountRules.MustChangePasswordClaim"/>).
    ///
    /// Å sette passordet bytter også sikkerhetsstempelet, så en økt som alt er åpen på kontoen,
    /// faller ut. Svaret er siden selv og ikke en omdirigering -- ellers måtte passordet lagt
    /// seg i en cookie på veien.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> AccountPassword(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == CurrentUserId)
        {
            TempData["AdminMessage"] = "This is your own account. Change your password from the account menu.";
            return RedirectToAction(nameof(Account), new { id });
        }

        var temporary = AccountRules.NewTemporaryPassword();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, temporary);

        if (!result.Succeeded)
        {
            TempData["AdminMessage"] = "The password could not be set: " +
                                       string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Account), new { id });
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        if ((await _userManager.GetClaimsAsync(user)).All(c => c.Type != AccountRules.MustChangePasswordClaim))
        {
            await _userManager.AddClaimAsync(user, new Claim(AccountRules.MustChangePasswordClaim, bool.TrueString));
        }

        _logger.LogInformation("An administrator gave account {AccountId} a temporary password.", user.Id);

        Response.Headers.CacheControl = "no-store";

        return View(new AdminAccountPasswordViewModel(user.Id, user.Email ?? string.Empty, temporary));
    }

    /// <summary>
    /// Låser kontoen. Sikkerhetsstempelet byttes, så en åpen økt faller ut innen noen minutter
    /// (ValidationInterval i Program.cs), ikke først når cookien går ut.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> AccountLock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == CurrentUserId)
        {
            TempData["AdminMessage"] = "You cannot lock the account you are signed in with.";
            return RedirectToAction(nameof(Account), new { id });
        }

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateSecurityStampAsync(user);

        _logger.LogInformation("An administrator locked account {AccountId}.", user.Id);

        TempData["AdminMessage"] = $"{user.Email} is locked and cannot sign in.";

        return RedirectToAction(nameof(Account), new { id });
    }

    /// <summary>Åpner en låst konto som alt har et passord. En uten får tilgang med AccountPassword.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    public async Task<IActionResult> AccountUnlock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (!await _userManager.HasPasswordAsync(user))
        {
            TempData["AdminMessage"] = "This account has no password. Give it access with a temporary password instead.";
            return RedirectToAction(nameof(Account), new { id });
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("An administrator unlocked account {AccountId}.", user.Id);

        TempData["AdminMessage"] = $"{user.Email} can sign in again.";

        return RedirectToAction(nameof(Account), new { id });
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task<AdminAccountsViewModel> BuildAccountsViewAsync(
        string? show,
        NewAccountForm form,
        CancellationToken cancellationToken)
    {
        var rows = await LoadAccountsAsync(cancellationToken);

        var staff = rows.Where(r => r.Roles.Contains(Roles.Admin) || r.Roles.Contains(Roles.Coach)).ToList();
        var players = rows.Where(r => r.Roles.Contains(Roles.Player)).ToList();
        var guardians = rows.Where(r => r.Roles.Contains(Roles.Guardian)).ToList();

        var chosen = show?.Trim().ToLowerInvariant() switch
        {
            AdminAccountsViewModel.Players => AdminAccountsViewModel.Players,
            AdminAccountsViewModel.Guardians => AdminAccountsViewModel.Guardians,
            _ => AdminAccountsViewModel.Staff
        };

        return new AdminAccountsViewModel
        {
            Show = chosen,
            Accounts = chosen switch
            {
                AdminAccountsViewModel.Players => players,
                AdminAccountsViewModel.Guardians => guardians,
                _ => staff
            },
            StaffCount = staff.Count,
            PlayerCount = players.Count,
            GuardianCount = guardians.Count,
            New = form
        };
    }

    /// <summary>
    /// Hver konto med roller og om den kan logge inn. Tre spørringer for hele lista, og
    /// aldri passordhashen ut av basen -- bare om den finnes.
    /// </summary>
    private async Task<List<AdminAccountRow>> LoadAccountsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.Email, u.UserName, u.LockoutEnd, HasPassword = u.PasswordHash != null })
            .ToListAsync(cancellationToken);

        var roles = (await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken))
            .ToLookup(r => r.UserId, r => r.Name ?? string.Empty);

        var mustChange = (await _db.UserClaims
                .Where(c => c.ClaimType == AccountRules.MustChangePasswordClaim)
                .Select(c => c.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return users
            .Select(u => new AdminAccountRow
            {
                Id = u.Id,
                Email = u.Email ?? u.UserName ?? u.Id,
                Roles = roles[u.Id].OrderBy(r => r, StringComparer.Ordinal).ToList(),
                HasPassword = u.HasPassword,
                IsLocked = u.LockoutEnd is { } end && end > now,
                MustChangePassword = mustChange.Contains(u.Id)
            })
            .OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
    /// Samler Player, Guardianships, Responses med Answers, FiveCSubmissions med sine svar
    /// og sin refleksjon, hele ConsentEvent-historikken, revisjonsloggen, frigivelsene,
    /// trenernes succession-vurderinger med kontraktsopplysningene, og fornavn og bilde til
    /// velkomsten. Avviket er ikke med — det er ikke lagret, det regnes ut hver gang (se
    /// ScoringService).
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
                    .ToList(),
                // Fritekst om spilleren. Den er noe av det mest identifiserende systemet
                // har, og nettopp derfor det et innsyn ikke kan hoppe over.
                Reflection = s.Reflection
                    .OrderBy(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.QuestionKey,
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

        // Trenernes vurderinger i succession planning. Vises aldri for spilleren i appen --
        // de er stabens arbeidsvurderinger -- men de er opplysninger om spilleren, og et innsyn
        // som hoppet over dem ville vært et innsyn med et hull i. Fritekstene er med av samme
        // grunn som refleksjonen.
        var successionAssessments = await _db.SuccessionAssessments
            .AsNoTracking()
            .Where(a => a.PlayerId == id)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.RaterUserId,
                a.CycleStartsOn,
                a.CatalogVersion,
                a.UpdatedAt,
                a.RatedAs,
                a.AbilityCategory,
                a.FirstPosition,
                a.SecondPosition,
                a.ThirdPosition,
                a.PersonalReadiness,
                a.PersonalReadinessNote,
                a.Projection0To6Months,
                a.Projection6To18Months,
                a.Projection18To36Months,
                a.PathwayBlocked,
                a.PathwayBlockedNote,
                a.WhatNow,
                a.SuccessionRisk,
                a.SuccessionRiskNote,
                a.ExternalNeeded,
                a.ExternalNeededNote,
                a.KeyDevelopmentFocus,
                a.SuperStrengths,
                a.Notes,
                Ratings = a.Ratings
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.RatingKey, r.Value })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var successionProfile = await _db.PlayerSuccessionProfiles
            .AsNoTracking()
            .Where(p => p.PlayerId == id)
            .Select(p => new
            {
                p.ContractType,
                p.ContractEndsOn,
                p.TrainingGroup,
                p.UpdatedByUserId,
                p.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Fornavn og bilde til velkomsten. Bildet er med som det er lagret (base64 i JSON-fila):
        // det er en opplysning om spilleren som alt annet, og et innsyn som sa «det finnes et
        // bilde» uten å vise det, ville ikke vært et svar.
        var personalDetails = await _db.PlayerPersonalDetails
            .AsNoTracking()
            .Where(d => d.PlayerId == id)
            .Select(d => new
            {
                d.FirstName,
                d.PhotoSource,
                d.PhotoContentType,
                d.PhotoUpdatedAt,
                d.Photo,
                d.UpdatedByUserId,
                d.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        var people = new Pseudonyms(player.UserId);

        // Rekkefølgen HER, ikke rekkefølgen i dokumentet, bestemmer løpenumrene. Kildene som
        // vet hvilken rolle personen hadde kommer først, slik at en foresatt som også har
        // endret et samtykke blir «Guardian 1» begge steder — ConsentEvent lagrer ingen rolle
        // og ville ellers gitt den samme personen to navn.
        foreach (var g in guardianships) people.For(g.GuardianUserId, Roles.Guardian);
        foreach (var r in responses) people.For(r.RespondentUserId, r.Respondent.ToString());
        foreach (var s in submissions) people.For(s.RespondentUserId, RoleOfSubmission(s.RespondentRole));
        foreach (var f in feedbackReleases) people.For(f.CoachUserId, Roles.Coach);
        foreach (var a in successionAssessments) people.For(a.RaterUserId, Roles.Coach);
        foreach (var a in accessEvents) people.For(a.ViewedByUserId, a.ViewedByRole);
        if (successionProfile is not null) people.For(successionProfile.UpdatedByUserId, Pseudonyms.UnknownRole);
        if (personalDetails is not null) people.For(personalDetails.UpdatedByUserId, Roles.Admin);
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
                    s.Answers,
                    s.Reflection
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
                .ToList(),
            SuccessionAssessments = successionAssessments
                .Select(a => new
                {
                    a.Id,
                    RatedBy = people.For(a.RaterUserId, Roles.Coach),
                    a.CycleStartsOn,
                    a.CatalogVersion,
                    a.UpdatedAt,
                    a.RatedAs,
                    a.AbilityCategory,
                    a.FirstPosition,
                    a.SecondPosition,
                    a.ThirdPosition,
                    a.Ratings,
                    a.PersonalReadiness,
                    a.PersonalReadinessNote,
                    a.Projection0To6Months,
                    a.Projection6To18Months,
                    a.Projection18To36Months,
                    a.PathwayBlocked,
                    a.PathwayBlockedNote,
                    a.WhatNow,
                    a.SuccessionRisk,
                    a.SuccessionRiskNote,
                    a.ExternalNeeded,
                    a.ExternalNeededNote,
                    a.KeyDevelopmentFocus,
                    a.SuperStrengths,
                    a.Notes
                })
                .ToList(),
            SuccessionProfile = successionProfile is null
                ? null
                : new
                {
                    successionProfile.ContractType,
                    successionProfile.ContractEndsOn,
                    successionProfile.TrainingGroup,
                    UpdatedBy = people.For(successionProfile.UpdatedByUserId, Pseudonyms.UnknownRole),
                    successionProfile.UpdatedAt
                },
            PersonalDetails = personalDetails is null
                ? null
                : new
                {
                    personalDetails.FirstName,
                    personalDetails.PhotoSource,
                    personalDetails.PhotoContentType,
                    personalDetails.PhotoUpdatedAt,
                    Photo = personalDetails.Photo,
                    UpdatedBy = people.For(personalDetails.UpdatedByUserId, Roles.Admin),
                    personalDetails.UpdatedAt
                }
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return File(json, "application/json", $"player-{player.Code}-export.json");
    }

    /// <summary>
    /// Bekreftelsessteget foran en sletting: hva som forsvinner, og feltet der spillerens navn
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
    /// revisjonslogg, frigivelser, succession-vurderingene med kontraktsopplysningene, og
    /// fornavn og bilde til velkomsten.
    /// Identity-brukeren håndteres for seg, i samme transaksjon, fordi den ligger utenfor
    /// spillerens fremmednøkler.
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

        // Sammenligningen ser bort fra ytre mellomrom og store/små bokstaver. Navnet skrives
        // av for hånd fra siden foran, og en Caps Lock-tast skal ikke koste et nytt forsøk på
        // en irreversibel handling — det som skal stanses er å treffe feil spiller, ikke å
        // skrive «viljar holm».
        if (!string.Equals(confirmCode?.Trim(), player.Code, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(AdminDeletePlayerViewModel.ConfirmCode),
                "Type the player's name exactly as it is shown above to confirm the deletion.");

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
            ReflectionAnswerCount = await _db.FiveCReflectionAnswers
                .CountAsync(a => a.Submission!.PlayerId == id, cancellationToken),
            ConsentEventCount = await _db.ConsentEvents
                .CountAsync(c => c.PlayerId == id, cancellationToken),
            AccessEventCount = await _db.PlayerAccessEvents
                .CountAsync(a => a.PlayerId == id, cancellationToken),
            FeedbackReleaseCount = await _db.FeedbackReleases
                .CountAsync(f => f.PlayerId == id, cancellationToken),
            SuccessionAssessmentCount = await _db.SuccessionAssessments
                .CountAsync(a => a.PlayerId == id, cancellationToken),
            HasSuccessionProfile = await _db.PlayerSuccessionProfiles
                .AnyAsync(p => p.PlayerId == id, cancellationToken),
            HasPersonalDetails = await _db.PlayerPersonalDetails
                .AnyAsync(d => d.PlayerId == id, cancellationToken)
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
