using System.ComponentModel.DataAnnotations;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// The 5C form: five categories, five statements each, answered on a 1-5 scale.
///
/// All three respondent types get the same form. What changes is the header -- who is
/// answering and about whom -- not the statements. See <see cref="Question.TextFor"/> for
/// the one hook that can change that later without touching this class or the view.
///
/// Nothing here holds question text that was typed in a .cshtml file. <see cref="Sections"/>
/// is rebuilt from <see cref="Services.FiveC.IQuestionCatalog"/> on every render, including
/// after a failed POST, so a new question set is picked up with no code change.
/// </summary>
public class SurveyFormViewModel
{
    // ---- Identity of the submission. Re-checked server side on POST; the hidden fields
    // ---- in the form are input, not proof.

    public int RoundId { get; set; }

    public string RoundName { get; set; } = string.Empty;

    public DateTimeOffset RoundClosesAt { get; set; }

    /// <summary>The player the answers are about, also when a coach or guardian is answering.</summary>
    public int PlayerId { get; set; }

    /// <summary>Player code, e.g. "TS-08-16". Codes are used in the UI, not names.</summary>
    public string PlayerCode { get; set; } = string.Empty;

    public string TeamName { get; set; } = string.Empty;

    /// <summary>Who is answering. Decides the wording of the header and which row is stored.</summary>
    public RespondentType Respondent { get; set; }

    /// <summary>Stored with the answers so it is knowable which wording was on screen.</summary>
    public string QuestionSetVersion { get; set; } = string.Empty;

    /// <summary>True when this person has already submitted for this player and round.</summary>
    public bool IsCorrection { get; set; }

    /// <summary>
    /// The answers, flat and in display order. This is the part that is model-bound.
    /// <see cref="Sections"/> points into this list by index.
    /// </summary>
    public List<QuestionInput> Answers { get; set; } = new();

    /// <summary>
    /// The reflection answers, in display order. Model-bound like <see cref="Answers"/>,
    /// and empty when the question set has no reflection section.
    /// <see cref="Reflection"/> points into this list by index.
    /// </summary>
    public List<ReflectionInput> ReflectionAnswers { get; set; } = new();

    // ---- Rendering only. Never bound from the request: everything below is rebuilt from
    // ---- the question catalog, so a tampered form cannot change what the questions say.

    public AnswerScale Scale { get; set; } = new();

    public IReadOnlyList<Section> Sections { get; set; } = Array.Empty<Section>();

    /// <summary>
    /// The reflection as it is laid out on the page, or null when the question set has none.
    /// Rebuilt from the catalog on every render, exactly like <see cref="Sections"/>.
    /// </summary>
    public ReflectionBlock? Reflection { get; set; }

    public int QuestionCount => Answers.Count;

    public int AnsweredCount => Answers.Count(a => a.Value.HasValue);

    /// <summary>The instruction shown above the form. Says who answers, and about whom.</summary>
    public string Instruction => Respondent switch
    {
        RespondentType.Player =>
            "Answer for how you are doing yourself. There are no right answers.",
        RespondentType.Coach =>
            $"You are answering as coach about player {PlayerCode}. Answer as you believe the player would.",
        RespondentType.Guardian =>
            $"You are answering as guardian about player {PlayerCode}. Answer as you believe your child would.",
        _ => string.Empty
    };

    /// <summary>One answer. The only two things the browser gets to send back.</summary>
    public sealed class QuestionInput
    {
        /// <summary>
        /// Key from the question set, e.g. "commitment-1". Validated against the catalog on
        /// POST -- a key that is not in the file is a rejected submission, not a new question.
        /// </summary>
        public string QuestionKey { get; set; } = string.Empty;

        /// <summary>
        /// 1-5, or null for unanswered. Null is not 3: an unanswered question stays out of
        /// every mean instead of being pulled to the middle of the scale.
        /// </summary>
        [Range(FiveCRules.ScaleMin, FiveCRules.ScaleMax,
            ErrorMessage = "Choose a value between 1 and 5.")]
        public int? Value { get; set; }
    }

