using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// One respondent role's team average for one slice of the questionnaire, and how many
/// people are behind it.
///
/// The count is not decoration. Below
/// <see cref="CanViewTeamAggregateRequirement.MinimumResponses"/> respondents an average is
/// close enough to a single person's answers to be read as one -- particularly by a coach,
/// who knows who has answered. <see cref="From"/> is therefore the only way one of these is
/// built, and it drops the number rather than handing it on: nothing downstream can show a
/// mean the threshold withheld, because nothing downstream is given it.
///
/// <see cref="Withheld"/> is what keeps that apart from "nobody answered". The two are
/// different things and the page says which one it is.
/// </summary>
/// <param name="Role">Whose answers the average is over.</param>
/// <param name="Mean">The average, on the 1-5 scale after reversal, or null.</param>
/// <param name="Respondents">How many people of that role are behind it. Never withheld.</param>
/// <param name="Withheld">There was a number, and it was suppressed as too thin.</param>
public sealed record TeamRoleAverage(
    RespondentType Role,
    double? Mean,
    int Respondents,
    bool Withheld)
{
    /// <summary>
    /// A role average with the minimum-respondents rule already applied.
    /// </summary>
    /// <param name="role">Whose answers these are.</param>
    /// <param name="mean">The average over everyone of that role who answered, or null.</param>
    /// <param name="respondents">How many of them there were.</param>
    public static TeamRoleAverage From(RespondentType role, double? mean, int respondents)
    {
        var enough = respondents >= CanViewTeamAggregateRequirement.MinimumResponses;

        return new TeamRoleAverage(
            Role: role,
            Mean: enough ? mean : null,
            Respondents: respondents,
            Withheld: !enough && mean.HasValue);
    }

    /// <summary>Nobody of this role answered. Not the same as an average of zero.</summary>
    public static TeamRoleAverage None(RespondentType role) => new(role, null, 0, false);

    /// <summary>Display name for the role, from the one place that decides it.</summary>
    public string RoleName => RespondentGap.DisplayName(Role);

    /// <summary>The same name in the plural, from the same place. "Coaches", not "Coachs".</summary>
    public string RoleNamePlural => RespondentGap.PluralName(Role);
}

/// <summary>
/// The three team averages for one slice of the questionnaire -- the whole form, one
/// category, or one statement.
///
/// Drawn with the same three bars as a single player's category, which is what
/// <see cref="IRespondentMeans"/> is for: a coach reads a team's Commitment the way they
/// read a player's, and nothing about the chart has to be relearned.
/// </summary>
/// <param name="Name">What the slice is called, e.g. "Commitment".</param>
/// <param name="Player">The squad's own answers, averaged.</param>
/// <param name="Guardian">Their guardians' answers, averaged.</param>
/// <param name="Coach">The coaches' answers, averaged.</param>
public sealed record TeamMeans(
    string Name,
    TeamRoleAverage Player,
    TeamRoleAverage Guardian,
    TeamRoleAverage Coach) : IRespondentMeans
{
    /// <inheritdoc />
    public string Label => Name;

    /// <inheritdoc />
    public double? PlayerMean => Player.Mean;

    /// <inheritdoc />
    public double? GuardianMean => Guardian.Mean;

    /// <inheritdoc />
    public double? CoachMean => Coach.Mean;

    /// <summary>
    /// The squad averages low enough here to be worth acting on.
    ///
    /// Built on the players' own answers, as it is for one player, and the respondent count
    /// stands in for the answered-question count: a mean under two backed by at least three
    /// players is a squad saying the same thing, not one bad afternoon. A withheld mean is
    /// null and therefore never flags -- see <see cref="FiveCRules.NeedsFollowUp"/>.
    /// </summary>
    public bool NeedsFollowUp => FiveCRules.NeedsFollowUp(Player.Mean, Player.Respondents);

    /// <summary>The three roles in display order, for a legend or a table row.</summary>
    public IReadOnlyList<TeamRoleAverage> Roles => new[] { Player, Guardian, Coach };

    /// <summary>True when at least one role has a number to show.</summary>
    public bool HasAnyMeans => PlayerMean.HasValue || GuardianMean.HasValue || CoachMean.HasValue;

    /// <summary>True when somebody answered but every role was too thin to show.</summary>
    public bool AnythingWithheld => Roles.Any(r => r.Withheld);
}

