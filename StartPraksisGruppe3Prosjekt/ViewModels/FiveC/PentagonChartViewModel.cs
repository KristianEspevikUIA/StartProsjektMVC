using System.Globalization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.FiveC;
using StartPraksisGruppe3Prosjekt.Services.FiveC;

namespace StartPraksisGruppe3Prosjekt.ViewModels.FiveC;

/// <summary>
/// The five C's as a shape rather than as five separate bars.
///
/// One axis per category, running out from the middle, and one closed polygon per
/// respondent. The bars answer "how high is Commitment"; this answers "what shape is this
/// player" -- whether they are even across the five, or spiked in one and hollow in
/// another. It is the question a season plan is actually made against, and it is the one
/// question five separate bar charts are worst at.
///
/// EVERY NUMBER IS COMPUTED HERE AND NOT IN THE VIEW, for one reason above all: the app
/// runs under a Norwegian culture, where a double formats as "3,0". An SVG points attribute
/// separates its pairs with commas, so a coordinate written with the current culture turns
/// "150.5 40.2" into "150,5 40,2" -- four numbers where there should be two, and a polygon
/// that either vanishes or draws somewhere else entirely. Every coordinate below goes
/// through <see cref="Fixed"/>, which is invariant.
///
/// The other reason is the CSP: it has no unsafe-inline, so nothing here can arrive as a
/// style attribute. Geometry is markup -- points, cx, x1 -- and colour is a class.
/// </summary>
public sealed class PentagonChartViewModel
{
    /// <summary>Half the viewBox. The chart is square and drawn from the middle out.</summary>
    private const double Centre = 150;

    /// <summary>Distance from the centre to a full score. Leaves room for the labels.</summary>
    private const double Radius = 100;

    /// <summary>Where the category names sit, measured from the centre.</summary>
    private const double LabelRadius = Radius + 22;

    /// <summary>
    /// The viewBox, as the view writes it.
    ///
    /// Wider than the chart and shorter than it is wide, because the labels sit OUTSIDE the
    /// pentagon and SVG clips to the viewBox. The pentagon spans x 50-250; the box runs
    /// from -100 to 400, which leaves about 130 units either side -- enough for the longest
    /// of the five C's set in the display face, with room for a longer word in a question
    /// set that has not been written yet. Vertically it is trimmed to what the shape and
    /// its top and bottom labels actually occupy, so the chart is not centred inside a band
    /// of empty space.
    /// </summary>
    public const string ViewBox = "-100 4 500 262";

    private PentagonChartViewModel(
        IReadOnlyList<Axis> axes,
        IReadOnlyList<Ring> rings,
        IReadOnlyList<Shape> shapes,
        IReadOnlyList<string> missingRoles)
    {
        Axes = axes;
        Rings = rings;
        Shapes = shapes;
        MissingRoles = missingRoles;
    }

    /// <summary>One spoke per category, with the label at its far end.</summary>
    public IReadOnlyList<Axis> Axes { get; }

    /// <summary>The rings at each whole point of the scale, outermost first.</summary>
    public IReadOnlyList<Ring> Rings { get; }

    /// <summary>The polygons that could be drawn, in the usual role order.</summary>
    public IReadOnlyList<Shape> Shapes { get; }

    /// <summary>
    /// The roles that could NOT be drawn, named so the page can say why rather than
    /// quietly showing two polygons where a reader expects three.
    /// </summary>
    public IReadOnlyList<string> MissingRoles { get; }

    /// <summary>True when there is at least one polygon. False means an empty grid.</summary>
    public bool HasAnyShapes => Shapes.Count > 0;

