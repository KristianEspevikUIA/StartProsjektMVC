namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>Configuration section "IdentityBenchmark". See appsettings.json.</summary>
public sealed class IdentityBenchmarkOptions
{
    public const string SectionName = "IdentityBenchmark";

    /// <summary>
    /// Folder with the extracted &lt;team&gt;.json files, relative to the content root or
    /// absolute. Git-ignored by default: the files name players.
    /// </summary>
    public string MatchDataPath { get; set; } = "Data/Identity/Matches";

    /// <summary>
    /// The teams offered in the selector, in order, e.g. U14, U15, U17. A team without a file
    /// is still listed, and says that nothing has been loaded for it.
    ///
    /// Empty here and filled from appsettings.json: the configuration binder appends to a
    /// collection that already has items, so a default list would come out doubled.
    /// </summary>
    public string[] Teams { get; set; } = Array.Empty<string>();
}
