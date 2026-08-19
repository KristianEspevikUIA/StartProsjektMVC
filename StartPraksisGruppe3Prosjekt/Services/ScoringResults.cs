namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Avvik på én påstand: hva spilleren svarte, hva treneren trodde, og differansen.
/// Alle feltene er nullbare fordi "Vet ikke" er et gyldig svar.
/// </summary>
/// <param name="ItemNumber">Påstandens nummer, 1-10.</param>
/// <param name="Construct">Rolleforståelse, Trygghet eller Mestring.</param>
/// <param name="PlayerScore">Spillerens svar, etter eventuell reversering.</param>
/// <param name="CoachScore">Trenerens gjetning, etter eventuell reversering.</param>
/// <param name="Gap">CoachScore - PlayerScore. Null hvis en av dem mangler.</param>
public sealed record ItemGap(
    int ItemNumber,
    string Construct,
    int? PlayerScore,
    int? CoachScore,
    int? Gap);

/// <summary>
/// Samlet avvik for én spiller i én runde.
/// </summary>
/// <param name="PlayerId">Spilleren.</param>
/// <param name="PlayerMean">Snitt av spillerens egne svar.</param>
/// <param name="CoachMean">Snitt av trenerens gjetninger.</param>
/// <param name="MeanAbsoluteGap">Snittet av |avvik| per påstand — hovedtallet (D).</param>
/// <param name="ComparableItems">Antall påstander der begge har svart noe annet enn "Vet ikke".</param>
/// <param name="Items">Avvik per påstand.</param>
public sealed record PlayerGap(
    int PlayerId,
    double? PlayerMean,
    double? CoachMean,
    double? MeanAbsoluteGap,
    int ComparableItems,
    IReadOnlyList<ItemGap> Items);

/// <summary>
/// Aggregert bilde for et lag. Returneres bare når nok spillere har svart —
/// se CanViewTeamAggregateRequirement.MinimumResponses.
/// </summary>
/// <param name="TeamId">Laget.</param>
/// <param name="RoundId">Runden.</param>
/// <param name="RespondentCount">Antall besvarelser bak tallene.</param>
/// <param name="MeanByConstruct">Snitt per område.</param>
/// <param name="MeanAbsoluteGapByConstruct">Snittavvik per område.</param>
public sealed record TeamAggregate(
    int TeamId,
    int RoundId,
    int RespondentCount,
    IReadOnlyDictionary<string, double> MeanByConstruct,
    IReadOnlyDictionary<string, double> MeanAbsoluteGapByConstruct);
