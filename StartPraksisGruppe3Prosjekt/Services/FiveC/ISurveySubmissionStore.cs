using StartPraksisGruppe3Prosjekt.Contracts.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Where submitted 5C forms go, and where the coach overview reads them back from.
///
/// The interface exists so that the controllers and the analysis service never mention
/// Supabase. Two implementations ship:
///
///   <see cref="SupabaseSurveySubmissionStore"/>  -- the real one, posts to Supabase.
///   <see cref="InMemorySurveySubmissionStore"/>  -- development fallback, used when
///                                                   Supabase is not configured yet.
///
/// Which one is live is decided in Program.cs from configuration, not by a flag passed
/// around in code. See appsettings.json, section "FiveC".
/// </summary>
public interface ISurveySubmissionStore
{
    /// <summary>
    /// A short name for whichever store is live, e.g. "Supabase". Shown to admins and
    /// written to the log at startup, so nobody has to guess whether answers are actually
    /// leaving the machine.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Stores one submitted form.
    ///
    /// Idempotent per (round, player, respondent): submitting again replaces the previous
    /// answers rather than adding a second submission. That is what makes "you can change
    /// your answers while the round is open" true.
    /// </summary>
    Task SaveAsync(SurveySubmission submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// This person's own submission for this player and round, or null if they have not
    /// answered yet. Used to pre-fill the form so a correction starts from what was sent.
    /// </summary>
    Task<SurveySubmission?> FindAsync(
        int roundId,
        int playerId,
        string respondentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every submission about one player in one round -- the player's own, the coach's and
    /// the guardian's. This is the input to the comparison in the coach overview.
    /// </summary>
    Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for a set of players, in one round trip. The team overview needs every
    /// player on the team, and one call per player would be N+1 requests over the network.
    ///
    /// The caller is still responsible for checking CanViewPlayer per player before
    /// anything is shown. This method does not filter on consent.
    /// </summary>
    Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many submissions each of these rounds holds, in one round trip.
    ///
    /// The admin period list needs a number per period and nothing else. Reading the
    /// submissions themselves to count them meant one request per period, each one dragging
    /// back twenty-five answers per respondent that were then thrown away.
    ///
    /// Every round asked for gets an entry, zero included. A missing key would leave the
    /// caller to guess, and "no answers" is exactly what the caller wants to be able to say.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> CountByRoundAsync(
        IEnumerable<int> roundIds,
        CancellationToken cancellationToken = default);
}
