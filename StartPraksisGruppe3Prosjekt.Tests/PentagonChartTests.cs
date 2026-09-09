using System.Globalization;
using StartPraksisGruppe3Prosjekt.Services.FiveC;
using StartPraksisGruppe3Prosjekt.ViewModels.FiveC;
using Xunit;

namespace StartPraksisGruppe3Prosjekt.Tests;

/// <summary>
/// The five C's drawn as one shape.
///
/// The case worth guarding hardest is the culture one. The application runs under a
/// Norwegian culture, where a double formats as "3,0" -- and an SVG points attribute uses
/// a comma to separate coordinates. A coordinate formatted with the current culture turns
/// one point into two numbers, and the polygon either disappears or is drawn somewhere
/// else. Nothing about that fails loudly, which is exactly why it is tested.
/// </summary>
public sealed class PentagonChartTests
{
    /// <summary>A category with the three means the chart reads.</summary>
    private sealed record Means(
        string Label,
        double? PlayerMean,
        double? GuardianMean,
        double? CoachMean) : IRespondentMeans
    {
        public bool NeedsFollowUp => false;
    }

    private static IReadOnlyList<IRespondentMeans> Five(
        double? player = 3.0,
        double? guardian = null,
        double? coach = 4.0) =>
        new[] { "Commitment", "Communication", "Concentration", "Control", "Confidence" }
            .Select(name => (IRespondentMeans)new Means(name, player, guardian, coach))
            .ToList();

    /// <summary>
    /// The one that breaks silently. Under nb-NO a bare ToString() writes "150,5", and the
    /// points attribute reads that as two coordinates -- so the assertion is not "the
    /// numbers are right" but "no coordinate pair contains more than one comma".
    /// </summary>
    [Fact]
    public void Coordinates_are_invariant_even_under_a_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nb-NO");

        try
        {
            var chart = PentagonChartViewModel.From(Five(player: 3.3, coach: 4.7))!;

            foreach (var points in chart.Shapes.Select(s => s.Points)
                         .Concat(chart.Rings.Select(r => r.Points)))
            {
                foreach (var pair in points.Split(' '))
                {
                    Assert.Equal(1, pair.Count(c => c == ','));

                    var parts = pair.Split(',');
                    Assert.True(double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
                    Assert.True(double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
                }
            }

            // The axis ends and the label positions go into their own attributes, where a
            // comma is not a separator -- but a comma decimal is still not a number to SVG.
            Assert.All(chart.Axes, axis =>
            {
                Assert.DoesNotContain(",", axis.X);
                Assert.DoesNotContain(",", axis.Y);
                Assert.DoesNotContain(",", axis.LabelX);
                Assert.DoesNotContain(",", axis.LabelY);
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// A role is drawn only when it has a number in every category. A polygon has to put
    /// every vertex somewhere, and the only place a missing one could go is the centre --
    /// which would draw "nobody answered this C" as "scored the bottom of the scale".
    /// </summary>
    [Fact]
    public void A_role_missing_one_category_is_named_rather_than_drawn_at_zero()
    {
        var categories = Five().ToList();
        categories[2] = new Means("Concentration", PlayerMean: null, GuardianMean: null, CoachMean: 4.0);

        var chart = PentagonChartViewModel.From(categories)!;

        Assert.DoesNotContain(chart.Shapes, s => s.RoleName == "Player");
        Assert.Contains("Player", chart.MissingRoles);

        // The coach still has all five and is still drawn.
        Assert.Contains(chart.Shapes, s => s.RoleName == "Coach");
    }

    /// <summary>
    /// A radius of value / 5, so the bottom of the scale is a small shape and not a point.
    /// (value - 1) / 4 would draw an average of 1 at the centre -- indistinguishable from
    /// the category nobody answered, which is the one thing this chart must not do.
    /// </summary>
    [Fact]
    public void The_lowest_possible_average_is_still_a_visible_shape()
    {
        var chart = PentagonChartViewModel.From(Five(player: 1.0, coach: null))!;

        var shape = Assert.Single(chart.Shapes);
        var vertices = shape.Points.Split(' ')
            .Select(pair => pair.Split(','))
            .Select(p => (
                X: double.Parse(p[0], CultureInfo.InvariantCulture),
                Y: double.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();

        Assert.All(vertices, v =>
        {
            var distance = Math.Sqrt(Math.Pow(v.X - 150, 2) + Math.Pow(v.Y - 150, 2));

            // A fifth of the full radius, which is 100.
            Assert.Equal(20, distance, precision: 1);
        });
    }

    [Fact]
    public void There_is_one_axis_per_category_and_one_ring_per_point_of_the_scale()
    {
        var chart = PentagonChartViewModel.From(Five())!;

        Assert.Equal(5, chart.Axes.Count);
        Assert.Equal(5, chart.Rings.Count);
        Assert.Equal(
            new[] { "Commitment", "Communication", "Concentration", "Control", "Confidence" },
            chart.Axes.Select(a => a.Label));

        // The first axis points straight up, because that is where a reader looks first and
        // it should be the first C rather than wherever an angle of zero lands.
        Assert.Equal("150", chart.Axes[0].X);
        Assert.Equal("50", chart.Axes[0].Y);
    }

    /// <summary>
    /// Two axes is a line, not a shape. Returning null lets the page leave the chart out
    /// rather than drawing something that cannot be read.
    /// </summary>
    [Fact]
    public void Fewer_than_three_categories_is_not_a_shape()
    {
        Assert.Null(PentagonChartViewModel.From(Five().Take(2).ToList()));
        Assert.NotNull(PentagonChartViewModel.From(Five().Take(3).ToList()));
    }

    /// <summary>
    /// The accessible name carries the numbers. A chart that is only a picture is a chart
    /// some readers do not get at all -- and this one is the reason to have the shape.
    /// </summary>
    [Fact]
    public void Each_shape_reads_out_its_own_numbers()
    {
        var chart = PentagonChartViewModel.From(Five(player: 3.5, coach: null))!;

        var shape = Assert.Single(chart.Shapes);

        Assert.Contains("Commitment 3.5", shape.Description);
        Assert.Contains("Confidence 3.5", shape.Description);
    }
}
