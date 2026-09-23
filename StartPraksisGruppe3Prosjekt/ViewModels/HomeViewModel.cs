using StartPraksisGruppe3Prosjekt.Services;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>The front page. Only a signed-in player the club has entered a name or photo for gets a welcome.</summary>
public class HomeViewModel
{
    public PlayerWelcome? Welcome { get; set; }

    /// <summary>"Welcome, Alex", or just "Welcome" when there is a photo and no name.</summary>
    public string? WelcomeHeading => Welcome is null
        ? null
        : Welcome.FirstName is { } name ? $"Welcome, {name}" : "Welcome";
}
