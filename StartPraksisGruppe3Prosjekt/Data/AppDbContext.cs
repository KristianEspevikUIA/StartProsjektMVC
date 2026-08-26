using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using StartPraksisGruppe3Prosjekt.Models;

namespace StartPraksisGruppe3Prosjekt.Data;

/// <summary>
/// Databasekonteksten. Arver fra IdentityDbContext slik at brukere og roller ligger
/// i samme base som domenemodellen.
/// </summary>
public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Guardianship> Guardianships => Set<Guardianship>();
    public DbSet<CoachTeam> CoachTeams => Set<CoachTeam>();
    public DbSet<SurveyRound> SurveyRounds => Set<SurveyRound>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Response> Responses => Set<Response>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();

    /// <summary>Append-only. Hvem som har sett hvilken spiller. Se PlayerAccessEvent.</summary>
    public DbSet<PlayerAccessEvent> PlayerAccessEvents => Set<PlayerAccessEvent>();

    /// <summary>Append-only. Trenerens frigivelse av egne svar til spilleren.</summary>
    public DbSet<FeedbackRelease> FeedbackReleases => Set<FeedbackRelease>();

    /// <summary>Innsendte 5C-skjemaer. Se EfSurveySubmissionStore.</summary>
    public DbSet<FiveCSubmission> FiveCSubmissions => Set<FiveCSubmission>();

    public DbSet<FiveCAnswer> FiveCAnswers => Set<FiveCAnswer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Team>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
        });

        builder.Entity<Player>(e =>
        {
            e.HasIndex(p => p.Code).IsUnique();
            e.HasIndex(p => p.UserId);
            e.HasOne(p => p.Team)
             .WithMany(t => t.Players)
             .HasForeignKey(p => p.TeamId)
             .OnDelete(DeleteBehavior.Restrict); // et lag med spillere skal ikke kunne slettes bort
        });

        builder.Entity<Guardianship>(e =>
        {
            // Samme foresatt skal ikke kunne knyttes til samme spiller to ganger.
            e.HasIndex(g => new { g.PlayerId, g.GuardianUserId }).IsUnique();
            e.HasOne(g => g.Player)
             .WithMany(p => p.Guardianships)
             .HasForeignKey(g => g.PlayerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CoachTeam>(e =>
        {
            e.HasIndex(ct => new { ct.CoachUserId, ct.TeamId }).IsUnique();
            e.HasOne(ct => ct.Team)
             .WithMany(t => t.CoachTeams)
             .HasForeignKey(ct => ct.TeamId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Item>(e =>
        {
            e.HasIndex(i => i.Number).IsUnique();
        });

        builder.Entity<Response>(e =>
        {
            // Én besvarelse per person, per spiller, per runde. Retting = oppdater raden.
            e.HasIndex(r => new { r.RoundId, r.PlayerId, r.RespondentUserId }).IsUnique();
            e.HasOne(r => r.Round)
             .WithMany(sr => sr.Responses)
             .HasForeignKey(r => r.RoundId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Player)
             .WithMany(p => p.Responses)
             .HasForeignKey(r => r.PlayerId)
             .OnDelete(DeleteBehavior.Cascade); // sletting av spiller fjerner svarene (GDPR)
        });

        builder.Entity<Answer>(e =>
        {
            e.HasIndex(a => new { a.ResponseId, a.ItemId }).IsUnique();
            e.HasOne(a => a.Response)
             .WithMany(r => r.Answers)
             .HasForeignKey(a => a.ResponseId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Item)
             .WithMany(i => i.Answers)
             .HasForeignKey(a => a.ItemId)
             .OnDelete(DeleteBehavior.Restrict);
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
