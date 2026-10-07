using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Databasekonteksten. Arver fra IdentityDbContext slik at brukere og roller ligger
/// i samme base som domenemodellen.
///
/// Tabeller og kolonner heter det samme som i koden, med små bokstaver og understrek
/// (players.birth_date, asp_net_users.user_name). Det settes ett sted, med
/// UseSnakeCaseNamingConvention i Program.cs, og gjelder Identity-tabellene også.
///
/// BRUKER-ID-ER: TO FREMMEDNØKLER, OG ELLERS INGEN -- MED VILJE.
/// players.user_id og guardianships.guardian_user_id peker på brukertabellen med fremmednøkkel:
/// de sier hvem som ER spilleren og hvem som ER foresatt, og skal ikke kunne peke på en konto
/// som ikke finnes. Alle andre kolonner som bærer en bruker-ID, er logg og historikk -- hvem
/// som endret et samtykke (consent_events.changed_by_user_id), så på en spiller
/// (player_access_events.viewed_by_user_id), slettet en (player_deletion_events.deleted_by_user_id),
/// frigav svar (feedback_releases.coach_user_id), sendte inn et skjema
/// (five_c_submissions.respondent_user_id), vurderte (succession_assessments.rater_user_id),
/// og «endret av» (player_succession_profiles.updated_by_user_id,
/// player_personal_details.updated_by_user_id). De har INGEN fremmednøkkel: raden skal overleve at
/// kontoen slettes. En fremmednøkkel ville enten nektet slettingen, eller tatt loggen med seg.
/// </summary>
public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Guardianship> Guardianships => Set<Guardianship>();
    public DbSet<SurveyRound> SurveyRounds => Set<SurveyRound>();
    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();

    /// <summary>Append-only. Hvem som har sett hvilken spiller. Se PlayerAccessEvent.</summary>
    public DbSet<PlayerAccessEvent> PlayerAccessEvents => Set<PlayerAccessEvent>();

    /// <summary>Append-only. Trenerens frigivelse av egne svar til spilleren.</summary>
    public DbSet<FeedbackRelease> FeedbackReleases => Set<FeedbackRelease>();

    /// <summary>
    /// Append-only. Hvem som har slettet hvilken spiller. Uten fremmednøkkel med vilje —
    /// se PlayerDeletionEvent.
    /// </summary>
    public DbSet<PlayerDeletionEvent> PlayerDeletionEvents => Set<PlayerDeletionEvent>();

    /// <summary>Innsendte 5C-skjemaer. Se EfSurveySubmissionStore.</summary>
    public DbSet<FiveCSubmission> FiveCSubmissions => Set<FiveCSubmission>();

    public DbSet<FiveCAnswer> FiveCAnswers => Set<FiveCAnswer>();

    /// <summary>
    /// Refleksjonen på slutten av perioden — fritekst og valgt C. Egen tabell fordi ingenting
    /// her regnes med i et snitt, og fordi fritekst om et barn skal ligge ett navngitt sted.
    /// Se FiveCReflectionAnswer.
    /// </summary>
    public DbSet<FiveCReflectionAnswer> FiveCReflectionAnswers => Set<FiveCReflectionAnswer>();

    /// <summary>
    /// Succession planning: one coach's view of one player in one eight-week cycle. Coaches and
    /// administrators only. See SuccessionAssessment.
    /// </summary>
    public DbSet<SuccessionAssessment> SuccessionAssessments => Set<SuccessionAssessment>();

    public DbSet<SuccessionRating> SuccessionRatings => Set<SuccessionRating>();

    /// <summary>Contract and training group, once per player. See PlayerSuccessionProfile.</summary>
    public DbSet<PlayerSuccessionProfile> PlayerSuccessionProfiles => Set<PlayerSuccessionProfile>();

    /// <summary>
    /// Fornavn og bilde til velkomsten når spilleren logger inn; fornavnet vises også på
    /// trenernes «Best eleven». Det eneste stedet et navn lagres om en spiller. Se
    /// PlayerPersonalDetails.
    /// </summary>
    public DbSet<PlayerPersonalDetails> PlayerPersonalDetails => Set<PlayerPersonalDetails>();

    /// <summary>Én rad: om databasen er til utvikling eller drift. Se DatabaseMarker og DatabaseGuard.</summary>
    public DbSet<DatabaseMarker> DatabaseMarker => Set<DatabaseMarker>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity setter tabellnavnene (AspNetUsers ...) og navnene på tre indekser
        // (UserNameIndex, EmailIndex, RoleNameIndex) selv, og et navn som er satt eksplisitt, lar
        // navnekonvensjonen stå. De får derfor navn her, så ingenting i skjemaet må skrives i
        // anførselstegn. Kolonnene, nøklene og resten av indeksene tar konvensjonen.
        builder.Entity<IdentityUser>(e =>
        {
            e.ToTable("asp_net_users");
            e.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_asp_net_users_normalized_user_name");
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_asp_net_users_normalized_email");
        });

        builder.Entity<IdentityRole>(e =>
        {
            e.ToTable("asp_net_roles");
            e.HasIndex(r => r.NormalizedName).HasDatabaseName("ix_asp_net_roles_normalized_name");
        });

        builder.Entity<IdentityUserRole<string>>().ToTable("asp_net_user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("asp_net_user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("asp_net_role_claims");

        builder.Entity<Team>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
        });

        builder.Entity<Player>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.UserId);
            e.HasOne(p => p.Team)
             .WithMany(t => t.Players)
             .HasForeignKey(p => p.TeamId)
             .OnDelete(DeleteBehavior.Restrict); // et lag med spillere skal ikke kunne slettes bort

            // Spilleren blir stående når kontoen slettes; det er bare koblingen som går.
            e.HasOne<IdentityUser>()
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Guardianship>(e =>
        {
            // Samme foresatt skal ikke kunne knyttes til samme spiller to ganger.
            e.HasIndex(g => new { g.PlayerId, g.GuardianUserId }).IsUnique();
            e.HasOne(g => g.Player)
             .WithMany(p => p.Guardianships)
             .HasForeignKey(g => g.PlayerId)
             .OnDelete(DeleteBehavior.Cascade);

            // En foresattkobling uten foresatt er ingenting: den går med kontoen.
            e.HasOne<IdentityUser>()
             .WithMany()
             .HasForeignKey(g => g.GuardianUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ConsentEvent>(e =>
        {
            // Oppslaget "nyeste hendelse for spilleren" er det som gjøres oftest.
            e.HasIndex(c => new { c.PlayerId, c.OccurredAt });
            e.HasOne(c => c.Player)
             .WithMany(p => p.ConsentEvents)
             .HasForeignKey(c => c.PlayerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlayerAccessEvent>(e =>
        {
            // De to oppslagene loggen faktisk brukes til: "hvem har sett denne spilleren"
            // og "hva har denne brukeren sett".
            e.HasIndex(a => new { a.PlayerId, a.OccurredAt });
            e.HasIndex(a => new { a.ViewedByUserId, a.OccurredAt });

            e.HasOne(a => a.Player)
             .WithMany()
             .HasForeignKey(a => a.PlayerId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller (GDPR) tar loggen med

            e.HasOne(a => a.Round)
             .WithMany()
             .HasForeignKey(a => a.RoundId)
             .OnDelete(DeleteBehavior.SetNull); // en slettet runde skal ikke slette loggen
        });

        builder.Entity<FiveCSubmission>(e =>
        {
            // Én besvarelse per person, per spiller, per runde. Retting = oppdater raden.
            // Regelen håndheves her og ikke i en tjeneste, slik at den holder uansett hvem
            // som skriver.
            e.HasIndex(s => new { s.RoundId, s.PlayerId, s.RespondentUserId }).IsUnique();

            e.HasOne(s => s.Round)
             .WithMany()
             .HasForeignKey(s => s.RoundId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Player)
             .WithMany()
             .HasForeignKey(s => s.PlayerId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller fjerner svarene (GDPR)
        });

        builder.Entity<FiveCAnswer>(e =>
        {
            e.HasIndex(a => new { a.SubmissionId, a.QuestionKey }).IsUnique();

            e.HasOne(a => a.Submission)
             .WithMany(s => s.Answers)
             .HasForeignKey(a => a.SubmissionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FiveCReflectionAnswer>(e =>
        {
            // Samme regel som for svarene: ett svar per spørsmål i én innsending. Retting
            // skriver over raden i stedet for å legge til en ny.
            e.HasIndex(a => new { a.SubmissionId, a.QuestionKey }).IsUnique();

            e.HasOne(a => a.Submission)
             .WithMany(s => s.Reflection)
             .HasForeignKey(a => a.SubmissionId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller når hit gjennom innsendingen
        });

        builder.Entity<SuccessionAssessment>(e =>
        {
            // One per coach, per player, per cycle. Rating again inside a cycle is a correction,
            // not a second opinion -- the same rule as the 5C submissions, held here so it
            // holds whoever writes. The cycle is the leading column after the player because
            // "this player, this cycle" is what every page asks.
            e.HasIndex(a => new { a.PlayerId, a.CycleStartsOn, a.RaterUserId }).IsUnique();

            // The board reads one cycle for a whole squad.
            e.HasIndex(a => a.CycleStartsOn);

            e.HasOne(a => a.Player)
             .WithMany()
             .HasForeignKey(a => a.PlayerId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller tar vurderingene med (GDPR)
        });

        builder.Entity<SuccessionRating>(e =>
        {
            e.HasIndex(r => new { r.AssessmentId, r.RatingKey }).IsUnique();

            e.HasOne(r => r.Assessment)
             .WithMany(a => a.Ratings)
             .HasForeignKey(r => r.AssessmentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlayerSuccessionProfile>(e =>
        {
            // One per player. A second row would be two contract end dates and no way to say
            // which one is true.
            e.HasIndex(p => p.PlayerId).IsUnique();

            e.HasOne(p => p.Player)
             .WithMany()
             .HasForeignKey(p => p.PlayerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PlayerPersonalDetails>(e =>
        {
            // Én per spiller: én spiller har ett fornavn og ett bilde i velkomsten.
            e.HasIndex(d => d.PlayerId).IsUnique();

            e.HasOne(d => d.Player)
             .WithMany()
             .HasForeignKey(d => d.PlayerId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller tar navn og bilde med (GDPR)
        });

        builder.Entity<PlayerDeletionEvent>(e =>
        {
            // «Hvem slettet hva, og når» leses kronologisk, og for én spiller når noen spør
            // hva som skjedde med akkurat den.
            e.HasIndex(d => d.OccurredAt);
            e.HasIndex(d => d.PlayerId);

            // INGEN HasOne(...).HasForeignKey(...) her. Raden skal overleve spilleren den
            // handler om; en fremmednøkkel ville enten blokkert slettingen eller tatt sporet
            // med i cascaden — og et spor som forsvinner sammen med handlingen er ikke et spor.
        });

        builder.Entity<FeedbackRelease>(e =>
        {
            // Gjeldende tilstand er nyeste rad for (runde, spiller).
            e.HasIndex(f => new { f.RoundId, f.PlayerId, f.OccurredAt });

            e.HasOne(f => f.Player)
             .WithMany()
             .HasForeignKey(f => f.PlayerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(f => f.Round)
             .WithMany()
             .HasForeignKey(f => f.RoundId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DatabaseMarker>(e =>
        {
            // Entall: tabellen har én rad, og den raden er markeringen.
            e.ToTable("database_marker", table =>
            {
                // Én rad, og bare de to verdiene vernet kjenner. Håndhevet av databasen, så
                // markeringen ikke kan bli tvetydig av noe som skrives utenom appen.
                table.HasCheckConstraint(
                    "ck_database_marker_single_row",
                    $"id = {Models.DatabaseMarker.SingleRowId}");

                table.HasCheckConstraint(
                    "ck_database_marker_environment",
                    $"environment in ('{Models.DatabaseMarker.Development}', '{Models.DatabaseMarker.Production}')");
            });

            e.Property(m => m.Id).ValueGeneratedNever();
        });
    }

    // Overstyrer overloaden med parameter, ikke den parameterløse: SaveChanges() kaller
    // videre hit. Overstyres bare SaveChanges(), går SaveChanges(false) utenom vakten.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnlyConsentLog();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyConsentLog();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Samtykkeloggen er append-only. Et samtykke som trekkes tilbake skal legges inn som
    /// en NY hendelse med lavere nivå — den gamle raden blir stående. Endring eller sletting
    /// av en eksisterende hendelse ville slettet dokumentasjonen på hva som var lov når.
    /// Unntaket er full sletting av spilleren (GDPR), der cascade tar raden med seg.
    /// </summary>
    private void GuardAppendOnlyConsentLog()
    {
        GuardAppendOnly<ConsentEvent>("ConsentEvent");
        GuardAppendOnly<PlayerAccessEvent>("PlayerAccessEvent");
        GuardAppendOnly<FeedbackRelease>("FeedbackRelease");
        GuardAppendOnly<PlayerDeletionEvent>("PlayerDeletionEvent");
    }

    /// <summary>
    /// Felles vakt for de append-only loggene. En logg som kan endres dokumenterer
    /// ingenting, så endring og sletting stoppes her og ikke i hver enkelt tjeneste.
    /// Unntaket er full sletting av spilleren (GDPR), der cascade tar radene med seg.
    /// </summary>
    private void GuardAppendOnly<TEntity>(string name) where TEntity : class
    {
        foreach (EntityEntry<TEntity> entry in ChangeTracker.Entries<TEntity>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{name} er en append-only logg. Legg til en ny hendelse i stedet for " +
                    "å endre eller slette en eksisterende.");
            }
        }
    }
}
