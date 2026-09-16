using StartPraksisGruppe3Prosjekt.Models.Identity;

namespace StartPraksisGruppe3Prosjekt.Services.Identity;

/// <summary>
/// Read access to the Gold Standard and to the extracted match data. Both are loaded and
/// checked once, at startup; nothing on the Identity page reads a file.
/// </summary>
public interface IIdentityCatalog
{
    GoldStandard GoldStandard { get; }

    /// <summary>Every configured team, in the configured order -- with or without data.</summary>
    IReadOnlyList<IdentityTeam> Teams { get; }

    /// <summary>The team with this key ("U14"), case-insensitive, or null.</summary>
    IdentityTeam? FindTeam(string key);
}

/// <summary>A team in the selector. <see cref="Data"/> is null when no file has been loaded.</summary>
public sealed record IdentityTeam(string Key, TeamMatchData? Data)
{
    public bool HasData => Data is { Matches.Count: > 0 };

    /// <summary>"Start U14" when the file says so, otherwise the key.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Data?.TeamName) ? Key : Data.TeamName;
}
