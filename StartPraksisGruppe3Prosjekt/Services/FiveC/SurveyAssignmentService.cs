using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <inheritdoc cref="ISurveyAssignmentService" />
public sealed class SurveyAssignmentService : ISurveyAssignmentService
{
    private readonly AppDbContext _db;
    private readonly ISurveySubmissionStore _store;

    public SurveyAssignmentService(AppDbContext db, ISurveySubmissionStore store)
    {
        _db = db;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveyAssignment>> GetAssignmentsAsync(
        ClaimsPrincipal user,
        int roundId,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Array.Empty<SurveyAssignment>();
        }

        // A player who is also a guardian gets one entry per role, so the union is keyed on
        // (player, role) rather than on player alone.
        var assignments = new Dictionary<(int PlayerId, RespondentType Role), Player>();

        void Add(Player player, RespondentType role) => assignments[(player.Id, role)] = player;

        // The player themselves. Found through Player.UserId, never through an id in a URL.
        var self = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (self is not null)
        {
            Add(self, RespondentType.Player);
        }

        // Children, for a guardian. The role alone grants nothing -- the Guardianship rows do.
        //
        // Queried from Players rather than from Guardianships, because Include has to come
        // before any Select that projects to a different entity: going through Guardianships
        // and selecting g.Player leaves EF with no queryable root to hang Include on.
        var childPlayers = await _db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .Where(p => p.Guardianships.Any(g => g.GuardianUserId == userId))
            .ToListAsync(cancellationToken);

        foreach (var child in childPlayers)
        {
            Add(child, RespondentType.Guardian);
        }

        // The coach's own teams, from CoachTeam. A coach is not a coach for everyone.
        var coachedTeamIds = await _db.CoachTeams
            .AsNoTracking()
            .Where(ct => ct.CoachUserId == userId)
            .Select(ct => ct.TeamId)
            .ToListAsync(cancellationToken);

        if (coachedTeamIds.Count > 0)
        {
            var teamPlayers = await _db.Players
                .AsNoTracking()
                .Include(p => p.Team)
                .Where(p => coachedTeamIds.Contains(p.TeamId))
                .ToListAsync(cancellationToken);

            foreach (var player in teamPlayers)
            {
                Add(player, RespondentType.Coach);
            }
        }

        if (assignments.Count == 0)
        {
            return Array.Empty<SurveyAssignment>();
        }

        // One read for every player at once. One call per player would be N+1 requests
        // against Supabase, which is a network round trip each.
        var playerIds = assignments.Keys.Select(k => k.PlayerId).Distinct().ToList();

        var submissions = await _store.GetForPlayersAsync(roundId, playerIds, cancellationToken);

        var submittedAt = submissions
            .Where(s => s.RespondentUserId == userId)
            .ToDictionary(
                s => (s.PlayerId, Contracts.FiveC.SurveySubmission.Roles.To(s.RespondentRole)),
                s => s.SubmittedAt);

        return assignments
            .Select(entry =>
            {
                var (key, player) = entry;

                return new SurveyAssignment(
                    PlayerId: player.Id,
                    PlayerCode: player.Code,
                    TeamName: player.Team?.Name ?? string.Empty,
                    Role: key.Role,
                    IsAboutSelf: key.Role == RespondentType.Player,
                    SubmittedAt: submittedAt.TryGetValue(key, out var at) ? at : null);
            })
            // Your own form first, then children, then the team -- and player code within
            // each group, so a coach with twenty players gets a stable list.
            .OrderBy(a => a.Role switch
            {
                RespondentType.Player => 0,
                RespondentType.Guardian => 1,
                _ => 2
            })
            .ThenBy(a => a.PlayerCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RespondentType>> GetAllowedRolesAsync(
        ClaimsPrincipal user,
        Player player,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Array.Empty<RespondentType>();
        }

        var roles = new List<RespondentType>();

        // Ordered by how directly the user is involved with the player. A link that does not
        // name a role gets the first of these, which is the least surprising default.
        if (player.UserId is not null && player.UserId == userId)
        {
            roles.Add(RespondentType.Player);
        }

        if (user.IsInRole(Roles.Guardian))
        {
            var isGuardian = await _db.Guardianships
                .AnyAsync(g => g.PlayerId == player.Id && g.GuardianUserId == userId, cancellationToken);

            if (isGuardian)
            {
                roles.Add(RespondentType.Guardian);
            }
        }

        if (user.IsInRole(Roles.Coach))
        {
            var coachesTeam = await _db.CoachTeams
                .AnyAsync(ct => ct.TeamId == player.TeamId && ct.CoachUserId == userId, cancellationToken);

            if (coachesTeam)
            {
                roles.Add(RespondentType.Coach);
            }
        }

        // Note that Admin is absent on purpose. An admin can see the answers; letting one
        // submit answers ABOUT a minor, in someone else's name, is a different thing, and
        // nobody has asked for it.

        return roles;
    }
}
