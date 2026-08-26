using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Development fallback for <see cref="ISurveySubmissionStore"/>, used while Supabase is
/// not configured. Answers live in memory and are gone when the process stops.
///
/// It exists so the form and the coach overview can be built and demonstrated before
/// Victor's tables are in place, not as a stepping stone to a local database. Nothing here
/// should grow into one -- when Supabase is configured this class is not registered at all.
///
/// In Development it seeds itself with MADE-UP submissions for the seeded players, so the
/// coach overview has something to draw. All of it is invented, including the player who
/// scores low enough to raise the follow-up flag. No real answers pass through here.
/// </summary>
public sealed class InMemorySurveySubmissionStore : ISurveySubmissionStore
{
    private readonly ConcurrentDictionary<SubmissionKey, SurveySubmission> _submissions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IQuestionCatalog _catalog;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<InMemorySurveySubmissionStore> _logger;
    private readonly SemaphoreSlim _seedLock = new(1, 1);

    private bool _seeded;

    public InMemorySurveySubmissionStore(
        IServiceScopeFactory scopeFactory,
        IQuestionCatalog catalog,
        IHostEnvironment environment,
        ILogger<InMemorySurveySubmissionStore> logger)
    {
        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Description => "In-memory (development only, not saved)";

    /// <inheritdoc />
    public async Task SaveAsync(SurveySubmission submission, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var key = new SubmissionKey(
            submission.RoundId,
            submission.PlayerId,
            submission.RespondentUserId);

        // Replace, never append: one submission per person, per player, per round.
        _submissions[key] = submission;
    }

    /// <inheritdoc />
    public async Task<SurveySubmission?> FindAsync(
        int roundId,
        int playerId,
        string respondentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        return _submissions.TryGetValue(
            new SubmissionKey(roundId, playerId, respondentUserId),
            out var submission)
            ? submission
            : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        return _submissions.Values
            .Where(s => s.RoundId == roundId && s.PlayerId == playerId)
            .OrderBy(s => s.RespondentRole, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var wanted = playerIds.ToHashSet();

        return _submissions.Values
            .Where(s => s.RoundId == roundId && wanted.Contains(s.PlayerId))
            .ToList();
    }

    /// <summary>
    /// Fills the store with invented submissions the first time it is read, so the coach
    /// overview is not empty on a fresh checkout. Outside Development it does nothing --
    /// an empty overview is the honest answer there.
    /// </summary>
    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (_seeded)
        {
            return;
        }

        await _seedLock.WaitAsync(cancellationToken);
        try
        {
            if (_seeded)
            {
                return;
            }

            _seeded = true;

            if (!_environment.IsDevelopment())
            {
                return;
            }

            await SeedDemoSubmissionsAsync(cancellationToken);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private async Task SeedDemoSubmissionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        var round = await db.SurveyRounds
            .AsNoTracking()
            .Where(r => r.OpensAt <= now && r.ClosesAt >= now)
            .OrderBy(r => r.ClosesAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (round is null)
        {
            _logger.LogInformation("No open round; skipping made-up 5C submissions.");
            return;
        }

        var players = await db.Players
            .AsNoTracking()
            .Include(p => p.Guardianships)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
        {
            // The player answers about themselves, if they have an account at all.
            if (player.UserId is { } playerUserId)
            {
                Add(round.Id, player, RespondentType.Player, playerUserId);
            }

            // One guardian per player, where there is one.
            if (player.Guardianships.FirstOrDefault() is { } guardianship)
            {
                Add(round.Id, player, RespondentType.Guardian, guardianship.GuardianUserId);
            }

            // A coach for the team, so there is something to compare against.
            var coachUserId = await db.CoachTeams
                .AsNoTracking()
                .Where(ct => ct.TeamId == player.TeamId)
                .Select(ct => ct.CoachUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (coachUserId is not null)
            {
                Add(round.Id, player, RespondentType.Coach, coachUserId);
            }
        }

        _logger.LogInformation(
            "Seeded {Count} MADE-UP 5C submissions for round '{Round}'. Development only.",
            _submissions.Count,
            round.Name);
    }

    /// <summary>
    /// Builds one invented submission. The values are derived from the player id and the
    /// role, so a restart gives the same numbers and the screenshots stay comparable.
    /// </summary>
    private void Add(int roundId, Player player, RespondentType respondent, string userId)
    {
        var random = new Random(HashCode.Combine(player.Id, (int)respondent));

        // Every third player is given a weak category, so the follow-up flag in the coach
        // overview has something to fire on. Which category rotates with the player id.
        var weakCategoryIndex = player.Id % 3 == 0
            ? player.Id % _catalog.Questions.Categories.Count
            : -1;

        var answers = new List<SurveyAnswer>();
        var categories = _catalog.Questions.Categories;

        for (var c = 0; c < categories.Count; c++)
        {
            var category = categories[c];
            var isWeak = c == weakCategoryIndex && respondent == RespondentType.Player;

            foreach (var question in category.Questions)
            {
                // A weak category lands on scores of 1-2 after reversal; everything else
                // sits in the ordinary 3-5 range with a bit of spread between roles.
                var score = isWeak
                    ? random.Next(1, 3)
                    : random.Next(3, 6);

                // The store keeps raw answers, so a reversed statement has to be turned
                // back before it is written -- the same rule, applied in the other direction.
                var raw = question.Reversed
                    ? PlayerRules.ReverseScoreBase - score
                    : score;

                answers.Add(new SurveyAnswer
                {
                    QuestionKey = question.Key,
                    CategoryKey = category.Key,
                    Value = raw
                });
            }
        }

        var submission = new SurveySubmission
        {
            RoundId = roundId,
            PlayerId = player.Id,
            PlayerCode = player.Code,
            RespondentRole = SurveySubmission.Roles.From(respondent),
            RespondentUserId = userId,
            QuestionSetVersion = _catalog.Questions.Version,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-random.Next(1, 10)),
            Answers = answers
        };

        _submissions[new SubmissionKey(roundId, player.Id, userId)] = submission;
    }

    private readonly record struct SubmissionKey(int RoundId, int PlayerId, string RespondentUserId);
}
