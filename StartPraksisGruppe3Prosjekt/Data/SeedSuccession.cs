using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StartPraksisGruppe3Prosjekt.Authorization;
using StartPraksisGruppe3Prosjekt.Models;
using StartPraksisGruppe3Prosjekt.Models.Succession;
using StartPraksisGruppe3Prosjekt.Services.Succession;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Oppdiktede succession-vurderinger, for Development.
///
/// ALT ER OPPDIKTET, som resten av seedingen. Arbeidsboka trenerne leverte har ekte navn på
/// ekte spillere, de fleste mindreårige, og ingenting fra den er her -- bare formen på den.
/// Spillerne er de oppdiktede fra SeedData, tekstene er skrevet for denne fila og nevner ingen.
///
/// Hva dataene er laget for å vise:
///   * Sammenligning. Tre trenere, og de er ikke enige: én er rausere, én strengere, og noen
///     spillere har to trenere tre poeng fra hverandre på én vurdering.
///   * Utvikling. Tre sykluser, de fleste går opp, noen står stille og et par går ned, slik at
///     «uker til klar» har både et tall, «not closing» og «ready now» å vise.
///   * En syklus som pågår. Den nåværende er bare delvis vurdert, så tavla viser både spillere
///     fra denne syklusen og spillere som faller tilbake på forrige.
///   * Hull på banen. Posisjonene følger de seedede posisjonene, så det finnes spillere til
///     alle elleve i 3-5-2 og 4-3-3 -- men ikke dobbelt opp overalt.
///
/// Idempotent per (spiller, trener, syklus), og tilfeldigheten er sådd fra SeedData.SeedKey og
/// syklusdato: samme base gir det samme bildet i morgen.
/// </summary>
internal static class SeedSuccession
{
    /// <summary>
    /// De to ekstra trenerkontoene. En sammenligning av trenere trenger mer enn én trener -- én
    /// konto kan ikke være uenig med seg selv. De har trenerrollen og vanlig demopassord, slik at
    /// man kan logge inn som en av dem og se sin egen kolonne komme opp.
    /// </summary>
    internal static readonly string[] ExtraRaterEmails =
    {
        "trener.akademi@ikstart.example",
        "trener.utvikling@ikstart.example"
    };

