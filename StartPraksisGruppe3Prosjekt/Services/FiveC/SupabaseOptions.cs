namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// How to reach Victor's Supabase project. Bound from configuration, section
/// <c>FiveC:Supabase</c>.
///
/// The URL and the table names belong in appsettings.json. THE KEY DOES NOT. Put it in
/// user-secrets in development and in the hosting environment in production:
///
///   dotnet user-secrets set "FiveC:Supabase:ApiKey" "..." --project StartPraksisGruppe3Prosjekt
///
/// The table names are configuration rather than constants so that Victor can rename a
/// table without a code change -- the schema is his, and this side should not be the
/// reason a rename is awkward.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "FiveC:Supabase";

    /// <summary>Project URL, e.g. https://abcdefgh.supabase.co -- no trailing slash needed.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Supabase API key. Use the service role key: the answers are written on behalf of a
    /// user who is signed in HERE, not in Supabase, so there is no Supabase JWT to pass on
    /// and row level security cannot see who the respondent is. That also means this key
    /// must never reach the browser.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>One row per submitted form.</summary>
    public string SubmissionsTable { get; set; } = "five_c_submissions";

    /// <summary>One row per answer, pointing back at a submission.</summary>
    public string AnswersTable { get; set; } = "five_c_answers";

    /// <summary>
    /// The columns that identify one submission. Used as the conflict target when a form is
    /// submitted a second time, so a correction updates the existing row.
    /// </summary>
    public string SubmissionConflictTarget { get; set; } = "round_id,player_id,respondent_user_id";

    /// <summary>The foreign key column on the answers table.</summary>
    public string AnswerSubmissionColumn { get; set; } = "submission_id";

    /// <summary>
    /// Whether there is enough here to talk to Supabase at all. When false the application
    /// falls back to <see cref="InMemorySurveySubmissionStore"/> and says so in the log.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey);
}
