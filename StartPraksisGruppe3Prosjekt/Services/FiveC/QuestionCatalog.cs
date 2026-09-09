using System.Text.Json;
using StartPraksisGruppe3Prosjekt.Models.FiveC;

namespace StartPraksisGruppe3Prosjekt.Services.FiveC;

/// <summary>
/// Loads Data/Questions/five-c-questions.json once and keeps it in memory.
///
/// Registered as a singleton: the file is content, it does not change while the app runs,
/// and re-reading it per request would be work for nothing.
///
/// The file is validated on load, and a bad file stops the application with a message that
/// names the problem. That is deliberate. The alternative -- starting anyway -- means the
/// error surfaces as a half-empty form in the middle of a round, to a 14-year-old.
/// </summary>
public sealed class QuestionCatalog : IQuestionCatalog
{
    /// <summary>Relative to the content root, so it resolves the same in dev and when published.</summary>
    public const string RelativePath = "Data/Questions/five-c-questions.json";

    /// <summary>The shape the 5C specification asks for. A deviation is logged, not fatal.</summary>
    private const int ExpectedCategoryCount = 5;
    private const int ExpectedQuestionsPerCategory = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyDictionary<string, Question> _questionsByKey;
    private readonly IReadOnlyDictionary<string, QuestionCategory> _categoriesByKey;
    private readonly IReadOnlyDictionary<string, QuestionCategory> _categoryByQuestionKey;
    private readonly IReadOnlyDictionary<string, ReflectionQuestion> _reflectionByKey;

