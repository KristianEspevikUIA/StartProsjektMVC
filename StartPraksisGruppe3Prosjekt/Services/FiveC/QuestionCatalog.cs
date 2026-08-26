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

    public QuestionCatalog(IWebHostEnvironment environment, ILogger<QuestionCatalog> logger)
    {
        var path = Path.Combine(environment.ContentRootPath, RelativePath);

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
            "Loaded 5C question set '{Version}': {CategoryCount} categories, {QuestionCount} questions.",
            Questions.Version,
            Questions.Categories.Count,
            questionCount);
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

        foreach (var duplicate in Duplicates(set.AllQuestions.Select(q => q.Key)))
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
