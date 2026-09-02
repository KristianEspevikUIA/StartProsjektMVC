using Microsoft.Extensions.DependencyInjection;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

// THROWAWAY: renders the coach team page with a full squad so the layout can be looked at.
public sealed class RenderDump : IAsyncLifetime
{
    private readonly StartCompassFactory _factory = new();
    public Task InitializeAsync() => _factory.InitialiseAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }

    private static readonly string[] Formation =
    {
        "Goalkeeper", "Right-back", "Centre-back", "Centre-back", "Left-back",
        "Defensive midfielder", "Central midfielder", "Attacking midfielder",
        "Right winger", "Striker", "Left winger"
    };

    [Fact]
    public async Task Dump()
    {
        var rounds = new List<SurveyRound>();

        await _factory.WithServicesAsync(async services =>
        {
            var periods = services.GetRequiredService<IPeriodService>();
            foreach (var (name, days) in new[] { ("Spring 2026", -150), ("Summer 2026", -60) })
            {
                var r = await periods.CreateAsync(name,
                    DateTimeOffset.UtcNow.AddDays(days - 60), DateTimeOffset.UtcNow.AddDays(days));
                rounds.Add(r.Round!);
            }
        });

        var players = new List<(int Id, string Code)>
        {
            (_factory.PlayerId, "TS-TEST-01"), (_factory.OtherPlayerId, "TS-TEST-02")
        };

        await _factory.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var existing = db.Players.Where(p => p.TeamId == _factory.TeamId).ToList();

            for (var i = 0; i < existing.Count; i++) existing[i].Position = Formation[i];

            for (var i = existing.Count; i < 11; i++)
            {
                var code = $"TS-TEST-{i + 1:00}";
                var p = new Player
                {
                    Code = code, TeamId = _factory.TeamId, UserId = $"user-{code}",
                    BirthDate = new DateOnly(2010, 1, 1), Position = Formation[i]
                };
                db.Players.Add(p);
                await db.SaveChangesAsync();
                players.Add((p.Id, code));
            }
            await db.SaveChangesAsync();
        });

        rounds.Add(new SurveyRound { Id = _factory.RoundId });

        await _factory.WithServicesAsync(async services =>
        {
            var store = services.GetRequiredService<ISurveySubmissionStore>();
            var catalog = services.GetRequiredService<IQuestionCatalog>();

            for (var pi = 0; pi < players.Count; pi++)
            {
                var (id, code) = players[pi];
                var random = new Random(pi + 7);
                var talent = 2.2 + random.NextDouble() * 2.0;

                for (var ri = 0; ri < rounds.Count; ri++)
                {
                    var roundId = rounds[ri].Id;

                    foreach (var (role, user, bias) in new[]
                    {
                        (RespondentType.Player, $"u-p-{code}", 0.0),
                        (RespondentType.Guardian, $"u-g-{code}", 0.5),
                        (RespondentType.Coach, $"u-c-{code}", -0.4)
                    })
                    {
                        if (role != RespondentType.Player && random.NextDouble() < 0.25) continue;

                        var answers = catalog.Questions.Categories.SelectMany(c =>
                        {
                            var target = talent + ri * 0.4 + bias + (random.NextDouble() - 0.5);
                            return c.Questions.Select(q =>
                            {
                                var score = Math.Clamp((int)Math.Round(target + (random.NextDouble() - 0.5)), 1, 5);
                                return new SurveyAnswer
                                {
                                    QuestionKey = q.Key,
                                    CategoryKey = c.Key,
                                    Value = q.Reversed ? PlayerRules.ReverseScoreBase - score : score
                                };
                            });
                        }).ToList();

                        await store.SaveAsync(new SurveySubmission
                        {
                            RoundId = roundId, PlayerId = id, PlayerCode = code,
                            RespondentRole = SurveySubmission.Roles.From(role),
                            RespondentUserId = user,
                            QuestionSetVersion = catalog.Questions.Version,
                            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-100 + ri * 40),
                            Answers = answers
                        });
                    }
                }
            }
        });

        var response = await _factory.ClientAs(StartCompassFactory.CoachUserId, Roles.Coach)
            .GetAsync($"/Coach/FiveCTeam/{_factory.TeamId}");
        await _factory.AssertOkAsync(response);

        var html = await response.Content.ReadAsStringAsync();
        File.WriteAllText(Environment.GetEnvironmentVariable("DUMP_PATH")!, html);
    }
}