    /// <summary>Hvor mange sykluser bakover fra den nåværende som seedes, den nåværende medregnet.</summary>
    private const int Cycles = 3;

    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        ISuccessionCatalog catalog,
        string password,
        ILogger logger)
    {
        var mainCoach = await userManager.FindByEmailAsync(SeedData.CoachEmail);
        if (mainCoach is null)
        {
            return;
        }

        var raters = new List<string> { mainCoach.Id };
        foreach (var email in ExtraRaterEmails)
        {
            raters.Add(await SeedData.EnsureUserAsync(userManager, email, password, Roles.Coach));
        }

        var players = await db.Players
            .AsNoTracking()
            .Include(p => p.Team)
            .OrderBy(p => p.Id)
            .ToListAsync();

        await SeedProfilesAsync(db, players, mainCoach.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = catalog.CycleOf(today);

        var cycles = new List<SuccessionCycle> { current };
        for (var i = 1; i < Cycles; i++)
        {
            cycles.Insert(0, cycles[0].Previous(catalog.Settings.Cycle));
        }

        var existing = (await db.SuccessionAssessments
                .AsNoTracking()
                .Select(a => new { a.PlayerId, a.RaterUserId, a.CycleStartsOn })
                .ToListAsync())
            .Select(a => (a.PlayerId, a.RaterUserId, a.CycleStartsOn))
            .ToHashSet();

        // Which centre-back is the right one and which the left: the seeded squads have two
        // "Centre-back"s each, in that order.
        var centreBackOrder = players
            .Where(p => p.Position == "Centre-back")
            .GroupBy(p => p.TeamId)
            .SelectMany(g => g.Select((p, index) => (p.Id, index)))
            .ToDictionary(x => x.Id, x => x.index);

        var added = 0;

        foreach (var player in players)
        {
            var profile = ProfileFor(player, centreBackOrder.GetValueOrDefault(player.Id));

            for (var c = 0; c < cycles.Count; c++)
            {
                var cycle = cycles[c];
                var isCurrent = c == cycles.Count - 1;

                for (var r = 0; r < raters.Count; r++)
                {
                    var rater = raters[r];

                    if (existing.Contains((player.Id, rater, cycle.StartsOn)))
                    {
                        continue;
                    }

                    var random = new Random(SeedData.StableSeed($"{SeedData.SeedKey(player)}|{cycle.Key}|{r}"));

                    // The main coach sees almost everyone; the other two not quite. The current
                    // cycle is under way, so about half the ratings are in.
                    var chance = isCurrent ? 0.45 : r == 0 ? 0.95 : 0.8;
                    if (random.NextDouble() > chance)
                    {
                        continue;
                    }

                    db.SuccessionAssessments.Add(
                        AssessmentFor(player, profile, cycle, r, rater, random, catalog, today));
                    existing.Add((player.Id, rater, cycle.StartsOn));
                    added++;
                }
            }
        }

        if (added == 0)
        {
            return;
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "La til {Count} oppdiktede succession-vurderinger fra {Raters} trenere over {Cycles} sykluser.",
            added,
            raters.Count,
            cycles.Count);
    }

    /// <summary>Kontrakt og treningsgruppe, for spillere som ikke har det fra før.</summary>
    private static async Task SeedProfilesAsync(AppDbContext db, IReadOnlyList<Player> players, string updatedBy)
    {
        var have = (await db.PlayerSuccessionProfiles.Select(p => p.PlayerId).ToListAsync()).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var player in players.Where(p => !have.Contains(p.Id)))
        {
            var random = new Random(SeedData.StableSeed($"{SeedData.SeedKey(player)}|profile"));
            var team = player.Team?.Name;

            // U14, U15 and U17: a youth contract is for the oldest, and not all of them.
            var contract = team switch
            {
                "U17" => random.NextDouble() < 0.6 ? "youth" : "non",
                "U15" => random.NextDouble() < 0.15 ? "youth" : "non",
                _ => "non"
            };

            // The MESO group is usually the player's own age group, sometimes the one above.
            var group = team switch
            {
                "U17" => random.NextDouble() < 0.2 ? "u19" : "u17",
                "U15" => random.NextDouble() < 0.2 ? "u17" : "u15",
                _ => random.NextDouble() < 0.15 ? "u15" : "u14"
            };

            db.PlayerSuccessionProfiles.Add(new PlayerSuccessionProfile
            {
                PlayerId = player.Id,
                ContractType = contract,
                // A few inside six months, so the board has contracts to warn about.
                ContractEndsOn = contract == "non" ? null : today.AddDays(40 + random.Next(0, 900)),
                TrainingGroup = group,
                UpdatedByUserId = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-random.Next(10, 120))
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Én oppdiktet spillers utgangspunkt: nivå, retning og posisjoner. Sådd fra spilleren alene, så
    /// alle tre trenerne vurderer den samme spilleren -- det er trenerne som skiller seg, ikke
    /// spilleren.
    /// </summary>
    private sealed record PlayerProfile(
        double Level,
        double GrowthPerCycle,
        IReadOnlyDictionary<string, double> Offsets,
        string[] Positions,
        string RatedAs);

    private static PlayerProfile ProfileFor(Player player, int centreBackIndex)
    {
        var random = new Random(SeedData.StableSeed($"{SeedData.SeedKey(player)}|succession"));
        var team = player.Team?.Name;

        var level = team switch
        {
            "U17" => 6.0 + random.NextDouble() * 2.4,
            "U15" => 4.8 + random.NextDouble() * 2.6,
            _ => 3.8 + random.NextDouble() * 2.8
        };

        // Fire av fem går opp. Den femte står stille eller går ned.
        var growth = random.NextDouble() < 0.2
            ? -0.1 - random.NextDouble() * 0.25
            : 0.1 + random.NextDouble() * 0.5;

        var offsets = new[] { "physical", "technical", "tactical", "mental", "professionalism", "availability" }
            .ToDictionary(key => key, _ => (random.NextDouble() - 0.5) * 2.4);

        var positions = player.Position switch
        {
            "Goalkeeper" => new[] { "GK" },
            "Right-back" => new[] { "RB", "RWB", "RCB" },
            "Centre-back" => centreBackIndex == 0 ? new[] { "RCB", "CB", "RB" } : new[] { "LCB", "CB", "LB" },
            "Left-back" => new[] { "LB", "LWB", "LCB" },
            "Defensive midfielder" => new[] { "C6", "R6", "L6" },
            "Central midfielder" => new[] { "R8", "L8", "C6" },
            "Attacking midfielder" => new[] { "ACM", "L8", "CF" },
            "Right winger" => new[] { "RW", "RST", "R8" },
            "Striker" => new[] { "CF", "LST", "RST" },
            "Left winger" => new[] { "LW", "LST", "L8" },
            _ => new[] { "C6" }
        };

        // Judged against their own age group, or the one above for the ones who are close.
        var ratedAs = team switch
        {
            "U17" => level > 7.2 ? "u19" : "u17",
            "U15" => level > 6.4 ? "u17" : "u15",
            _ => level > 5.8 ? "u15" : "u14"
        };

        return new PlayerProfile(level, growth, offsets, positions, ratedAs);
    }

    private static SuccessionAssessment AssessmentFor(
        Player player,
        PlayerProfile profile,
        SuccessionCycle cycle,
        int raterIndex,
        string raterUserId,
        Random random,
        ISuccessionCatalog catalog,
        DateOnly today)
    {
        // The main coach is the yardstick, the academy coach is more generous, the development
        // coach stricter. Plus what every coach brings to every player on the day.
        var bias = raterIndex switch { 1 => 0.45, 2 => -0.35, _ => 0.0 };
        var step = cycle.Number;

        // Every so often one coach sees one rating very differently -- the disagreement the
        // comparison is for.
        var outlierKey = random.NextDouble() < 0.12
            ? profile.Offsets.Keys.ElementAt(random.Next(profile.Offsets.Count))
            : null;

        var ratings = catalog.Settings.Ratings
            .Select(rating =>
            {
                var offset = profile.Offsets.GetValueOrDefault(rating.Key);
                var noise = (random.NextDouble() - 0.5) * 1.2;
                var outlier = rating.Key == outlierKey ? (random.NextDouble() < 0.5 ? -3.2 : 3.2) : 0;
                var value = profile.Level + offset + profile.GrowthPerCycle * (step - 4) + bias + noise + outlier;

                return new SuccessionRating
                {
                    RatingKey = rating.Key,
                    Value = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 1, 10)
                };
            })
            .ToList();

        var overall = ratings.Average(r => r.Value);

        // Positions: mostly the player's own order. One coach in five swaps the 2nd and 3rd;
        // one in ten has the 2nd as first choice.
        var positions = profile.Positions.ToArray();
        var roll = random.NextDouble();
        if (positions.Length == 3 && roll < 0.2)
        {
            (positions[1], positions[2]) = (positions[2], positions[1]);
        }
        else if (positions.Length == 3 && roll < 0.3)
        {
            (positions[0], positions[1]) = (positions[1], positions[0]);
        }

        var age = player.AgeAt(today);
        var category = overall switch
        {
            >= 7 when age <= 20 => "performance-potential",
            >= 7 => "performance",
            >= 5.2 when age <= 19 => "potential",
            >= 6 => "performance",
            _ => "squad"
        };

        // Coaches do not always agree on the box either.
        if (random.NextDouble() < 0.12)
        {
            category = category == "potential" ? "squad" : "potential";
        }

        var team = player.Team?.Name;
        var updated = cycle.StartsOn.AddDays(random.Next(3, 50));
        if (updated > today)
        {
            updated = today;
        }

        return new SuccessionAssessment
        {
            PlayerId = player.Id,
            RaterUserId = raterUserId,
            CycleStartsOn = cycle.StartsOn,
            CatalogVersion = catalog.Settings.Version,
            UpdatedAt = new DateTimeOffset(updated.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero),
            RatedAs = profile.RatedAs,
            AbilityCategory = category,
            FirstPosition = positions.ElementAtOrDefault(0),
            SecondPosition = positions.ElementAtOrDefault(1),
            ThirdPosition = positions.ElementAtOrDefault(2),
            PersonalReadiness = random.NextDouble() < 0.85
                ? Math.Clamp((int)Math.Round(overall + (random.NextDouble() - 0.4) * 2.4), 1, 10)
                : null,
            Projection0To6Months = Pick(random, 0.7, team switch
            {
                "U17" => new[] { "U17s starter", "Train with the U19s", "U17s rotation" },
                "U15" => new[] { "U15s starter", "Train with the U17s", "U15s rotation" },
                _ => new[] { "U14s starter", "Train with the U15s", "U14s rotation" }
            }),
            Projection6To18Months = Pick(random, 0.6, team switch
            {
                "U17" => new[] { "U19s squad", "Train with the 1st team", "U17s captain" },
                "U15" => new[] { "U17s squad", "U17s starter", "U15s captain" },
                _ => new[] { "U15s starter", "U15s squad", "U14s captain" }
            }),
            Projection18To36Months = Pick(random, 0.45, team switch
            {
                "U17" => new[] { "U19s starter", "1st team squad", "Youth contract" },
                "U15" => new[] { "U17s starter", "U19s squad" },
                _ => new[] { "U17s squad", "U15s starter" }
            }),
            PathwayBlocked = Maybe(random, 0.25, 0.8),
            WhatNow = Pick(random, 0.5, new[]
            {
                "Two sessions a week with the 1st team, matches with their own team.",
                "Loan in the next window if minutes do not come.",
                "Gym block through the winter, review after pre-season.",
                "Minutes in the cup games; keep in the matchday squad.",
                "Stay in the current group and play every week."
            }),
            SuccessionRisk = random.NextDouble() switch { < 0.5 => "green", < 0.85 => "amber", _ => "red" },
            ExternalNeeded = Maybe(random, 0.15, 0.75),
            KeyDevelopmentFocus = Pick(random, 0.75, new[]
            {
                "First touch under pressure.",
                "Scanning before receiving.",
                "Defending the back post.",
                "Acceleration over the first ten metres.",
                "Weak foot, both passing and finishing.",
                "Game management in the last fifteen minutes."
            }),
            SuperStrengths = Pick(random, 0.65, new[]
            {
                "Ball carrying.",
                "Aerial duels.",
                "Work rate without the ball.",
                "Passing range.",
                "One-v-one defending.",
                "Finishing inside the box."
            }),
            Notes = Pick(random, 0.2, new[]
            {
                "Went through this with the player in the last one-to-one.",
                "Worth a second look after the next block of games."
            }),
            Ratings = ratings
        };
    }

    /// <summary>One of the texts, or nothing -- a coach does not fill in every column.</summary>
    private static string? Pick(Random random, double chance, string[] options) =>
        random.NextDouble() < chance ? options[random.Next(options.Length)] : null;

    /// <summary>Yes with <paramref name="yes"/>, no up to <paramref name="answered"/>, otherwise unanswered.</summary>
    private static bool? Maybe(Random random, double yes, double answered) =>
        random.NextDouble() switch
        {
            var r when r < yes => true,
            var r when r < answered => false,
            _ => null
        };
}
