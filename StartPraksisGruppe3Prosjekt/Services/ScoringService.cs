using StartPraksisGruppe3Prosjekt.Data;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Services;

/// <summary>
/// Eier: Victor.
///
/// <see cref="ScoreOf"/> er ferdig — den er selve definisjonen av reverseringsregelen
/// og bør brukes overalt der et råsvar gjøres om til en skår. Resten er TODO.
/// </summary>
public class ScoringService : IScoringService
{
    private readonly AppDbContext _db;

    public ScoringService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public int ScoreOf(Item item, int rawValue)
    {
        if (rawValue < PlayerRules.ScaleMin || rawValue > PlayerRules.ScaleMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawValue),
                rawValue,
                $"Svar må ligge mellom {PlayerRules.ScaleMin} og {PlayerRules.ScaleMax}.");
        }

        // Påstand 5 er negativt formulert: (6 - verdi).
        return item.IsReversed ? PlayerRules.ReverseScoreBase - rawValue : rawValue;
    }

    /// <inheritdoc />
    public Task<PlayerGap?> GetPlayerGapAsync(
        int roundId,
        int playerId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Victor): hent spillerens egen Response (RespondentType.Player) og trenerens
        // Response (RespondentType.Coach) for runden, par svarene på ItemId, kjør begge
        // gjennom ScoreOf, og regn ut gap per påstand + snitt av |gap|.
        // Påstander der én av partene svarte "Vet ikke" (Value == null) holdes utenfor
        // snittet, men skal fortsatt vises i lista med Gap = null.
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlayerGap>> GetTeamGapsAsync(
        int roundId,
        int teamId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Victor): samme utregning for alle spillere på laget, i så få spørringer
        // som mulig. Ikke filtrer på samtykke her — det gjør CanViewPlayer-policyen.
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<TeamAggregate?> GetTeamAggregateAsync(
        int roundId,
        int teamId,
        CancellationToken cancellationToken = default)
    {
        // TODO (Victor): snitt per Construct for laget.
        // Returner null hvis antall besvarelser er under
        // CanViewTeamAggregateRequirement.MinimumResponses — det er den samme grensen
        // policyen håndhever, og begge skal holde.
        throw new NotImplementedException();
    }
}
