using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;

namespace StartPraksisGruppe3Prosjekt.ViewModels.Succession;

/// <summary>The cycle, team and level pickers the board pages share.</summary>
public sealed class SuccessionFilterViewModel
{
    public required SuccessionCycle Cycle { get; init; }

    public required SuccessionCycle CurrentCycle { get; init; }

    public IReadOnlyList<SuccessionCycle> Cycles { get; init; } = Array.Empty<SuccessionCycle>();

    public int? TeamId { get; init; }

    public IReadOnlyList<TeamOption> Teams { get; init; } = Array.Empty<TeamOption>();

    /// <summary>Only assessments made against this level. Null for all of them.</summary>
    public string? RatedAs { get; init; }

    public bool IsCurrentCycle => Cycle.StartsOn == CurrentCycle.StartsOn;

    public string? TeamName => Teams.FirstOrDefault(t => t.Id == TeamId)?.Name;

    public sealed record TeamOption(int Id, string Name);
}

/// <summary>
/// The Squad board / Best eleven links in the hero. They carry the team and an earlier cycle
/// across, so a coach who has clicked into U15 on one page is still looking at U15 on the other.
/// </summary>
/// <param name="Active">"board" or "eleven".</param>
public sealed record SuccessionNavViewModel(string Active, int? TeamId, string? CycleKey)
{
    public static SuccessionNavViewModel For(string active, SuccessionFilterViewModel filter) =>
        new(active, filter.TeamId, filter.IsCurrentCycle ? null : filter.Cycle.Key);
}

/// <summary>/Succession: the workbook, pulled together.</summary>
public sealed class SuccessionOverviewViewModel
{
    public required SuccessionFilterViewModel Filter { get; init; }

    /// <summary>"code", "readiness" or "team".</summary>
    public string Sort { get; init; } = "code";

    public IReadOnlyList<BoardPlayer> Rows { get; init; } = Array.Empty<BoardPlayer>();

    /// <summary>Players at least one coach has rated in the cycle shown.</summary>
    public int RatedThisCycle { get; init; }

    /// <summary>Players the signed-in coach has rated in the cycle shown.</summary>
    public int RatedByYou { get; init; }

    public int ReadyNow { get; init; }

    public int Developing { get; init; }

    /// <summary>Rated this cycle, and the coaches are at least the threshold apart somewhere.</summary>
    public IReadOnlyList<BoardPlayer> Disagreements { get; init; } = Array.Empty<BoardPlayer>();

    /// <summary>Contracts that end within <see cref="ContractWarningDays"/>.</summary>
    public IReadOnlyList<BoardPlayer> ContractsEnding { get; init; } = Array.Empty<BoardPlayer>();

    public const int ContractWarningDays = 183;

    public DateOnly Today { get; init; }

    public bool CanRate { get; init; }

    /// <summary>
    /// What to call every coach behind a row on the board: the signed-in coach is "You", the
    /// others the part of their address before the @. See ISuccessionPlanningService.RaterNamesAsync.
    /// </summary>
    public IReadOnlyDictionary<string, string> RaterNames { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The model for one row's list of coaches.</summary>
    public SuccessionRatersViewModel RatersOf(BoardPlayer row) => new(row, RaterNames);
}

/// <summary>The coaches behind one row of the board. See _SuccessionRaters.cshtml.</summary>
public sealed record SuccessionRatersViewModel(BoardPlayer Row, IReadOnlyDictionary<string, string> Names)
{
    public string NameOf(string raterUserId) => Names.TryGetValue(raterUserId, out var name) ? name : "Coach";
}

/// <summary>/Succession/Formation: the best eleven, and who is next in line.</summary>
public sealed class SuccessionFormationViewModel
{
    public required SuccessionFilterViewModel Filter { get; init; }

    public required FormationDefinition Formation { get; init; }

    public IReadOnlyList<FormationDefinition> Formations { get; init; } = Array.Empty<FormationDefinition>();

    /// <summary>The pitch, attack first, as the file draws it.</summary>
    public IReadOnlyList<IReadOnlyList<SlotView>> Lines { get; init; } = Array.Empty<IReadOnlyList<SlotView>>();

