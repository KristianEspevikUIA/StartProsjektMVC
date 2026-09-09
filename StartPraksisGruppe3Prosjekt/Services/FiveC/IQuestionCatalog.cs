using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Read access to the 5C question set.
///
/// Every view, controller and service that needs a question goes through here. Nothing
/// asks the file system, and no question text is written in a .cshtml file -- that is
/// what makes it possible to hand the coaching team a new set of 25 questions and change
/// nothing but Data/Questions/five-c-questions.json.
/// </summary>
public interface IQuestionCatalog
{
    /// <summary>The question set, loaded and validated once at startup.</summary>
    QuestionSet Questions { get; }

    /// <summary>The question with this key, or null if the file no longer contains it.</summary>
    Question? FindQuestion(string key);

    /// <summary>The category with this key, or null if the file no longer contains it.</summary>
    QuestionCategory? FindCategory(string key);

    /// <summary>
    /// The category a question belongs to, or null for an unknown key. Used when reading
    /// stored answers back: an answer knows its question key, not its category.
    /// </summary>
    QuestionCategory? FindCategoryForQuestion(string questionKey);

    /// <summary>
    /// The reflection question with this key, or null if the file no longer contains it.
    /// Separate from <see cref="FindQuestion"/> because the two are answered differently --
    /// a statement with a number, a reflection question with a C or with words.
    /// </summary>
    ReflectionQuestion? FindReflectionQuestion(string key);

    /// <summary>
    /// The palette name a question is marked with: its own colour if the file gives it one,
    /// otherwise its category's, otherwise the palette entry at the category's position.
    ///
    /// Resolved here rather than in a view so that the form, the statement table and the
    /// player page cannot mark the same statement with three different colours. An unknown
    /// key gets <see cref="QuestionColors.Fallback"/> -- a stored answer for a question the
    /// file no longer contains still has to render somewhere.
    /// </summary>
    string ColorForQuestion(string questionKey);

    /// <summary>The palette name for a category, resolved the same way.</summary>
    string ColorForCategory(string categoryKey);
}
