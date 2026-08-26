namespace StartPraksisGruppe3Prosjekt.Authorization;

/// <summary>De fire rollene i systemet. Bruk konstantene, ikke strengliteraler.</summary>
public static class Roles
{
    public const string Player = "Player";
    public const string Coach = "Coach";
    public const string Guardian = "Guardian";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = new[] { Player, Coach, Guardian, Admin };

    /// <summary>
    /// Display name for a role. The interface is in English, matching the StartCompass
    /// site and the wireframes, so this is a pass-through for now -- it stays because it
    /// is the one place to change if the club wants Norwegian labels back.
    /// </summary>
    public static string DisplayName(string role) => role switch
    {
        Player => "Player",
        Coach => "Coach",
        Guardian => "Guardian",
        Admin => "Administrator",
        _ => role
    };
}