    /// <summary>The same slots, goalkeeper first, for the depth table.</summary>
    public IEnumerable<SlotView> DepthOrder => Lines.Reverse().SelectMany(line => line);

    /// <summary>
    /// The team rating over the pitch: the starters' readiness in the positions they are in --
    /// overall, marked down for a 2nd or 3rd position (SuccessionMath.PositionFit). Null with
    /// nobody on the pitch.
    /// </summary>
    public double? TeamRating { get; init; }

    /// <summary>The same, for each part of the team: attack, midfield, defence, goalkeeper.</summary>
    public IReadOnlyList<UnitRating> Units { get; init; } = Array.Empty<UnitRating>();

    public int ReadyCount { get; init; }

    public int FilledCount { get; init; }

    public int CandidateCount { get; init; }

    /// <summary>
    /// Every rated player there is to choose from, strongest first -- the eleven and the bench.
    /// </summary>
    public IReadOnlyList<SquadPlayer> Squad { get; init; } = Array.Empty<SquadPlayer>();

    /// <summary>The substitutes: everybody rated who is not in the eleven.</summary>
    public IEnumerable<SquadPlayer> Bench => Squad.Where(p => !p.Starts);

    /// <summary>The same pitch as data, for moving players about on it. See lineup.js.</summary>
    public required LineupEditorData Editor { get; init; }

    /// <summary>
    /// <see cref="Editor"/> as JSON, for a data block in the page. The default encoder escapes
    /// &lt; and &gt;, so nothing in it can close the block early.
    /// </summary>
    public string EditorJson => JsonSerializer.Serialize(Editor, EditorJsonOptions);

    private static readonly JsonSerializerOptions EditorJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// What a shirt says: the first name, or the full name where there is none. <see cref="Tag"/> is
/// the full name, for a first name two players on the page share.
/// </summary>
public sealed record ShirtName(string Name, string? Tag)
{
    public string Full => Tag is null ? Name : $"{Name} ({Tag})";
}

/// <summary>One part of the team and its rating. Null when nobody in it is on the pitch.</summary>
public sealed record UnitRating(TeamUnit Unit, double? Rating);

/// <summary>A rated player on the formation page, whether or not they start.</summary>
public sealed class SquadPlayer
{
    public required int PlayerId { get; init; }

    public required string Code { get; init; }

    /// <summary>The first name, where the club has entered one; otherwise the code.</summary>
    public required string Name { get; init; }

    /// <summary>The code too, when another player on the page has the same first name.</summary>
    public string? NameTag { get; init; }

    public string? TeamName { get; init; }

    public double Overall { get; init; }

    public ReadinessLevel Level { get; init; }

    /// <summary>Every position a coach named for them, best rank first.</summary>
    public IReadOnlyList<PositionPreference> Positions { get; init; } = Array.Empty<PositionPreference>();

    public bool FromEarlierCycle { get; init; }

    /// <summary>In the best eleven.</summary>
    public bool Starts { get; init; }
}

/// <summary>
/// The pitch for lineup.js: the slots, every player who could fill one, and the eleven the
/// page picked. Everything the script shows about a player is worked out here -- the name, the
/// numbers, the words -- so the script moves shirts about and does no succession maths beyond
/// SuccessionMath.PositionFit, the one subtraction that says how ready a player is in the slot
/// they were moved to.
/// </summary>
/// <param name="Lines">How many slots are in each row on the pitch, attack first.</param>
/// <param name="Pick">The best eleven: a player id per slot, null where nobody was named.</param>
/// <param name="RankPenalty">The file's positionRankPenalty: [0] for a 1st position, and on.</param>
/// <param name="OutOfPositionPenalty">The file's outOfPositionPenalty.</param>
public sealed record LineupEditorData(
    IReadOnlyList<LineupSlot> Slots,
    IReadOnlyList<int> Lines,
    IReadOnlyList<LineupPlayer> Players,
    IReadOnlyList<int?> Pick,
    IReadOnlyList<double> RankPenalty,
    double OutOfPositionPenalty,
    double ReadyAt,
    double DevelopingAt);

/// <param name="Unit">"Attack", "Midfield", "Defence" or "Goalkeeper". See SuccessionMath.UnitOf.</param>
public sealed record LineupSlot(int Index, string Position, string Name, string Unit);

/// <param name="Name">What the shirt says: the first name, or the code.</param>
/// <param name="Tag">The code as well, for a first name another player on the page shares.</param>
/// <param name="Positions">Position key to the best rank any coach gave it.</param>
public sealed record LineupPlayer(
    int Id,
    string Code,
    string Name,
    string? Tag,
    string? Team,
    double Overall,
    string Level,
    IReadOnlyDictionary<string, int> Positions,
    bool Earlier,
    string Url);

public sealed class SlotView
{
    public required string Position { get; init; }

