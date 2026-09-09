using System.Text.Json.Serialization;

namespace StartPraksisGruppe3Prosjekt.Models.FiveC;

/// <summary>
/// The questionnaire as it is written in Data/Questions/five-c-questions.json.
///
/// These types exist so that the questions live in one editable data file rather than
/// in the views. Nothing here is an entity: the question set is content, not state, and
/// is never written to the database. Answers reference a question by
/// <see cref="Question.Key"/>, so the coaching team can rewrite every wording in the file
/// without invalidating answers that are already stored.
/// </summary>
public sealed class QuestionSet
{
    /// <summary>
    /// Identifies the wording used for a submission, e.g. "placeholder-2026-08-26".
    /// Stored alongside the answers so a later reader can tell which text was on screen.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("scale")]
    public AnswerScale Scale { get; init; } = new();

    /// <summary>The five C's, in the order they are shown.</summary>
    [JsonPropertyName("categories")]
    public IReadOnlyList<QuestionCategory> Categories { get; init; } = Array.Empty<QuestionCategory>();

    /// <summary>
    /// The short reflection asked after the rated statements, or null when the question set
    /// does not have one. See <see cref="ReflectionSection"/>.
    /// </summary>
    [JsonPropertyName("reflection")]
    public ReflectionSection? Reflection { get; init; }

    /// <summary>Every question across every category, in display order.</summary>
    [JsonIgnore]
    public IEnumerable<Question> AllQuestions => Categories.SelectMany(c => c.Questions);

    /// <summary>The reflection questions, in display order. Empty when there is no reflection.</summary>
    [JsonIgnore]
    public IReadOnlyList<ReflectionQuestion> ReflectionQuestions =>
        Reflection?.Questions ?? Array.Empty<ReflectionQuestion>();
}

/// <summary>One of the five C's, with the questions that belong to it.</summary>
public sealed class QuestionCategory
{
    /// <summary>Stable identifier, e.g. "commitment". Answers are grouped on this.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>Heading shown above the questions, e.g. "Commitment".</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>One sentence explaining what the category covers.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("questions")]
    public IReadOnlyList<Question> Questions { get; init; } = Array.Empty<Question>();
}

/// <summary>One statement, answered on the 1-5 scale.</summary>
public sealed class Question
{
    /// <summary>
    /// Stable identifier, e.g. "commitment-1". This is what is stored with the answer.
    /// Rewriting <see cref="Text"/> is safe; changing the key orphans existing answers.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>The statement as the player reads it, in first person.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Wording for somebody answering ABOUT the player rather than about themselves --
    /// "The player keeps working ...". Used by coaches, and by guardians when
    /// <see cref="TextForGuardian"/> is not set. Null falls back to <see cref="Text"/>.
    /// </summary>
    [JsonPropertyName("textAboutPlayer")]
    public string? TextAboutPlayer { get; init; }

    /// <summary>
    /// Wording for a guardian -- "My child keeps working ...".
    ///
    /// Separate from <see cref="TextAboutPlayer"/> because the two readers are not the same
    /// person. A guardian answering about "the player" is being asked about a stranger, and
    /// a coach reading "my child" is being asked something else entirely. Falls back to
    /// <see cref="TextAboutPlayer"/>, then to <see cref="Text"/>.
    /// </summary>
    [JsonPropertyName("textForGuardian")]
    public string? TextForGuardian { get; init; }

    /// <summary>
    /// True when the statement is negatively worded. Scored as (6 - value) by
    /// <see cref="FiveCRules.Score"/> so that a high score always means "good".
    /// The respondent is not told, and the scale is not flipped in the form.
    /// </summary>
    [JsonPropertyName("reversed")]
    public bool Reversed { get; init; }

    /// <summary>
    /// The wording this respondent should see.
    ///
    /// One question, three readers. The player answers about themselves, the coach about a
    /// player they train, the guardian about their own child. It is the same statement and
    /// the same 1-5 scale -- only the grammar changes, and the answer is stored identically
    /// whichever wording produced it.
    ///
    /// Each level falls back to the one below, so a question set that only fills in
    /// <see cref="Text"/> still works for everybody.
    /// </summary>
    public string TextFor(RespondentType respondent) => respondent switch
    {
        RespondentType.Player => Text,
        RespondentType.Guardian => TextForGuardian ?? TextAboutPlayer ?? Text,
        _ => TextAboutPlayer ?? Text
    };
}

/// <summary>The response scale. Defined in the data file so the labels are editable too.</summary>
public sealed class AnswerScale
{
    [JsonPropertyName("min")]
    public int Min { get; init; } = FiveCRules.ScaleMin;

    [JsonPropertyName("max")]
    public int Max { get; init; } = FiveCRules.ScaleMax;

