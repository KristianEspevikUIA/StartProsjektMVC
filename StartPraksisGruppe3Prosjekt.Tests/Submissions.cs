using System.Security.Claims;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>Building blocks the service tests share: a filled-in form, and a signed-in user.</summary>
internal static class Submissions
{
    /// <summary>
    /// One complete submission, every question answered with the same raw value. Raw, not
    /// scored -- a reversed statement is flipped when it is read, never when it is written.
    /// </summary>
    public static SurveySubmission Filled(
        IQuestionCatalog catalog,
        int roundId,
        int playerId,
        string playerCode,
        RespondentType role,
        string userId,
        int value = 4)
    {
        var answers = catalog.Questions.Categories
            .SelectMany(category => category.Questions.Select(question => new SurveyAnswer
            {
                QuestionKey = question.Key,
                CategoryKey = category.Key,
                Value = value
            }))
            .ToList();

        return new SurveySubmission
        {
            RoundId = roundId,
            PlayerId = playerId,
            PlayerCode = playerCode,
            RespondentRole = SurveySubmission.Roles.From(role),
            RespondentUserId = userId,
            QuestionSetVersion = catalog.Questions.Version,
            SubmittedAt = DateTimeOffset.UtcNow,
            Answers = answers
        };
    }

    /// <summary>A signed-in user with an id and, optionally, roles.</summary>
    public static ClaimsPrincipal User(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }
}
