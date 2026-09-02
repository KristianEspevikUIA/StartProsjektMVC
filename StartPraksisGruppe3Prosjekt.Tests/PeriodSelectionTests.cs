using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Services;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// Which period the user is looking at. Picking one on the form list and then opening the
/// team overview used to throw the choice away, so the order the rules are applied in --
/// URL, then remembered, then current -- is the thing worth holding still.
/// </summary>
public class PeriodSelectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly SurveyRound Closed =
        new() { Id = 1, Name = "Spring 2026", OpensAt = Now.AddDays(-90), ClosesAt = Now.AddDays(-60) };

    private static readonly SurveyRound OpenClosingSoon =
        new() { Id = 2, Name = "Summer 2026", OpensAt = Now.AddDays(-30), ClosesAt = Now.AddDays(3) };

    private static readonly SurveyRound OpenClosingLater =
        new() { Id = 3, Name = "Autumn 2026", OpensAt = Now.AddDays(-1), ClosesAt = Now.AddDays(60) };

    private static readonly IReadOnlyList<SurveyRound> Rounds =
        new[] { Closed, OpenClosingSoon, OpenClosingLater };

    private static PeriodSelection Selection(FakeHttpContextAccessor http) =>
        new(new UnusablePeriodService(), http, new FakeWebHostEnvironment(AppContext.BaseDirectory));

    [Fact]
    public void A_period_named_in_the_url_wins_and_is_remembered()
    {
        // A shared link has to mean what it says, even when the reader has a different
        // period remembered from last time.
        var http = new FakeHttpContextAccessor((PeriodSelection.CookieName, OpenClosingLater.Id.ToString()));

        var selected = Selection(http).Select(Rounds, requestedRoundId: Closed.Id);

        Assert.Equal(Closed.Id, selected!.Id);
        Assert.Equal(Closed.Id.ToString(), http.ResponseSetCookies[PeriodSelection.CookieName]);
    }

    [Fact]
    public void A_remembered_period_is_used_when_the_url_says_nothing()
    {
        var http = new FakeHttpContextAccessor((PeriodSelection.CookieName, OpenClosingSoon.Id.ToString()));

        var selected = Selection(http).Select(Rounds, requestedRoundId: null);

        Assert.Equal(OpenClosingSoon.Id, selected!.Id);
    }

    [Fact]
    public void A_remembered_period_that_no_longer_exists_is_ignored()
    {
        // A period that has since been deleted must not turn into a 404 on a page nobody
        // asked for. The stale cookie is cleared rather than left to fail again.
        var http = new FakeHttpContextAccessor((PeriodSelection.CookieName, "999"));

        var selected = Selection(http).Select(Rounds, requestedRoundId: null);

        Assert.Equal(OpenClosingLater.Id, selected!.Id);
        Assert.True(http.ResponseSetCookies.ContainsKey(PeriodSelection.CookieName));
    }

    [Fact]
    public void A_url_naming_a_period_that_does_not_exist_falls_through()
    {
        var http = new FakeHttpContextAccessor();

        var selected = Selection(http).Select(Rounds, requestedRoundId: 999);

        Assert.Equal(OpenClosingLater.Id, selected!.Id);
    }

    [Fact]
    public void With_nothing_remembered_the_current_period_is_the_open_one_closing_last()
    {
        var selected = Selection(new FakeHttpContextAccessor()).Select(Rounds, requestedRoundId: null);

        Assert.Equal(OpenClosingLater.Id, selected!.Id);
    }

    [Fact]
    public void With_nothing_open_the_current_period_is_the_most_recent()
    {
        var onlyClosed = new[]
        {
            Closed,
            new SurveyRound
            {
                Id = 4, Name = "Winter 2025", OpensAt = Now.AddDays(-200), ClosesAt = Now.AddDays(-150)
            }
        };

        var selected = Selection(new FakeHttpContextAccessor()).Select(onlyClosed, requestedRoundId: null);

        Assert.Equal(Closed.Id, selected!.Id);
    }

    [Fact]
    public void No_periods_means_no_selection() =>
        Assert.Null(Selection(new FakeHttpContextAccessor()).Select(Array.Empty<SurveyRound>(), null));

    [Fact]
    public void Selecting_from_a_loaded_list_never_goes_back_to_the_database()
    {
        // The form list already has every period in hand for its picker. Asking
        // IPeriodService again was a second query for a list that was already there, which
        // is what Select exists to avoid -- UnusablePeriodService throws if it is touched.
        var selection = Selection(new FakeHttpContextAccessor());

        var exception = Record.Exception(() => selection.Select(Rounds, requestedRoundId: null));

        Assert.Null(exception);
    }

    /// <summary>An IPeriodService that fails the test if anything asks it a question.</summary>
    private sealed class UnusablePeriodService : IPeriodService
    {
        public Task<IReadOnlyList<SurveyRound>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Select must work from the list it was given.");

        public Task<SurveyRound?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Select must work from the list it was given.");

        public Task<IReadOnlyDictionary<int, int>> GetSubmissionCountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Select must work from the list it was given.");

        public Task<PeriodResult> CreateAsync(
            string name,
            DateTimeOffset opensAt,
            DateTimeOffset closesAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Select does not create periods.");

        public Task<PeriodResult> CloseNowAsync(int roundId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Select does not close periods.");
    }
}