    /// <summary>
    /// Builds the chart from the per-category means -- a player's five C's, or a squad's.
    ///
    /// A role is drawn only when it has a number for EVERY category. A polygon with a
    /// missing vertex has to put that vertex somewhere, and the only honest place is
    /// nowhere: dropping to the centre would draw a player as having zero commitment when
    /// what happened is that nobody answered the commitment questions. The role is named in
    /// <see cref="MissingRoles"/> instead.
    /// </summary>
    /// <param name="categories">
    /// The categories, in the order the question set lists them. Three or fewer is not a
    /// shape -- with two axes a "polygon" is a line -- and returns null.
    /// </param>
    public static PentagonChartViewModel? From(IReadOnlyList<IRespondentMeans> categories)
    {
        if (categories.Count < 3)
        {
            return null;
        }

        var axes = new List<Axis>(categories.Count);

        for (var i = 0; i < categories.Count; i++)
        {
            // Straight up for the first axis, then clockwise. Up is where a reader looks
            // first, and it should be the first C rather than whatever an angle of zero
            // happens to point at.
            var angle = -Math.PI / 2 + 2 * Math.PI * i / categories.Count;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            axes.Add(new Axis(
                Label: categories[i].Label,
                X: Fixed(Centre + Radius * cos),
                Y: Fixed(Centre + Radius * sin),
                LabelX: Fixed(Centre + LabelRadius * cos),
                LabelY: Fixed(Centre + LabelRadius * sin),
                // A label on the left of the chart has to end at the axis, one on the
                // right has to start at it, and one straight above or below is centred.
                // Left as text-anchor rather than nudged by hand, so a longer category
                // name grows away from the chart instead of over it.
                Anchor: Math.Abs(cos) < 0.2 ? "middle" : cos > 0 ? "start" : "end",
                // The top and bottom labels would otherwise sit on the axis they name.
                Baseline: Math.Abs(cos) < 0.2 ? (sin < 0 ? "auto" : "hanging") : "middle"));
        }

        // Outermost first, so the faintest ring is drawn last and sits on top of nothing.
        var rings = Enumerable.Range(1, FiveCRules.ScaleMax)
            .Reverse()
            .Select(value => new Ring(
                Value: value,
                Points: Polygon(categories.Count, _ => (double)value)))
            .ToList();

        var roles = new (RespondentType Role, string Css, Func<IRespondentMeans, double?> Mean)[]
        {
            (RespondentType.Player, "player", m => m.PlayerMean),
            (RespondentType.Guardian, "guardian", m => m.GuardianMean),
            (RespondentType.Coach, "coach", m => m.CoachMean)
        };

        var shapes = new List<Shape>(roles.Length);
        var missing = new List<string>(roles.Length);

        foreach (var (role, css, mean) in roles)
        {
            var values = categories.Select(mean).ToList();

            if (values.Any(value => value is null))
            {
                // Not "they scored zero" and not "there is no such role" -- the page says
                // which of those it is in words, next to the chart.
                missing.Add(RespondentGap.DisplayName(role));
                continue;
            }

            shapes.Add(new Shape(
                RoleName: RespondentGap.DisplayName(role),
                Css: css,
                Points: Polygon(categories.Count, i => values[i]!.Value),
                // Read out for anyone who cannot see the shape. The same numbers are in the
                // bars below, but a chart that is only a picture is a chart some readers do
                // not get at all.
                Description: string.Join(", ", categories.Select((category, i) =>
                    $"{category.Label} {values[i]!.Value.ToString("0.0", CultureInfo.InvariantCulture)}"))));
        }

        return new PentagonChartViewModel(axes, rings, shapes, missing);
    }

    /// <summary>
    /// The closed polygon for one set of values, as an SVG points attribute.
    ///
    /// The radius is value / ScaleMax, the same fraction the bar chart uses for its width.
    /// (value - 1) / (max - 1) would stretch the bottom of the scale and draw an average of
    /// 1 as a point at the centre -- indistinguishable from an unanswered category, which
    /// is the one thing this chart must not do.
    /// </summary>
    private static string Polygon(int axisCount, Func<int, double> valueAt)
    {
        var points = new List<string>(axisCount);

        for (var i = 0; i < axisCount; i++)
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * i / axisCount;
            var radius = Radius * valueAt(i) / FiveCRules.ScaleMax;

            points.Add(
                $"{Fixed(Centre + radius * Math.Cos(angle))},{Fixed(Centre + radius * Math.Sin(angle))}");
        }

        return string.Join(" ", points);
    }

    /// <summary>
    /// A coordinate, invariant and to two decimals. Invariant is not a nicety here: under
    /// nb-NO the decimal separator is a comma, and a comma inside an SVG points attribute
    /// is a coordinate separator.
    /// </summary>
    private static string Fixed(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <param name="Label">The category name, e.g. "Commitment".</param>
    /// <param name="X">Outer end of the spoke.</param>
    /// <param name="Y">Outer end of the spoke.</param>
    /// <param name="LabelX">Where the name sits, just beyond the spoke.</param>
    /// <param name="LabelY">Where the name sits, just beyond the spoke.</param>
    /// <param name="Anchor">SVG text-anchor: start, middle or end.</param>
    /// <param name="Baseline">SVG dominant-baseline, so a top label clears its own axis.</param>
    public sealed record Axis(
        string Label,
        string X,
        string Y,
        string LabelX,
        string LabelY,
        string Anchor,
        string Baseline);

    /// <param name="Value">Which point of the 1-5 scale this ring marks.</param>
    /// <param name="Points">The ring, as an SVG points attribute.</param>
    public sealed record Ring(int Value, string Points);

    /// <summary>One respondent's outline across every category.</summary>
    /// <param name="RoleName">Whose shape this is, e.g. "Coach".</param>
    /// <param name="Css">"player", "guardian" or "coach" -- the suffix in startcompass.css.</param>
    /// <param name="Points">The polygon, as an SVG points attribute.</param>
    /// <param name="Description">The same shape in words, for the accessible name.</param>
    public sealed record Shape(string RoleName, string Css, string Points, string Description);
}
