using System.ComponentModel.DataAnnotations;

namespace StartPraksisGruppe3Prosjekt.Models;

/// <summary>
/// One submitted 5C form, stored in the database.
///
/// These tables exist because the app now talks to the Supabase Postgres database directly
/// through EF Core. The earlier arrangement -- posting over PostgREST to a database this
/// process is already connected to, with a second credential and no shared transaction --
/// was a workaround for the app running on local SQLite, and that is no longer the case.
/// The PostgREST store is still there for a genuinely separate Supabase project; see
/// docs/five-c.md.
///
/// Two rules the schema enforces rather than trusting callers to remember:
///
///   * One submission per (round, player, respondent). Answering again is a correction,
///     not a second opinion. That is the unique index in AppDbContext.
///   * <see cref="FiveCAnswer.Value"/> is nullable. Null means "not answered", and null is
///     not 3. A NOT NULL column would quietly turn every blank into a middling opinion.
/// </summary>
public class FiveCSubmission
{
    public int Id { get; set; }

    /// <summary>The period being answered.</summary>
    public int RoundId { get; set; }
    public SurveyRound? Round { get; set; }

    /// <summary>
    /// The player the answers are ABOUT -- also when a coach or guardian filled the form in.
    /// Never the respondent.
    /// </summary>
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>
    /// The club-internal pseudonymous code, e.g. "TS-08-16". Denormalised so a row is
    /// readable without joining back to a table of minors. Never a name.
    /// </summary>
    [Required]
    [StringLength(20)]
    [Display(Name = "Player code")]
    public string PlayerCode { get; set; } = string.Empty;

    /// <summary>
    /// Who answered: "player", "coach" or "guardian". Stored as the wire value from
    /// <see cref="Contracts.FiveC.SurveySubmission.Roles"/> rather than as an enum number,
    /// so the table reads plainly to anyone looking at it in Supabase.
    /// </summary>
    [Required]
    [StringLength(20)]
    [Display(Name = "Answered by")]
    public string RespondentRole { get; set; } = string.Empty;

    /// <summary>The signed-in user who actually filled the form in.</summary>
    [Required]
    [StringLength(450)]
    public string RespondentUserId { get; set; } = string.Empty;

    /// <summary>
    /// Which wording was on screen. Two periods answered against different question sets
    /// are not comparable, and this is what makes that visible afterwards.
    /// </summary>
    [Required]
    [StringLength(100)]
    [Display(Name = "Question set")]
    public string QuestionSetVersion { get; set; } = string.Empty;

    [Display(Name = "Submitted")]
    public DateTimeOffset SubmittedAt { get; set; }

    public List<FiveCAnswer> Answers { get; set; } = new();
}

/// <summary>One answer in a <see cref="FiveCSubmission"/>.</summary>
public class FiveCAnswer
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }
    public FiveCSubmission? Submission { get; set; }

    /// <summary>
    /// The stable key from Data/Questions/five-c-questions.json, e.g. "commitment-1".
    /// Not an index and not the question text: the text is expected to be rewritten.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string QuestionKey { get; set; } = string.Empty;

    /// <summary>
    /// Which of the five C's the question belongs to. Denormalised on purpose, so answers
    /// can be grouped per C without loading the question set.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string CategoryKey { get; set; } = string.Empty;

    /// <summary>
    /// The raw answer, 1-5, exactly as given. Null means not answered -- and null is NOT 3.
    /// Stored unreversed: negatively worded statements are flipped when they are read, so
    /// the raw answer survives a later change to which statements count as reversed.
    /// </summary>
    [Range(PlayerRules.ScaleMin, PlayerRules.ScaleMax)]
    [Display(Name = "Answer")]
    public int? Value { get; set; }
}
