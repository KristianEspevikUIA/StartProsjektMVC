namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>De fire rollene i systemet. Bruk konstantene, ikke strengliteraler.</summary>
public static class Roles
{
    public const string Player = "Player";
    public const string Coach = "Coach";
    public const string Guardian = "Guardian";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = new[] { Player, Coach, Guardian, Admin };

    /// <summary>Norsk visningsnavn for en rolle. Kode er engelsk, UI er norsk.</summary>
    public static string DisplayName(string role) => role switch
    {
        Player => "Spiller",
        Coach => "Trener",
        Guardian => "Foresatt",
        Admin => "Administrator",
        _ => role
    };
}