    /// <summary>One of the five C's as it is laid out on the page.</summary>
    /// <param name="Key">Category key, e.g. "commitment".</param>
    /// <param name="Name">Heading, e.g. "Commitment".</param>
    /// <param name="Description">One sentence about what the category covers.</param>
    /// <param name="Questions">The statements in this category, in display order.</param>
    public sealed record Section(
        string Key,
        string Name,
        string Description,
        IReadOnlyList<SectionQuestion> Questions);

    /// <summary>One statement on the page, pointing at its slot in <see cref="Answers"/>.</summary>
    /// <param name="Index">Index into <see cref="Answers"/>, for the form field name.</param>
    /// <param name="Number">Running number across the whole form, 1-25. Display only.</param>
    /// <param name="Text">The statement, as this respondent should read it.</param>
    /// <param name="Reversed">
    /// Negatively worded. Shown exactly as written -- the reversal is a scoring rule in
    /// <see cref="FiveCRules.Score"/> and is not something the respondent should notice.
    /// Flipping the scale in the form to compensate would reverse it twice.
    /// </param>
    public sealed record SectionQuestion(
        int Index,
        int Number,
        string Text,
        bool Reversed);

    /// <summary>
    /// One reflection answer. Words rather than a number, and the only two things the
    /// browser gets to send back for it.
    /// </summary>
    public sealed class ReflectionInput
    {
        /// <summary>
        /// Key from the question set, e.g. "reflection-strength". Validated against the
        /// catalog on POST, exactly as a statement key is.
        /// </summary>
        public string QuestionKey { get; set; } = string.Empty;

        /// <summary>
        /// A category key for a choice between the five C's, or what was written for a
        /// written question. Null or blank means the question was left alone.
        ///
        /// The attribute is the ceiling the column can hold. The real limit is per question
        /// and comes from the question set -- see <see cref="ReflectionField.MaxLength"/> --
        /// and is checked in the controller, which is the side that knows which question
        /// this is.
        /// </summary>
        [StringLength(FiveCRules.ReflectionTextLimit)]
        public string? Value { get; set; }
    }

    /// <summary>The reflection as one block on the page.</summary>
    /// <param name="Title">Heading, e.g. "End of period". Also the tab label.</param>
    /// <param name="Description">One or two sentences about what the section is for.</param>
    /// <param name="Questions">The reflection questions, in display order.</param>
    public sealed record ReflectionBlock(
        string Title,
        string Description,
        IReadOnlyList<ReflectionField> Questions);

    /// <summary>
    /// One reflection question on the page, pointing at its slot in
    /// <see cref="ReflectionAnswers"/>.
    /// </summary>
    /// <param name="Index">Index into <see cref="ReflectionAnswers"/>, for the field name.</param>
    /// <param name="Number">Running number within the reflection. Display only.</param>
    /// <param name="Text">The question, as this respondent should read it.</param>
    /// <param name="IsCategoryChoice">
    /// True when it is answered by picking one of the five C's, false when it is written.
    /// </param>
    /// <param name="Required">Whether the form refuses to save without it.</param>
    /// <param name="MaxLength">Longest written answer accepted. Ignored for a choice.</param>
    /// <param name="Placeholder">Grey text in an empty field. Never a value.</param>
    /// <param name="Choices">
    /// The five C's to choose between, taken from the catalog so the choice cannot drift
    /// from the categories above it. Empty for a written question.
    /// </param>
    public sealed record ReflectionField(
        int Index,
        int Number,
        string Text,
        bool IsCategoryChoice,
        bool Required,
        int MaxLength,
        string? Placeholder,
        IReadOnlyList<ReflectionChoice> Choices);

    /// <summary>One of the five C's, as an option in a reflection question.</summary>
    /// <param name="Key">Category key, e.g. "confidence". This is what is stored.</param>
    /// <param name="Name">Heading, e.g. "Confidence". This is what is read.</param>
    public sealed record ReflectionChoice(string Key, string Name);
}