    /// <summary>
    /// Whether "Don't know" is offered as an option outside the 1-5 row. Off by default:
    /// the 5C specification asks for a plain 1-5 scale. When it is turned on, the answer
    /// is submitted empty and stored as null -- never as 3, which would be a real answer.
    /// </summary>
    [JsonPropertyName("allowDontKnow")]
    public bool AllowDontKnow { get; init; }

    [JsonPropertyName("dontKnowLabel")]
    public string DontKnowLabel { get; init; } = "Don't know";

    [JsonPropertyName("options")]
    public IReadOnlyList<ScaleOption> Options { get; init; } = Array.Empty<ScaleOption>();

    /// <summary>Label for the lowest value, shown at the left end of the row.</summary>
    public string LowLabel => Options.FirstOrDefault()?.Label ?? string.Empty;

    /// <summary>Label for the highest value, shown at the right end of the row.</summary>
    public string HighLabel => Options.LastOrDefault()?.Label ?? string.Empty;
}

/// <summary>One point on the scale, e.g. 1 = "Strongly disagree".</summary>
public sealed class ScaleOption
{
    [JsonPropertyName("value")]
    public int Value { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// The reflection asked once, after the rated statements.
///
/// The 25 statements say WHERE a player is on each C. They do not say what to do next, and
/// a number cannot: "Concentration 2.8" is not a plan. This section is where the respondent
/// writes that down in their own words -- the strongest C, the one worth working on next,
/// and what would help -- so the meso period ends with something a conversation can start
/// from.
///
/// It is content, exactly like the statements above it: written in
/// Data/Questions/five-c-questions.json, never in a view, and answered by all three
/// respondent types. Leave the whole section out of the file and the form is the 25
/// statements it was before.
/// </summary>
public sealed class ReflectionSection
{
    /// <summary>Heading over the section, e.g. "End of period". Also the tab label.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>One or two sentences saying what the section is for.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("questions")]
    public IReadOnlyList<ReflectionQuestion> Questions { get; init; } = Array.Empty<ReflectionQuestion>();
}

/// <summary>
/// One reflection question. Either a choice between the five C's or a written answer --
/// <see cref="ReflectionQuestionTypes"/> says which.
///
/// The three wordings work exactly as they do on a rated statement: the player answers
/// about themselves, the coach about a player, the guardian about their child. See
/// <see cref="Question.TextFor"/>.
/// </summary>
public sealed class ReflectionQuestion
{
    /// <summary>
    /// Stable identifier, e.g. "reflection-strength". Answers are stored against it, so the
    /// same rule holds as for a statement: rewrite the text freely, change the key only when
    /// it is genuinely a different question.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// "category" for a choice between the five C's, "text" for a written answer.
    /// Validated on load, so a typo here stops the application rather than rendering a
    /// question nobody can answer.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = ReflectionQuestionTypes.Text;

    /// <inheritdoc cref="Question.Text" />
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <inheritdoc cref="Question.TextAboutPlayer" />
    [JsonPropertyName("textAboutPlayer")]
    public string? TextAboutPlayer { get; init; }

    /// <inheritdoc cref="Question.TextForGuardian" />
    [JsonPropertyName("textForGuardian")]
    public string? TextForGuardian { get; init; }

    /// <summary>
    /// Grey text inside an empty written answer. Optional, and never a default value: what
    /// it says is not submitted.
    /// </summary>
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; init; }

    /// <summary>
    /// Whether the form refuses to save without this one. False by default, and deliberately
    /// so: the 25 statements are the measurement, and a compulsory paragraph at the end of
    /// them is answered with a full stop. Turn it on per question in the file if the club
    /// decides otherwise -- no code change.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>
    /// Longest written answer accepted, in characters. Ignored for a category choice.
    /// Enforced on the server as well as in the browser, and capped at
    /// <see cref="FiveCRules.ReflectionTextLimit"/> -- the size of the column it is stored in.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int MaxLength { get; init; } = FiveCRules.DefaultReflectionMaxLength;

    /// <summary>True when this question is answered by picking one of the five C's.</summary>
    [JsonIgnore]
    public bool IsCategoryChoice =>
        string.Equals(Type, ReflectionQuestionTypes.Category, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="Question.TextFor" />
    public string TextFor(RespondentType respondent) => respondent switch
    {
        RespondentType.Player => Text,
        RespondentType.Guardian => TextForGuardian ?? TextAboutPlayer ?? Text,
        _ => TextAboutPlayer ?? Text
    };
}

/// <summary>The two values <see cref="ReflectionQuestion.Type"/> may take.</summary>
public static class ReflectionQuestionTypes
{
    /// <summary>A choice between the five C's. The answer stored is a category key.</summary>
    public const string Category = "category";

    /// <summary>A written answer, in the respondent's own words.</summary>
    public const string Text = "text";

    public static bool IsKnown(string? type) =>
        string.Equals(type, Category, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Text, StringComparison.OrdinalIgnoreCase);
}