    public required string PositionName { get; init; }

    public SlotPlayer? Starter { get; init; }

    public IReadOnlyList<SlotPlayer> NextInLine { get; init; } = Array.Empty<SlotPlayer>();

    /// <summary>Nobody next in line who is ready, or nobody at all: where to look outside.</summary>
    public bool IsThin => Starter is null || NextInLine.Count == 0;
}

public sealed class SlotPlayer
{
    public required int PlayerId { get; init; }

    public required string Code { get; init; }

    /// <summary>The first name, where the club has entered one; otherwise the code.</summary>
    public required string Name { get; init; }

    /// <summary>The code too, when another player on the page has the same first name.</summary>
    public string? NameTag { get; init; }

    public string? TeamName { get; init; }

    public double Overall { get; init; }

    /// <summary>Their readiness in this slot: <see cref="Overall"/> less the rank's penalty.</summary>
    public double Fit { get; init; }

    /// <summary>1 when this is their 1st position, 2 or 3 otherwise.</summary>
    public int Rank { get; init; }

    public ReadinessLevel Level { get; init; }

    public required ReadinessOutlook Outlook { get; init; }

    /// <summary>Their consensus is from an earlier cycle than the one shown.</summary>
    public bool FromEarlierCycle { get; init; }

    /// <summary>Where they start instead, for someone next in line who starts elsewhere.</summary>
    public string? StartsAt { get; init; }

    public string? RatedAs { get; init; }
}

/// <summary>/Succession/Player/{id}: each coach side by side.</summary>
public sealed class SuccessionPlayerViewModel
{
    public required SuccessionPlayerDetail Detail { get; init; }

    public required SuccessionFilterViewModel Filter { get; init; }

    /// <summary>One column per coach who rated in the cycle, the signed-in coach first.</summary>
    public IReadOnlyList<RaterColumn> Raters { get; init; } = Array.Empty<RaterColumn>();

    /// <summary>A coach, in the current cycle.</summary>
    public bool CanRate { get; init; }

    /// <summary>The signed-in coach has rated this player in the cycle shown.</summary>
    public bool HasOwn { get; init; }

    public required SuccessionProfileForm Profile { get; init; }

    public sealed record RaterColumn(string Name, bool IsYou, SuccessionAssessment Assessment, double? Overall);
}

/// <summary>The contract and training group form on the player page.</summary>
public sealed class SuccessionProfileForm
{
    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Contract type")]
    public string? ContractType { get; set; }

    [Display(Name = "Contract ends")]
    [DataType(DataType.Date)]
    public DateOnly? ContractEndsOn { get; set; }

    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Training group")]
    public string? TrainingGroup { get; set; }
}

/// <summary>
/// The rating form: one row of the workbook, for one coach, one player and the current cycle.
///
/// Bound back on POST. Which player, which coach and which cycle are NOT fields -- they come
/// from the route, the sign-in and the clock, so a form cannot put one coach's view under
/// another's name or back-date it into a closed cycle.
/// </summary>
public sealed class SuccessionRateViewModel
{
    // --- shown, not posted ------------------------------------------------------------

    public int PlayerId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string? TeamName { get; set; }

    public string CycleLabel { get; set; } = string.Empty;

    /// <summary>The cycle the form was filled in from, when it is the coach's earlier one.</summary>
    public string? PrefilledFrom { get; set; }

    /// <summary>A correction of this cycle's assessment rather than a first one.</summary>
    public bool IsCorrection { get; set; }

