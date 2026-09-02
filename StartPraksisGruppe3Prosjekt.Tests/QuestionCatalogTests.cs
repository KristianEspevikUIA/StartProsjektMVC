using Microsoft.Extensions.Logging.Abstractions;
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
