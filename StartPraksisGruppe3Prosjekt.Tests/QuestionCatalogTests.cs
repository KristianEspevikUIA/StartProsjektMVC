using Microsoft.Extensions.Logging.Abstractions;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The question set is a data file the coaching team is expected to edit, and a bad edit
/// stops the application. That is deliberate -- but it means the file is worth checking in
/// CI rather than at the next startup.
///
/// The first test loads THE file the application ships (linked into the test output, see the
/// csproj). The rest use small broken files to show what the validation actually catches.
/// </summary>
public class QuestionCatalogTests
{
    private static QuestionCatalog Load(string contentRoot) =>
        new(new FakeWebHostEnvironment(contentRoot), NullLogger<QuestionCatalog>.Instance);

    [Fact]
    public void The_shipped_question_set_loads()
    {
        var catalog = Load(AppContext.BaseDirectory);

        Assert.False(string.IsNullOrWhiteSpace(catalog.Questions.Version));
        Assert.NotEmpty(catalog.Questions.Categories);
        Assert.All(catalog.Questions.Categories, category => Assert.NotEmpty(category.Questions));

        // Answers are stored against the key, so a duplicate key would silently merge two
        // different statements.
        var keys = catalog.Questions.AllQuestions.Select(q => q.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_question_is_findable_by_key_and_knows_its_category()
    {
        var catalog = Load(AppContext.BaseDirectory);

        foreach (var category in catalog.Questions.Categories)
        {
            foreach (var question in category.Questions)
            {
                Assert.NotNull(catalog.FindQuestion(question.Key));
                Assert.Equal(category.Key, catalog.FindCategoryForQuestion(question.Key)?.Key);
            }
        }
    }

    [Fact]
    public void A_duplicate_question_key_is_named_in_the_error()
    {
        using var content = new TemporaryQuestionSet("""
        {
          "version": "test",
          "scale": { "min": 1, "max": 2, "options": [
            { "value": 1, "label": "Low" }, { "value": 2, "label": "High" } ] },
          "categories": [
            { "key": "commitment", "name": "Commitment", "questions": [
              { "key": "commitment-1", "text": "One" },
              { "key": "commitment-1", "text": "Also one" } ] }
          ]
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root));

        Assert.Contains("commitment-1", error.Message);
    }

    [Fact]
    public void A_scale_that_does_not_cover_its_own_range_is_rejected()
    {
        using var content = new TemporaryQuestionSet("""
        {
          "version": "test",
          "scale": { "min": 1, "max": 5, "options": [
            { "value": 1, "label": "Low" }, { "value": 2, "label": "High" } ] },
          "categories": [
            { "key": "commitment", "name": "Commitment", "questions": [
              { "key": "commitment-1", "text": "One" } ] }
          ]
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root));

        Assert.Contains("scale.options", error.Message);
    }

    [Fact]
    public void The_shipped_reflection_loads_and_every_question_is_findable()
    {
        var catalog = Load(AppContext.BaseDirectory);

        // The section is optional in the format but present in the file we ship, and the
        // form is built from it -- so an edit that drops it should fail here, not quietly
        // remove five questions from the end of the questionnaire.
        Assert.NotNull(catalog.Questions.Reflection);
        Assert.NotEmpty(catalog.Questions.ReflectionQuestions);

        foreach (var question in catalog.Questions.ReflectionQuestions)
        {
            Assert.NotNull(catalog.FindReflectionQuestion(question.Key));

            // The two kinds are answered by different controls and validated differently.
            // Anything else has no meaning on the page.
            Assert.True(ReflectionQuestionTypes.IsKnown(question.Type));
        }

        // A C to pick has to be a C that was asked about. The choices are built from the
        // categories, so this holds by construction -- and stops holding the moment somebody
        // hard-codes a list somewhere.
        Assert.Contains(catalog.Questions.ReflectionQuestions, q => q.IsCategoryChoice);
    }

    [Fact]
    public void A_reflection_question_with_an_unknown_type_is_rejected()
    {
        using var content = new TemporaryQuestionSet("""
        {
          "version": "test",
          "scale": { "min": 1, "max": 2, "options": [
            { "value": 1, "label": "Low" }, { "value": 2, "label": "High" } ] },
          "categories": [
            { "key": "commitment", "name": "Commitment", "questions": [
              { "key": "commitment-1", "text": "One" } ] }
          ],
          "reflection": { "title": "End of period", "questions": [
            { "key": "reflection-strength", "type": "dropdown", "text": "Which C?" } ] }
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root));

        // The key and the typo both, because the person reading this is editing JSON.
        Assert.Contains("reflection-strength", error.Message);
        Assert.Contains("dropdown", error.Message);
    }

    [Fact]
    public void A_reflection_key_that_repeats_a_statement_key_is_rejected()
    {
        using var content = new TemporaryQuestionSet("""
        {
          "version": "test",
          "scale": { "min": 1, "max": 2, "options": [
            { "value": 1, "label": "Low" }, { "value": 2, "label": "High" } ] },
          "categories": [
            { "key": "commitment", "name": "Commitment", "questions": [
              { "key": "commitment-1", "text": "One" } ] }
          ],
          "reflection": { "title": "End of period", "questions": [
            { "key": "commitment-1", "type": "text", "text": "Say more." } ] }
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root));

        // Two answers, two tables, one key. Whichever way that is read afterwards, one of
        // the two questions is being reported as the other.
        Assert.Contains("commitment-1", error.Message);
    }

    [Fact]
    public void A_written_answer_may_not_ask_for_more_room_than_the_column_has()
    {
        using var content = new TemporaryQuestionSet("""
        {
          "version": "test",
          "scale": { "min": 1, "max": 2, "options": [
            { "value": 1, "label": "Low" }, { "value": 2, "label": "High" } ] },
          "categories": [
            { "key": "commitment", "name": "Commitment", "questions": [
              { "key": "commitment-1", "text": "One" } ] }
          ],
          "reflection": { "title": "End of period", "questions": [
            { "key": "reflection-support", "type": "text", "maxLength": 9000,
              "text": "What would help?" } ] }
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(content.Root));

        Assert.Contains("reflection-support", error.Message);
        Assert.Contains(FiveCRules.ReflectionTextLimit.ToString(), error.Message);
    }

    [Fact]
    public void A_missing_file_says_which_path_was_tried()
    {
        var empty = Directory.CreateTempSubdirectory("startcompass-tests-");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => Load(empty.FullName));

            Assert.Contains(QuestionCatalog.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                error.Message);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    /// <summary>A content root holding one question set file, deleted when the test ends.</summary>
    private sealed class TemporaryQuestionSet : IDisposable
    {
        private readonly DirectoryInfo _root;

        public TemporaryQuestionSet(string json)
        {
            _root = Directory.CreateTempSubdirectory("startcompass-tests-");

            var file = Path.Combine(_root.FullName, QuestionCatalog.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, json);
        }

        public string Root => _root.FullName;

        public void Dispose() => _root.Delete(recursive: true);
    }
}
