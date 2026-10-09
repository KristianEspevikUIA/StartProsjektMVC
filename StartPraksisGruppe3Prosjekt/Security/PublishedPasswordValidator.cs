using Microsoft.AspNetCore.Identity;

namespace StartPraksisGruppe3Prosjekt.Security;

/// <summary>
/// Avviser utviklingspassordet som nytt passord utenfor Development. Det står i et offentlig
/// repo -- se <see cref="AccountRules.PublishedDevPassword"/> -- og skal ikke kunne velges av
/// noen, heller ikke av den som har hørt at «det er det alle bruker».
///
/// I Development slipper det gjennom: der er det nettopp det seedingen gir demokontoene.
/// </summary>
public sealed class PublishedPasswordValidator : IPasswordValidator<IdentityUser>
{
    private readonly IHostEnvironment _environment;

    public PublishedPasswordValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user, string? password)
    {
        var refused = !_environment.IsDevelopment()
                      && string.Equals(password, AccountRules.PublishedDevPassword, StringComparison.Ordinal);

        return Task.FromResult(refused
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PublishedPassword",
                Description = "That password cannot be used. Choose another one."
            })
            : IdentityResult.Success);
    }
}
