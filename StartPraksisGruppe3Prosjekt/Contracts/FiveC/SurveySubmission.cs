using System.Text.Json.Serialization;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Contracts.FiveC;

/// <summary>
/// WHAT THE FRONTEND SENDS WHEN A 5C FORM IS SUBMITTED.
///
/// This is the contract between this application and whatever stores the answers. It is
/// deliberately flat and free of anything database-shaped: no surrogate keys, no foreign
/// keys, no table names. Victor owns the Supabase schema; this type only says what the
/// form has to hand over, so the two can be lined up without either side guessing.
///
/// The same shape is mirrored in Contracts/FiveC/survey-submission.ts. Keep them in step.
///
/// The JSON property names are snake_case because that is what a Postgres/PostgREST table
/// will call its columns, which lets the payload be posted with no translation layer:
///
/// {
///   "round_id": 2,
///   "player_id": 14,
///   "player_code": "TS-08-16",
///   "respondent_role": "coach",
///   "respondent_user_id": "9f0c...",
///   "question_set_version": "placeholder-2026-08-26",
///   "submitted_at": "2026-08-26T07:30:00+00:00",
///   "answers": [
///     { "question_key": "commitment-1", "category_key": "commitment", "value": 4 }
///   ],
///   "reflection": [
///     { "question_key": "reflection-strength", "value": "confidence" },
///     { "question_key": "reflection-strength-example", "value": "Took the last penalty ..." }
///   ]
/// }
/// </summary>
public sealed record SurveySubmission
{
    /// <summary>The round being answered. Answers outside an open round are rejected.</summary>
    [JsonPropertyName("round_id")]
    public required int RoundId { get; init; }

    /// <summary>
    /// The player the answers are ABOUT -- also when a coach or a guardian is filling in
    /// the form. Never the respondent.
    /// </summary>
    [JsonPropertyName("player_id")]
    public required int PlayerId { get; init; }

    /// <summary>
    /// The club-internal pseudonymous code, e.g. "TS-08-16". Sent so that a row is
    /// readable without joining back to a table of minors. Never a name.
    /// </summary>
    [JsonPropertyName("player_code")]
    public required string PlayerCode { get; init; }

    /// <summary>Who is answering: "player", "coach" or "guardian". See <see cref="Roles"/>.</summary>
    [JsonPropertyName("respondent_role")]
    public required string RespondentRole { get; init; }

    /// <summary>
    /// The signed-in user who actually filled the form in. Together with round and player
    /// this identifies one submission: one form per person, per player, per round.
    /// </summary>
    [JsonPropertyName("respondent_user_id")]
    public required string RespondentUserId { get; init; }

    /// <summary>
    /// Which wording was on screen, from the question set file. Two rounds answered against
    /// different question texts are not comparable, and this is what makes that visible.
    /// </summary>
    [JsonPropertyName("question_set_version")]
    public required string QuestionSetVersion { get; init; }

    [JsonPropertyName("submitted_at")]
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>One entry per answered question, in the order the form showed them.</summary>
    [JsonPropertyName("answers")]
    public required IReadOnlyList<SurveyAnswer> Answers { get; init; }

    /// <summary>
    /// The end-of-period reflection: the strongest C, the one to work on next, and what was
    /// written about them. Empty when the question set has no reflection section, or when
    /// the respondent left all of it blank.
    ///
    /// Not required, unlike <see cref="Answers"/>: a question set without a reflection is a
    /// valid question set, and this way a caller written before the section existed still
    /// compiles and still stores what it always did.
    /// </summary>
    [JsonPropertyName("reflection")]
    public IReadOnlyList<ReflectionAnswer> Reflection { get; init; } = Array.Empty<ReflectionAnswer>();

    /// <summary>
    /// The three role strings used on the wire. Lower-case so they can be a Postgres enum
    /// or a check constraint without any casing surprises.
    /// </summary>
    public static class Roles
    {
        public const string Player = "player";
        public const string Coach = "coach";
        public const string Guardian = "guardian";

        /// <summary>Maps the in-app enum to the wire value. Use this, not ToString().</summary>
        public static string From(RespondentType respondent) => respondent switch
        {
            RespondentType.Player => Player,
            RespondentType.Coach => Coach,
            RespondentType.Guardian => Guardian,
            _ => throw new ArgumentOutOfRangeException(nameof(respondent), respondent, null)
        };

        /// <summary>Maps a wire value back, for reading stored submissions.</summary>
        public static RespondentType To(string role) => role?.ToLowerInvariant() switch
        {
            Player => RespondentType.Player,
            Coach => RespondentType.Coach,
            Guardian => RespondentType.Guardian,
            _ => throw new ArgumentOutOfRangeException(
                nameof(role), role, $"Unknown respondent role. Expected one of: {Player}, {Coach}, {Guardian}.")
        };
    }
}

/// <summary>One answer in a submission.</summary>
public sealed record SurveyAnswer
{
    /// <summary>
    /// The stable key from the question set file, e.g. "commitment-1". Not an index and
    /// not the question text: the text is expected to be rewritten, the key is not.
    /// </summary>
    [JsonPropertyName("question_key")]
    public required string QuestionKey { get; init; }

    /// <summary>
    /// Which of the five C's the question belongs to, e.g. "commitment". Denormalised on
    /// purpose so answers can be grouped per C without loading the question set.
    /// </summary>
    [JsonPropertyName("category_key")]
    public required string CategoryKey { get; init; }

    /// <summary>
    /// The raw answer, 1-5, exactly as the respondent gave it. Null means the question was
    /// not answered -- and null is NOT 3. An unanswered question is left out of every mean
    /// rather than pulled to the middle of the scale.
    ///
    /// Stored unreversed. Negatively worded statements are flipped when they are read, by
    /// <see cref="Models.FiveC.FiveCRules.Score"/>, so that the raw answer survives a later
    /// change to which statements count as reversed.
    /// </summary>
    [JsonPropertyName("value")]
    public required int? Value { get; init; }
}

/// <summary>
/// One answer to a reflection question -- the part of the form that is words rather than
/// numbers.
///
/// Kept apart from <see cref="SurveyAnswer"/> and stored in its own table, for two reasons
/// that both matter more than the saved join:
///
///   * It is not scored. Nothing here goes into a mean, a difference or a trend, and an
///     answer that cannot be scored has no business in the table the scoring reads.
///   * It is free text about a child. The numbers are pseudonymous by construction; a
///     sentence is only as pseudonymous as whoever typed it. Keeping it in one named place
///     is what makes it findable for an access request and reviewable on its own.
/// </summary>
public sealed record ReflectionAnswer
{
    /// <summary>
    /// The stable key from the question set file, e.g. "reflection-strength".
    /// See <see cref="SurveyAnswer.QuestionKey"/> -- the same rule applies.
    /// </summary>
    [JsonPropertyName("question_key")]
    public required string QuestionKey { get; init; }

    /// <summary>
    /// What was answered.
    ///
    /// For a question of type "category" this is a category key -- "confidence", not
    /// "Confidence" -- so the choice can be counted and compared without matching on a
    /// heading that is expected to be rewritten. For a question of type "text" it is what
    /// the respondent wrote, trimmed.
    ///
    /// Null means the question was left blank. A blank is not an answer, and it is not
    /// stored as an empty string.
    /// </summary>
    [JsonPropertyName("value")]
    public required string? Value { get; init; }
}
