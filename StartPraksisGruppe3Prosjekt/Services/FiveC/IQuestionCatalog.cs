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
}
