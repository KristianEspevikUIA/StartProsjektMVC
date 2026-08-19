using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.ViewModels;

/// <summary>
/// Lagoversikten treneren møter. TODO (Taavi): fyll ut i CoachController.
///
/// Merk: en rad skal bare inneholde avvikstall for spillere der CanViewPlayer sa ja.
/// For de andre vises raden uten tall, ikke skjules helt — treneren skal se at
/// spilleren finnes, men ikke hva hen har svart.
/// </summary>
public class TeamOverviewViewModel
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;

    public int RoundId { get; set; }
    public string RoundName { get; set; } = string.Empty;

    public List<PlayerRow> Players { get; set; } = new();

    /// <summary>Aggregert lagsnitt, eller null når for få har svart. Se CanViewTeamAggregate.</summary>
    public Services.TeamAggregate? Aggregate { get; set; }

    public class PlayerRow
    {
        public int PlayerId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Position { get; set; }

        /// <summary>Om innlogget trener faktisk får se tallene for denne spilleren.</summary>
        public bool CanView { get; set; }

        public ConsentLevel Consent { get; set; }
        public bool PlayerHasAnswered { get; set; }
        public bool CoachHasAnswered { get; set; }

        /// <summary>Snittavvik. Null når det ikke er noe å vise, eller ikke lov å vise det.</summary>
        public double? MeanAbsoluteGap { get; set; }
    }
}
