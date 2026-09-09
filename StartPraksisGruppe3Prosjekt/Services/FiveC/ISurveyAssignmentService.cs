using System.Security.Claims;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Answers "which forms is this user supposed to fill in, and in which role".
///
/// One list, three meanings: a player answers about themselves, a guardian about their own
/// children, a coach about any player in the club. Keeping that in one service means
/// the list on /Survey and the check on /Survey/Fill cannot drift apart -- a link that is
/// not in the list is also a link that will not open.
///
/// This service decides WHICH ROLE a user may answer in. It does not decide whether the
/// user may see the player at all: that stays with the CanViewPlayer policy, which the
/// controller runs first. Both have to pass.
/// </summary>
public interface ISurveyAssignmentService
{
    /// <summary>
    /// Every form this user is expected to fill in for the round, with whether it has been
    /// answered already. Empty is a normal result -- an admin, or a coach without a team.
    /// </summary>
    Task<IReadOnlyList<SurveyAssignment>> GetAssignmentsAsync(
        ClaimsPrincipal user,
        int roundId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roles this user may answer in about this player, in the order they should be
    /// preferred when a link does not name one. Empty means the user may not answer at all.
    ///
    /// A user can legitimately have more than one: a coach whose own child plays in the club
    /// answers as a coach about the squad and as a guardian about their child.
    /// </summary>
    Task<IReadOnlyList<RespondentType>> GetAllowedRolesAsync(
        ClaimsPrincipal user,
        Player player,
        CancellationToken cancellationToken = default);
}

/// <summary>One form a user is expected to fill in.</summary>
/// <param name="PlayerId">The player the form is about.</param>
/// <param name="PlayerCode">
/// Player code, e.g. "TS-08-16". CODES, NOT NAMES -- and not because a name column has not
/// been built yet: there are no names anywhere in this data model, on purpose, so the code
/// is what identifies a player in every list in the application.
/// </param>
/// <param name="TeamName">The player's team.</param>
/// <param name="Position">Their position, or null when the club has not recorded one.</param>
/// <param name="BirthDate">
/// Carried so the list can show an age. The date itself is never printed -- an age is the
/// part a coach reading a squad list needs, and a birth date is more than that.
/// </param>
/// <param name="Role">Which role this user answers in for this player.</param>
/// <param name="IsAboutSelf">True when the user is the player. Changes the wording only.</param>
/// <param name="SubmittedAt">When it was last submitted, or null if it has not been.</param>
public sealed record SurveyAssignment(
    int PlayerId,
    string PlayerCode,
    string TeamName,
    string? Position,
    DateOnly BirthDate,
    RespondentType Role,
    bool IsAboutSelf,
    DateTimeOffset? SubmittedAt)
{
    public bool HasAnswered => SubmittedAt.HasValue;

    /// <summary>
    /// Age in whole years today, by the same rule the guardian requirement uses --
    /// <see cref="PlayerRules.AgeAt"/>. Local date rather than UTC: a coach reading a squad
    /// list is in a place, and a player is a year older on their birthday there.
    /// </summary>
    public int Age => PlayerRules.AgeAt(BirthDate, DateOnly.FromDateTime(DateTime.Now));
}