    public QuestionCatalog(IWebHostEnvironment environment, ILogger<QuestionCatalog> logger)
    {
        // GetFullPath, not just Combine: RelativePath uses forward slashes, and Combine
        // leaves them alone. On Windows that produced "C:\...\Data/Questions/..." in the
        // error message below -- the one place this path is ever read by a person, and the
        // one place a mixed separator is confusing. Normalising it also makes the message
        // assertable on both platforms, which it was not.
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, RelativePath));

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The 5C question set was not found at '{path}'. The file is required: it is " +
                "the source of truth for the questionnaire.");
        }

        QuestionSet? parsed;
        try
        {
            using var stream = File.OpenRead(path);
            parsed = JsonSerializer.Deserialize<QuestionSet>(stream, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The 5C question set at '{path}' is not valid JSON: {ex.Message}", ex);
        }

        Questions = parsed ?? throw new InvalidOperationException(
            $"The 5C question set at '{path}' is empty.");

        Validate(Questions, path);

        _categoriesByKey = Questions.Categories.ToDictionary(
            c => c.Key, StringComparer.OrdinalIgnoreCase);

        _questionsByKey = Questions.AllQuestions.ToDictionary(
            q => q.Key, StringComparer.OrdinalIgnoreCase);

        _categoryByQuestionKey = Questions.Categories
            .SelectMany(c => c.Questions.Select(q => (q.Key, Category: c)))
            .ToDictionary(pair => pair.Key, pair => pair.Category, StringComparer.OrdinalIgnoreCase);

        _reflectionByKey = Questions.ReflectionQuestions.ToDictionary(
            q => q.Key, StringComparer.OrdinalIgnoreCase);

        var questionCount = _questionsByKey.Count;

        if (Questions.Categories.Count != ExpectedCategoryCount
            || questionCount != ExpectedCategoryCount * ExpectedQuestionsPerCategory)
        {
            // Not an error: the coaching team may well land on a different number. Worth
            // saying out loud, because the usual cause is a question lost in an edit.
            logger.LogWarning(
                "The 5C question set has {CategoryCount} categories and {QuestionCount} questions. " +
                "The expected shape is {ExpectedCategories} x {ExpectedPerCategory}.",
                Questions.Categories.Count,
                questionCount,
                ExpectedCategoryCount,
                ExpectedQuestionsPerCategory);
        }

        logger.LogInformation(
            "Loaded 5C question set '{Version}': {CategoryCount} categories, {QuestionCount} questions, " +
            "{ReflectionCount} reflection questions.",
            Questions.Version,
            Questions.Categories.Count,
            questionCount,
            _reflectionByKey.Count);
    }

    /// <inheritdoc />
    public QuestionSet Questions { get; }

    /// <inheritdoc />
    public Question? FindQuestion(string key) =>
        _questionsByKey.TryGetValue(key, out var question) ? question : null;

    /// <inheritdoc />
    public QuestionCategory? FindCategory(string key) =>
        _categoriesByKey.TryGetValue(key, out var category) ? category : null;

    /// <inheritdoc />
    public QuestionCategory? FindCategoryForQuestion(string questionKey) =>
        _categoryByQuestionKey.TryGetValue(questionKey, out var category) ? category : null;

    /// <inheritdoc />
    public ReflectionQuestion? FindReflectionQuestion(string key) =>
        _reflectionByKey.TryGetValue(key, out var question) ? question : null;

    /// <summary>
    /// Everything that has to hold for the form to be answerable and the answers readable.
    /// Every message names the file and the offending key, because whoever broke it is
    /// most likely editing JSON rather than reading C#.
    /// </summary>
    private static void Validate(QuestionSet set, string path)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(set.Version))
        {
            problems.Add(
                "'version' is missing. It records which wording a submission was answered against.");
        }

        if (set.Scale.Min >= set.Scale.Max)
        {
            problems.Add(
                $"'scale.min' ({set.Scale.Min}) must be lower than 'scale.max' ({set.Scale.Max}).");
        }

        var expectedValues = Enumerable.Range(
            set.Scale.Min,
            Math.Max(0, set.Scale.Max - set.Scale.Min + 1));

        var actualValues = set.Scale.Options.Select(o => o.Value).ToList();

        if (!expectedValues.SequenceEqual(actualValues))
        {
            problems.Add(
                $"'scale.options' must list every value from {set.Scale.Min} to {set.Scale.Max}, in order. " +
                $"Found: {(actualValues.Count == 0 ? "nothing" : string.Join(", ", actualValues))}.");
        }

        if (set.Scale.Options.Any(o => string.IsNullOrWhiteSpace(o.Label)))
        {
            problems.Add(
                "Every entry in 'scale.options' needs a 'label'. The scale is not answerable as bare numbers.");
        }

        if (set.Categories.Count == 0)
        {
            problems.Add("'categories' is empty. The questionnaire needs at least one category.");
        }

        foreach (var duplicate in Duplicates(set.Categories.Select(c => c.Key)))
        {
            problems.Add(
                $"The category key '{duplicate}' is used more than once. Category keys must be unique.");
        }

        // Statements and reflection questions are checked together: they are stored in
        // different tables, but both are stored against this key, and two questions sharing
        // one is the kind of edit that reads fine and merges two answers.
        var answerKeys = set.AllQuestions.Select(q => q.Key)
            .Concat(set.ReflectionQuestions.Select(q => q.Key));

        foreach (var duplicate in Duplicates(answerKeys))
        {
            problems.Add(
                $"The question key '{duplicate}' is used more than once. Answers are stored against " +
                "this key, so it has to be unique across the whole file.");
        }

        foreach (var category in set.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Key))
            {
                problems.Add($"A category is missing 'key' (name: '{category.Name}').");
            }

            if (string.IsNullOrWhiteSpace(category.Name))
            {
                problems.Add(
                    $"Category '{category.Key}' is missing 'name'. It is the heading shown above the questions.");
            }

            if (category.Questions.Count == 0)
            {
                problems.Add($"Category '{category.Key}' has no questions.");
            }

            foreach (var question in category.Questions)
            {
                if (string.IsNullOrWhiteSpace(question.Key))
                {
                    problems.Add($"A question in '{category.Key}' is missing 'key'.");
                }

                if (string.IsNullOrWhiteSpace(question.Text))
                {
                    problems.Add($"Question '{question.Key}' in '{category.Key}' is missing 'text'.");
                }
            }
        }

        // The reflection section is optional. Present, it has to be answerable: a question
        // whose 'type' is a typo would otherwise render as an input nobody can fill in.
        foreach (var question in set.ReflectionQuestions)
        {
            if (string.IsNullOrWhiteSpace(question.Key))
            {
                problems.Add($"A reflection question is missing 'key' (text: '{question.Text}').");
            }

            if (string.IsNullOrWhiteSpace(question.Text))
            {
                problems.Add($"Reflection question '{question.Key}' is missing 'text'.");
            }

            if (!ReflectionQuestionTypes.IsKnown(question.Type))
            {
                problems.Add(
                    $"Reflection question '{question.Key}' has the unknown type '{question.Type}'. " +
                    $"Use '{ReflectionQuestionTypes.Category}' for a choice between the five C's, or " +
                    $"'{ReflectionQuestionTypes.Text}' for a written answer.");
            }

            if (!question.IsCategoryChoice
                && (question.MaxLength < 1 || question.MaxLength > FiveCRules.ReflectionTextLimit))
            {
                problems.Add(
                    $"Reflection question '{question.Key}' has 'maxLength' {question.MaxLength}. " +
                    $"It has to be between 1 and {FiveCRules.ReflectionTextLimit} -- the size of the " +
                    "column the answer is stored in.");
            }
        }

        if (set.Reflection is { } reflection
            && reflection.Questions.Count > 0
            && string.IsNullOrWhiteSpace(reflection.Title))
        {
            problems.Add(
                "'reflection.title' is missing. It is the heading over the section and the label on its tab.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The 5C question set at '{path}' cannot be used:{Environment.NewLine}  - " +
                string.Join($"{Environment.NewLine}  - ", problems));
        }
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> keys) =>
        keys.Where(k => !string.IsNullOrWhiteSpace(k))
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
}
