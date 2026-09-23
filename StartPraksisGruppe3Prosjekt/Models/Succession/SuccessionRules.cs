namespace StartPraksisGruppe3Prosjekt.Models.Succession;

/// <summary>
/// The fixed limits of succession planning. Everything a coach might want to change -- the
/// lists, the thresholds, the cycle length -- is in succession-planning.json instead; what is
/// here is what the database columns and the pitch are built around.
/// </summary>
public static class SuccessionRules
{
    /// <summary>A formation that is not eleven players is a typo, and the catalog refuses it.</summary>
    public const int PlayersOnThePitch = 11;

    /// <summary>How many positions a coach names for a player: 1st, 2nd and 3rd, as in the workbook.</summary>
    public const int PositionsPerPlayer = 3;

    /// <summary>Column length for a position key ("RCB", "ACM").</summary>
    public const int PositionKeyLength = 20;

    /// <summary>Column length for every other key: a rating, a category, a level, a risk.</summary>
    public const int OptionKeyLength = 40;

    /// <summary>The three projection columns: short, e.g. "U19s starter".</summary>
    public const int ProjectionLimit = 200;

    /// <summary>What now, key development focus, super strengths.</summary>
    public const int TextLimit = 500;

    /// <summary>The notes column. The workbook's widest.</summary>
    public const int NotesLimit = 1000;

    /// <summary>How many players are named as next in line behind the starter in each slot.</summary>
    public const int NextInLine = 2;
}