/// <summary>One statement, averaged across the squad.</summary>
/// <param name="QuestionKey">Stable key from the question set, e.g. "commitment-1".</param>
/// <param name="Number">Running number across the whole form, 1-25. Matches the form.</param>
/// <param name="Text">The statement in the player's own wording, which is the reference one.</param>
/// <param name="Reversed">
/// Negatively worded. Unlike the per-player statement table, the numbers here are SCORED
/// and not raw: they sit beside category averages on a "higher is better" scale, and a raw
/// average on a reversed statement would be the one bar in the section pointing the wrong
/// way. The flag stays so a reader can tell which statements were turned round.
/// </param>
/// <param name="Means">The three team averages for this statement.</param>
public sealed record TeamQuestionAverage(
    string QuestionKey,
    int Number,
    string Text,
    bool Reversed,
    TeamMeans Means);

/// <summary>One of the five C's, averaged across the squad, with its statements.</summary>
/// <param name="CategoryKey">Category key, e.g. "commitment".</param>
/// <param name="Means">The three team averages for the category as a whole.</param>
/// <param name="Questions">The statements in it, each averaged the same way.</param>
public sealed record TeamCategoryAverage(
    string CategoryKey,
    TeamMeans Means,
    IReadOnlyList<TeamQuestionAverage> Questions)
{
    /// <summary>Heading, e.g. "Commitment".</summary>
    public string CategoryName => Means.Name;
}

/// <summary>
/// A whole squad's 5C picture for one round: across every statement, per category, and per
/// statement -- the same three levels a single player is read at, one step up.
///
/// EVERY NUMBER IS AN AVERAGE OF PLAYERS, NOT OF ANSWERS. At each level the squad value is
/// the mean of the per-player values at that level, so one player counts once whether they
/// answered five statements or twenty-five. Pooling every answer instead would let the most
/// diligent respondent quietly weigh the most, and a team average is meant to describe the
/// average player.
///
/// No player code and no player id leaves this record. It is an aggregate, it is built from
/// ids the caller already had, and there is nothing here to trace back to one person -- see
/// <see cref="TeamRoleAverage"/> for the rule that keeps it that way.
///
/// Nothing is stored. Like the rest of the 5C picture it is recalculated from the raw
/// answers on every request.
/// </summary>
/// <param name="TeamId">The team.</param>
/// <param name="TeamName">Team name, e.g. "G16". Teams have names; players have codes.</param>
/// <param name="RoundId">The period.</param>
/// <param name="SquadSize">How many players are on the team at all.</param>
/// <param name="PlayersWithAnswers">
/// How many of them anybody has answered about. This is the number the
/// <see cref="Policies.CanViewTeamAggregate"/> check is made against.
/// </param>
/// <param name="Overall">Across all twenty-five statements at once.</param>
/// <param name="Categories">The five C's, in the order the question set lists them.</param>
public sealed record TeamFiveCAggregate(
    int TeamId,
    string TeamName,
    int RoundId,
    int SquadSize,
    int PlayersWithAnswers,
    TeamMeans Overall,
    IReadOnlyList<TeamCategoryAverage> Categories)
{
    /// <summary>True when at least one number anywhere can be shown.</summary>
    public bool HasAnyMeans => Overall.HasAnyMeans || Categories.Any(c => c.Means.HasAnyMeans);

    /// <summary>The categories the squad scores consistently low on. Empty is the normal case.</summary>
    public IReadOnlyList<TeamCategoryAverage> FollowUp =>
        Categories.Where(c => c.Means.NeedsFollowUp).ToList();

    /// <summary>True when at least one category is flagged. Drives the notice.</summary>
    public bool NeedsFollowUp => FollowUp.Count > 0;

    /// <summary>
    /// The category the squad's own answers are lowest in, or null when none can be shown.
    /// Not a flag -- every squad has a lowest C -- but it is where a season plan starts.
    /// </summary>
    public TeamCategoryAverage? WeakestCategory =>
        Categories.Where(c => c.Means.PlayerMean.HasValue)
                  .OrderBy(c => c.Means.PlayerMean)
                  .FirstOrDefault();

    /// <summary>The category the squad's own answers are highest in, or null.</summary>
    public TeamCategoryAverage? StrongestCategory =>
        Categories.Where(c => c.Means.PlayerMean.HasValue)
                  .OrderByDescending(c => c.Means.PlayerMean)
                  .FirstOrDefault();
}
