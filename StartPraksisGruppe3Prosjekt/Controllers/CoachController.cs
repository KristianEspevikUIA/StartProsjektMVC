using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.ViewModels;

namespace StartPraksisGruppe3Prosjekt.Controllers;

/// <summary>
/// Eier: Taavi.
///
/// [Authorize(Roles = ...)] slipper deg bare inn i controlleren. Den sier ingenting om
/// HVILKE spillere du får se. Hver action som tar imot en spiller-ID må i tillegg kjøre
/// ressurssjekken — se <see cref="PlayerDetail"/>, som er mønsteret alle skal følge.
/// </summary>
[Authorize(Roles = Roles.Coach + "," + Roles.Admin)]
public class CoachController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly IScoringService _scoring;
    private readonly IConsentService _consent;

    public CoachController(
        AppDbContext db,
        IAuthorizationService authz,
        IScoringService scoring,
        IConsentService consent)
    {
        _db = db;
        _authz = authz;
        _scoring = scoring;
        _consent = consent;
    }

    /// <summary>Trenerens lag. TODO (Taavi): list lagene fra CoachTeam for innlogget bruker.</summary>
    public IActionResult Index()
    {
        // TODO (Taavi): hent CoachTeams for innlogget bruker og vis lagene.
        return View();
    }

    /// <summary>
    /// Lagoversikt med aggregert snitt.
    /// TODO (Taavi): bygg TeamOverviewViewModel. Aggregatet skal bare vises hvis
    /// CanViewTeamAggregate sier ja — og policyen krever at du oppgir antall besvarelser,
    /// se <see cref="TeamAggregateResource"/>.
    /// </summary>
    public async Task<IActionResult> Team(int id, int? roundId)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id);
        if (team is null)
        {
            return NotFound();
        }

        // Samme mønster som PlayerDetail: rollen slapp deg inn i controlleren, policyen
        // avgjør om nettopp DETTE laget er ditt. Uten denne kunne en trener bla gjennom
        // lag-ID-er og få bekreftet hvilke lag som finnes.
        var teamAllowed = await _authz.AuthorizeAsync(User, team, Policies.CanViewTeam);
        if (!teamAllowed.Succeeded)
        {
            return Forbid();
        }

        // TODO (Taavi): tell faktiske besvarelser for laget i runden i stedet for 0.
        var responseCount = 0;

        var aggregateAllowed = await _authz.AuthorizeAsync(
            User,
            new TeamAggregateResource(team, responseCount),
            Policies.CanViewTeamAggregate);

        var model = new TeamOverviewViewModel
        {
            TeamId = team.Id,
            TeamName = team.Name
        };

        if (aggregateAllowed.Succeeded)
        {
            // TODO (Taavi): model.Aggregate = await _scoring.GetTeamAggregateAsync(...)
        }
        else
        {
            ViewData["AggregateMessage"] =
                $"For få besvarelser til å vise lagsnitt (minst " +
                $"{CanViewTeamAggregateRequirement.MinimumResponses} kreves).";
        }

        return View(model);
    }

    /// <summary>
    /// Spillerdetalj.
    ///
    /// DETTE ER MØNSTERET. Alle actions som tar imot en spiller-ID skal se slik ut:
    /// hent spilleren, spør policyen, og returner Forbid() hvis svaret er nei.
    /// Ikke sjekk rolle eller lag for hånd i controlleren — reglene bor i
    /// CanViewPlayerHandler, ett sted, slik at de kan endres uten å lete.
    /// </summary>
    public async Task<IActionResult> PlayerDetail(int id, int? roundId)
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
            // Forbid, ikke NotFound: brukeren er innlogget, men har ikke tilgang hit.
            return Forbid();
        }

        var model = new PlayerDetailViewModel
        {
            PlayerId = player.Id,
            Code = player.Code,
            Position = player.Position,
            TeamName = player.Team?.Name ?? string.Empty,
            Consent = await _consent.GetCurrentLevelAsync(player.Id)
        };

        // TODO (Taavi): hent runden, påstandene og
        // model.Gap = await _scoring.GetPlayerGapAsync(roundId, player.Id);
        // Avviket regnes ut her og lagres aldri.

        return View(model);
    }

    /// <summary>
    /// Søk etter spiller på kode.
    /// TODO (Taavi): treff skal filtreres med CanViewPlayer før de vises — et søk som
    /// bekrefter at en spiller finnes, er også en opplysning.
    /// </summary>
    public IActionResult Search(string? q)
    {
        return View();
    }
}