    // --- posted -----------------------------------------------------------------------

    /// <summary>Keyed by the rating key: name="Ratings[physical]".</summary>
    public Dictionary<string, int?> Ratings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Range(1, 10)]
    [Display(Name = "Your own call: ready for the next step")]
    public int? PersonalReadiness { get; set; }

    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Rated as")]
    public string? RatedAs { get; set; }

    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Ability category")]
    public string? AbilityCategory { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "1st position")]
    public string? FirstPosition { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "2nd position")]
    public string? SecondPosition { get; set; }

    [StringLength(SuccessionRules.PositionKeyLength)]
    [Display(Name = "3rd position")]
    public string? ThirdPosition { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "0–6 months")]
    public string? Projection0To6Months { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "6–18 months")]
    public string? Projection6To18Months { get; set; }

    [StringLength(SuccessionRules.ProjectionLimit)]
    [Display(Name = "18–36 months")]
    public string? Projection18To36Months { get; set; }

    [Display(Name = "Pathway blocked?")]
    public bool? PathwayBlocked { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "What now?")]
    public string? WhatNow { get; set; }

    [StringLength(SuccessionRules.OptionKeyLength)]
    [Display(Name = "Succession risk")]
    public string? SuccessionRisk { get; set; }

    [Display(Name = "External needed?")]
    public bool? ExternalNeeded { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "Key development focus")]
    public string? KeyDevelopmentFocus { get; set; }

    [StringLength(SuccessionRules.TextLimit)]
    [Display(Name = "Super strengths")]
    public string? SuperStrengths { get; set; }

    [StringLength(SuccessionRules.NotesLimit)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    /// <summary>The form, filled in from an assessment already saved.</summary>
    public void CopyFrom(SuccessionAssessment assessment)
    {
        Ratings = assessment.Ratings.ToDictionary(r => r.RatingKey, r => (int?)r.Value, StringComparer.OrdinalIgnoreCase);
        PersonalReadiness = assessment.PersonalReadiness;
        RatedAs = assessment.RatedAs;
        AbilityCategory = assessment.AbilityCategory;
        FirstPosition = assessment.FirstPosition;
        SecondPosition = assessment.SecondPosition;
        ThirdPosition = assessment.ThirdPosition;
        Projection0To6Months = assessment.Projection0To6Months;
        Projection6To18Months = assessment.Projection6To18Months;
        Projection18To36Months = assessment.Projection18To36Months;
        PathwayBlocked = assessment.PathwayBlocked;
        WhatNow = assessment.WhatNow;
        SuccessionRisk = assessment.SuccessionRisk;
        ExternalNeeded = assessment.ExternalNeeded;
        KeyDevelopmentFocus = assessment.KeyDevelopmentFocus;
        SuperStrengths = assessment.SuperStrengths;
        Notes = assessment.Notes;
    }

    /// <summary>What the form says, as an unsaved assessment. Blank text is stored as null, not "".</summary>
    public SuccessionAssessment ToAssessment(SuccessionSettings settings) => new()
    {
        Ratings = settings.Ratings
            .Where(r => Ratings.TryGetValue(r.Key, out var value) && value.HasValue)
            .Select(r => new SuccessionRating { RatingKey = r.Key, Value = Ratings[r.Key]!.Value })
            .ToList(),
        PersonalReadiness = PersonalReadiness,
        RatedAs = Blank(RatedAs),
        AbilityCategory = Blank(AbilityCategory),
        FirstPosition = Blank(FirstPosition),
        SecondPosition = Blank(SecondPosition),
        ThirdPosition = Blank(ThirdPosition),
        Projection0To6Months = Blank(Projection0To6Months),
        Projection6To18Months = Blank(Projection6To18Months),
        Projection18To36Months = Blank(Projection18To36Months),
        PathwayBlocked = PathwayBlocked,
        WhatNow = Blank(WhatNow),
        SuccessionRisk = Blank(SuccessionRisk),
        ExternalNeeded = ExternalNeeded,
        KeyDevelopmentFocus = Blank(KeyDevelopmentFocus),
        SuperStrengths = Blank(SuperStrengths),
        Notes = Blank(Notes)
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
