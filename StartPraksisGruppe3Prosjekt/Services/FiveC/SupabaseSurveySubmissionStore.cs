using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StartPraksisGruppe3Prosjekt.Contracts.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Stores 5C submissions in Supabase over PostgREST, the REST API every Supabase project
/// exposes at /rest/v1. No client library: two tables and four requests do not need one,
/// and a package would be one more thing to keep in step with Victor's schema.
///
/// This class knows the SHAPE of the data -- <see cref="SurveySubmission"/> -- and gets the
/// table and column names from <see cref="SupabaseOptions"/>. It does not define the schema.
/// If a name here is wrong, it is configuration that is wrong, not code.
///
/// UNVERIFIED AGAINST A REAL PROJECT. The tables did not exist when this was written, so
/// the request shapes follow the PostgREST documentation rather than a green test. Expect
/// to check the two POSTs against the real tables the first time it is pointed at them.
/// </summary>
public sealed class SupabaseSurveySubmissionStore : ISurveySubmissionStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseSurveySubmissionStore> _logger;

    public SupabaseSurveySubmissionStore(
        HttpClient http,
        IOptions<SupabaseOptions> options,
        ILogger<SupabaseSurveySubmissionStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri($"{_options.Url.TrimEnd('/')}/rest/v1/");

        // PostgREST wants the key twice: apikey gets the request past the gateway,
        // Authorization decides which role the query runs as.
        _http.DefaultRequestHeaders.Add("apikey", _options.ApiKey);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    /// <inheritdoc />
    public string Description => "Supabase";

    /// <inheritdoc />
    public async Task SaveAsync(
        SurveySubmission submission,
        CancellationToken cancellationToken = default)
    {
        // 1. Upsert the submission row. merge-duplicates plus the conflict target is what
        //    turns a second submission into a correction instead of a duplicate.
        var submissionId = await UpsertSubmissionAsync(submission, cancellationToken);

        // 2. Replace the answers wholesale. Diffing 25 rows to save a delete is not worth
        //    the branch, and a wholesale replace cannot leave an answer behind that the
        //    respondent has since cleared.
        await DeleteAnswersAsync(submissionId, cancellationToken);
        await InsertAnswersAsync(submissionId, submission.Answers, cancellationToken);

        _logger.LogInformation(
            "Stored 5C submission {SubmissionId}: round {RoundId}, player {PlayerId}, role {Role}.",
            submissionId,
            submission.RoundId,
            submission.PlayerId,
            submission.RespondentRole);
    }

    /// <inheritdoc />
    public async Task<SurveySubmission?> FindAsync(
        int roundId,
        int playerId,
        string respondentUserId,
        CancellationToken cancellationToken = default)
    {
        var query =
            $"{_options.SubmissionsTable}" +
            $"?round_id=eq.{roundId}" +
            $"&player_id=eq.{playerId}" +
            $"&respondent_user_id=eq.{Uri.EscapeDataString(respondentUserId)}" +
            "&select=*&limit=1";

        var rows = await GetRowsAsync(query, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var withAnswers = await AttachAnswersAsync(rows, cancellationToken);
        return withAnswers.SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayerAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        var query =
            $"{_options.SubmissionsTable}" +
            $"?round_id=eq.{roundId}" +
            $"&player_id=eq.{playerId}" +
            "&select=*";

        var rows = await GetRowsAsync(query, cancellationToken);
        return await AttachAnswersAsync(rows, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurveySubmission>> GetForPlayersAsync(
        int roundId,
        IEnumerable<int> playerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = playerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<SurveySubmission>();
        }

        var query =
            $"{_options.SubmissionsTable}" +
            $"?round_id=eq.{roundId}" +
            $"&player_id=in.({string.Join(',', ids)})" +
            "&select=*";

        var rows = await GetRowsAsync(query, cancellationToken);
        return await AttachAnswersAsync(rows, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> CountByRoundAsync(
        IEnumerable<int> roundIds,
        CancellationToken cancellationToken = default)
    {
        var ids = roundIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        // One request for every round at once, selecting the round id and nothing else.
        // PostgREST can return a count in the Content-Range header, but only for one filter
        // at a time -- that would be a request per round, which is the thing this replaces.
        var query =
            $"{_options.SubmissionsTable}" +
            $"?round_id=in.({string.Join(',', ids)})" +
            "&select=round_id";

        using var response = await _http.GetAsync(query, cancellationToken);
        await EnsureSuccessAsync(response, "count submissions", cancellationToken);

        var rows = await response.Content.ReadFromJsonAsync<List<SubmissionRow>>(Json, cancellationToken)
                   ?? new List<SubmissionRow>();

        var counts = rows
            .GroupBy(row => row.RoundId)
            .ToDictionary(group => group.Key, group => group.Count());

        return ids.ToDictionary(
            id => id,
            id => counts.TryGetValue(id, out var count) ? count : 0);
    }

    private async Task<long> UpsertSubmissionAsync(
        SurveySubmission submission,
        CancellationToken cancellationToken)
    {
        var url = $"{_options.SubmissionsTable}?on_conflict={_options.SubmissionConflictTarget}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            // PostgREST takes an array even for a single row.
            Content = JsonContent.Create(new[] { SubmissionRow.From(submission) }, options: Json)
        };

        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "upsert submission", cancellationToken);

        var rows = await response.Content.ReadFromJsonAsync<List<SubmissionRow>>(Json, cancellationToken);

        var id = rows?.FirstOrDefault()?.Id;
        if (id is null)
        {
            throw new InvalidOperationException(
                $"Supabase accepted the submission but returned no id. The '{_options.SubmissionsTable}' " +
                "table needs a generated 'id' column for the answers to point at.");
        }

        return id.Value;
    }

    private async Task DeleteAnswersAsync(long submissionId, CancellationToken cancellationToken)
    {
        var url = $"{_options.AnswersTable}?{_options.AnswerSubmissionColumn}=eq.{submissionId}";

        using var response = await _http.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, "delete previous answers", cancellationToken);
    }

    private async Task InsertAnswersAsync(
        long submissionId,
        IReadOnlyList<SurveyAnswer> answers,
        CancellationToken cancellationToken)
    {
        if (answers.Count == 0)
        {
            return;
        }

        var rows = answers
            .Select(a => AnswerRow.From(submissionId, a, _options.AnswerSubmissionColumn))
            .ToList();

        using var response = await _http.PostAsJsonAsync(_options.AnswersTable, rows, Json, cancellationToken);
        await EnsureSuccessAsync(response, "insert answers", cancellationToken);
    }

    private async Task<List<SubmissionRow>> GetRowsAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(query, cancellationToken);
        await EnsureSuccessAsync(response, "read submissions", cancellationToken);

        return await response.Content.ReadFromJsonAsync<List<SubmissionRow>>(Json, cancellationToken)
               ?? new List<SubmissionRow>();
    }

    /// <summary>
    /// Fetches the answers for a set of submissions in one request and folds them into the
    /// contract type. Kept as a second request rather than a PostgREST embedded select,
    /// because embedding depends on the foreign key being named a particular way -- and
    /// that is a detail of a schema this class does not own.
    /// </summary>
    private async Task<IReadOnlyList<SurveySubmission>> AttachAnswersAsync(
        List<SubmissionRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<SurveySubmission>();
        }

        var ids = rows.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToList();

        // The foreign key column is configurable, so it is aliased back to submission_id in
        // the select. That keeps the response shape fixed no matter what the column is called,
        // which is what lets AnswerRow have a compile-time [JsonPropertyName] for it.
        var query =
            $"{_options.AnswersTable}" +
            $"?{_options.AnswerSubmissionColumn}=in.({string.Join(',', ids)})" +
            $"&select=submission_id:{_options.AnswerSubmissionColumn},question_key,category_key,value";

        using var response = await _http.GetAsync(query, cancellationToken);
        await EnsureSuccessAsync(response, "read answers", cancellationToken);

        var answerRows = await response.Content.ReadFromJsonAsync<List<AnswerRow>>(Json, cancellationToken)
                         ?? new List<AnswerRow>();

        var bySubmission = answerRows
            .GroupBy(a => a.SubmissionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows
            .Select(row => row.ToSubmission(
                bySubmission.TryGetValue(row.Id ?? 0, out var list)
                    ? list.Select(a => a.ToAnswer()).ToList()
                    : new List<SurveyAnswer>()))
            .ToList();
    }

    /// <summary>
    /// PostgREST puts a readable explanation in the body -- a missing column, a failed
    /// constraint. Losing it turns every schema mismatch into a bare status code.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Supabase rejected the request to {operation}: {(int)response.StatusCode} " +
            $"{response.ReasonPhrase}. {body}");
    }

    /// <summary>
    /// One row of the submissions table. Separate from <see cref="SurveySubmission"/>
    /// because the wire row carries a database id and does not carry the answers.
    /// </summary>
    private sealed record SubmissionRow
    {
        [JsonPropertyName("id")]
        public long? Id { get; init; }

        [JsonPropertyName("round_id")]
        public int RoundId { get; init; }

        [JsonPropertyName("player_id")]
        public int PlayerId { get; init; }

        [JsonPropertyName("player_code")]
        public string PlayerCode { get; init; } = string.Empty;

        [JsonPropertyName("respondent_role")]
        public string RespondentRole { get; init; } = string.Empty;

        [JsonPropertyName("respondent_user_id")]
        public string RespondentUserId { get; init; } = string.Empty;

        [JsonPropertyName("question_set_version")]
        public string QuestionSetVersion { get; init; } = string.Empty;

        [JsonPropertyName("submitted_at")]
        public DateTimeOffset SubmittedAt { get; init; }

        public static SubmissionRow From(SurveySubmission submission) => new()
        {
            // Id is left out on write: the database generates it.
            RoundId = submission.RoundId,
            PlayerId = submission.PlayerId,
            PlayerCode = submission.PlayerCode,
            RespondentRole = submission.RespondentRole,
            RespondentUserId = submission.RespondentUserId,
            QuestionSetVersion = submission.QuestionSetVersion,
            SubmittedAt = submission.SubmittedAt
        };

        public SurveySubmission ToSubmission(IReadOnlyList<SurveyAnswer> answers) => new()
        {
            RoundId = RoundId,
            PlayerId = PlayerId,
            PlayerCode = PlayerCode,
            RespondentRole = RespondentRole,
            RespondentUserId = RespondentUserId,
            QuestionSetVersion = QuestionSetVersion,
            SubmittedAt = SubmittedAt,
            Answers = answers
        };
    }

    /// <summary>One row of the answers table.</summary>
    private sealed record AnswerRow
    {
        [JsonPropertyName("submission_id")]
        public long SubmissionId { get; init; }

        [JsonPropertyName("question_key")]
        public string QuestionKey { get; init; } = string.Empty;

        [JsonPropertyName("category_key")]
        public string CategoryKey { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public int? Value { get; init; }

        /// <summary>
        /// Built as a dictionary because the foreign key column name is configurable, and a
        /// [JsonPropertyName] is fixed at compile time.
        /// </summary>
        public static Dictionary<string, object?> From(
            long submissionId,
            SurveyAnswer answer,
            string submissionColumn) => new()
        {
            [submissionColumn] = submissionId,
            ["question_key"] = answer.QuestionKey,
            ["category_key"] = answer.CategoryKey,
            ["value"] = answer.Value
        };

        public SurveyAnswer ToAnswer() => new()
        {
            QuestionKey = QuestionKey,
            CategoryKey = CategoryKey,
            Value = Value
        };
    }
}
