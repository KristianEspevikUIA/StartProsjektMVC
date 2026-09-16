using StartPraksisGruppe3Prosjekt.Models.Identity;
using StartPraksisGruppe3Prosjekt.ViewModels.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>
/// Key Tactical Insights: three or four sentences written from the numbers on the page.
///
/// Every sentence is a template filled with a value, the Gold Standard range and -- for the
/// player-level markers -- who contributed most. Nothing is inferred beyond that: no marker
/// that is not measured is mentioned, and no adjective says more than the status already does.
/// The club's comparison document went further ("textbook defending forward aggression") on
/// the strength of a pressure count; that is exactly the step this does not take.
///
/// Order: up to two strengths first, then up to two areas to work on, filled up to four from
/// whichever side has more.
/// </summary>
public static class IdentityInsightsBuilder
{
    public const int MaxInsights = 4;
    private const int PerSide = 2;
    private const int LeadersNamed = 3;

    public static IReadOnlyList<IdentityInsight> Build(
        IReadOnlyList<MarkerReading> readings,
        IReadOnlyList<IdentityMatch> selectedMatches)
    {
        var measured = readings.Where(r => r.IsMeasured).ToList();

        var strengths = measured
            .Where(r => r.Status >= IdentityStatus.EliteAlignment)
            .OrderByDescending(r => r.Status)
            .ThenByDescending(r => Relative(r))
            .ToList();

        var toWorkOn = measured
            .Where(r => r.Status < IdentityStatus.EliteAlignment)
            .OrderBy(r => r.Status)
            .ThenBy(r => Relative(r))
            .ToList();

        var chosenStrengths = strengths.Take(PerSide).ToList();
        var chosenToWorkOn = toWorkOn.Take(PerSide).ToList();

        // Four slots. If one side has fewer than two, the other side fills in.
        var spare = MaxInsights - chosenStrengths.Count - chosenToWorkOn.Count;
        chosenStrengths.AddRange(strengths.Skip(PerSide).Take(spare));
        spare = MaxInsights - chosenStrengths.Count - chosenToWorkOn.Count;
        chosenToWorkOn.AddRange(toWorkOn.Skip(PerSide).Take(spare));

        return chosenStrengths
            .Concat(chosenToWorkOn)
            .Select(r => new IdentityInsight(Label(r.Status!.Value), r.Marker.Name, Sentence(r, selectedMatches), r.Status!.Value))
            .ToList();
    }

    public static string Label(IdentityStatus status) => status switch
    {
        IdentityStatus.Exceptional or IdentityStatus.EliteAlignment => "Strength",
        IdentityStatus.StrongAlignment => "Close to target",
        _ => "Growth area"
    };

    private static string Sentence(MarkerReading reading, IReadOnlyList<IdentityMatch> selectedMatches)
    {
        var marker = reading.Marker;
        var target = marker.Target;
        var value = reading.Value!.Value;
        var isAverage = selectedMatches.Count > 1;

        var sentence = $"{reading.DisplayValue}{(isAverage ? " on average" : string.Empty)} — ";

        sentence += (reading.Status, target.Direction) switch
        {
            (IdentityStatus.Exceptional, TargetDirection.LowerIsBetter) =>
                $"at or below {IdentityFormat.Value(target.ExceptionalAtOrBelow!.Value, target.Unit, isAverage: false)}, " +
                $"beyond the elite range ({marker.EliteRange}).",
            (IdentityStatus.Exceptional, _) =>
                $"above the elite range of {marker.EliteRange}.",
            (IdentityStatus.EliteAlignment, _) =>
                $"inside the elite range of {marker.EliteRange}.",
            (_, TargetDirection.LowerIsBetter) =>
                $"{IdentityFormat.Gap(Math.Round(value - target.Max!.Value, 1), target.Unit)} above the elite ceiling ({marker.EliteRange}).",
            _ =>
                $"{IdentityFormat.Gap(Math.Round(target.Min!.Value - value, 1), target.Unit)} short of the elite range of {marker.EliteRange}."
        };

        if (isAverage && reading.MatchesAtOrAboveRange is { } inRange)
        {
            sentence += $" In or above the range in {inRange} of {selectedMatches.Count} matches.";
        }

        var leaders = Leaders(marker, selectedMatches);
        if (leaders.Count > 0)
        {
            var names = leaders.Select(l => $"{l.Name} ({l.Value})").ToList();
            sentence += isAverage
                ? $" Most over the {selectedMatches.Count} matches: {IdentityFormat.JoinNames(names)}."
                : $" Led by {IdentityFormat.JoinNames(names)}.";
        }

        return sentence;
    }

    /// <summary>
    /// The top contributors on a player-level marker, at most three. Empty for team-level markers.
    ///
    /// A tie that crosses the cut is left out whole. Four players on one interception each are
    /// not "led by" whichever of them sorts first alphabetically -- naming one of them would be
    /// a ranking the numbers do not make.
    /// </summary>
    private static IReadOnlyList<(string Name, int Value)> Leaders(
        IdentityMarker marker,
        IReadOnlyList<IdentityMatch> matches)
    {
        var metric = marker.Measurement.Metric!;

        var ranked = matches
            .SelectMany(m => m.Players)
            .Select(p => (p.Name, Value: IdentityFormat.PlayerValue(p, metric)))
            .Where(p => p.Value is > 0)
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Value: g.Sum(p => p.Value!.Value)))
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        if (ranked.Count <= LeadersNamed)
        {
            return ranked;
        }

        var cutValue = ranked[LeadersNamed - 1].Value;
        var tieCrossesCut = ranked[LeadersNamed].Value == cutValue;

        return ranked
            .Take(LeadersNamed)
            .Where(p => !tieCrossesCut || p.Value != cutValue)
            .ToList();
    }

    /// <summary>
    /// How far past (or short of) the range, relative to it, so markers with different scales
    /// can be ranked. Higher is always better here.
    /// </summary>
    private static double Relative(MarkerReading reading)
    {
        var target = reading.Marker.Target;
        var value = reading.Value!.Value;

        return target.Direction == TargetDirection.LowerIsBetter
            ? target.Max!.Value / Math.Max(value, 0.0001)
            : reading.Status == IdentityStatus.Exceptional
                ? value / target.Max!.Value
                : value / target.Min!.Value;
    }
}
