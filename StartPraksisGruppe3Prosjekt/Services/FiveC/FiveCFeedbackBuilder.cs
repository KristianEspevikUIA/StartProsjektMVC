using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Builds the player-facing 5C page for a player or their guardian.
///
/// It exists so the redaction decision -- whether the coach's answers are visible -- is
/// made once. A player page and a guardian page that each decided for themselves would be
/// two chances to get it wrong, and the way to get it wrong is to show a fourteen-year-old
/// what their coach thinks of them before the coach has said it out loud.
/// </summary>
public interface IFiveCFeedbackBuilder
{
    Task<FiveCFeedbackViewModel> BuildAsync(
        Player player,
        SurveyRound round,
        IReadOnlyList<FiveCTeamViewModel.RoundOption> rounds,
        bool viewerIsGuardian,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFiveCFeedbackBuilder" />
public sealed class FiveCFeedbackBuilder : IFiveCFeedbackBuilder
{
    private readonly IFiveCAnalysisService _analysis;
    private readonly Services.IFeedbackReleaseService _releases;
    private readonly Services.IPlayerAccessLog _accessLog;

    public FiveCFeedbackBuilder(
        IFiveCAnalysisService analysis,
        Services.IFeedbackReleaseService releases,
        Services.IPlayerAccessLog accessLog)
    {
        _analysis = analysis;
        _releases = releases;
        _accessLog = accessLog;
    }

    /// <inheritdoc />
    public async Task<FiveCFeedbackViewModel> BuildAsync(
        Player player,
        SurveyRound round,
        IReadOnlyList<FiveCTeamViewModel.RoundOption> rounds,
        bool viewerIsGuardian,
        CancellationToken cancellationToken = default)
    {
        var comparison = await _analysis.GetForPlayerAsync(
            round.Id,
            player.Id,
            player.Code,
            cancellationToken);

        var released = await _releases.IsReleasedAsync(round.Id, player.Id, cancellationToken);

        // The audit log, shown to the person it is about. Roles and dates only -- see
        // FiveCFeedbackViewModel.RecentAccess.
        var access = await _accessLog.GetForPlayerAsync(player.Id, take: 20, cancellationToken);

        return new FiveCFeedbackViewModel
        {
            PlayerId = player.Id,
            PlayerCode = player.Code,
            TeamName = player.Team?.Name ?? string.Empty,
            ViewerIsGuardian = viewerIsGuardian,

            RoundId = round.Id,
            RoundName = round.Name,
            RoundIsOpen = round.IsOpenAt(DateTimeOffset.UtcNow),
            RoundClosesAt = round.ClosesAt,
            Rounds = rounds,

            // Redacted unless the coach has released. The view never gets the coach's
            // numbers in the first place, so it cannot show them by mistake.
            Comparison = released
                ? comparison
                : FiveCFeedbackViewModel.Redact(comparison),

            CoachHasAnswered = comparison.CoachHasAnswered,
            CoachAnswersReleased = released,

            CanFillIn = round.IsOpenAt(DateTimeOffset.UtcNow),
            FillRole = viewerIsGuardian ? RespondentType.Guardian : RespondentType.Player,

            // A guardian's own form is a separate submission from the player's, so "have
            // you answered" is a different question depending on who is looking.
            ViewerHasAnswered = viewerIsGuardian
                ? comparison.GuardianHasAnswered
                : comparison.PlayerHasAnswered,

            // The conversation follows the player, whoever is looking. See Stage.
            PlayerHasAnswered = comparison.PlayerHasAnswered,

            RecentAccess = access
                .Select(a => new FiveCFeedbackViewModel.AccessEntry(a.ViewedByRole, a.OccurredAt))
                .ToList()
        };
    }
}
