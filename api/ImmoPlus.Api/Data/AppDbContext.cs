namespace ImmoPlus.Api.Data;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Utilisateur> Utilisateurs { get; set; }
    public DbSet<Appartement> Appartements { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<FinancementLocation> FinancementLocations { get; set; }
    public DbSet<Mensualite> Mensualites { get; set; }
    public DbSet<Paiement> Paiements { get; set; }
    public DbSet<Candidature> Candidatures { get; set; }
    public DbSet<Garant> Garants { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<Reservation> Reservations { get; set; }
    public DbSet<PhotoAppartement> PhotosAppartement { get; set; }
    public DbSet<DemandeComplement> DemandesComplement { get; set; }
    public DbSet<Visite> Visites { get; set; }
    public DbSet<Loyer> Loyers { get; set; }
    public DbSet<Indisponibilite> Indisponibilites { get; set; }
    public DbSet<Avis> Avis { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<TransactionMobile> TransactionsMobiles { get; set; }
    public DbSet<PhotoEtatDesLieux> PhotosEtatDesLieux { get; set; }
    public DbSet<Favori> Favoris { get; set; }
    public DbSet<NotificationSms> NotificationsSms { get; set; }
    public DbSet<JournalAction> JournalActions { get; set; }
    public DbSet<Signalement> Signalements { get; set; }
    public DbSet<PieceIdentite> PiecesIdentite { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Relations ---

        modelBuilder.Entity<Visite>()
            .HasOne(v => v.Candidature)
            .WithMany(c => c.Visites)
            .HasForeignKey(v => v.CandidatureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DemandeComplement>()
            .HasOne(d => d.Candidature)
            .WithMany(c => c.DemandesComplement)
            .HasForeignKey(d => d.CandidatureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PhotoAppartement>()
            .HasOne(p => p.Appartement)
            .WithMany(a => a.Photos)
            .HasForeignKey(p => p.AppartementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Indisponibilite>()
            .HasOne(i => i.Appartement)
            .WithMany(a => a.Indisponibilites)
            .HasForeignKey(i => i.AppartementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Appartement>()
            .HasOne(a => a.Bailleur)
            .WithMany(u => u.Appartements)
            .HasForeignKey(a => a.BailleurId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Location>()
            .HasOne(l => l.Locataire)
            .WithMany(u => u.Locations)
            .HasForeignKey(l => l.LocataireId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Location>()
            .HasOne(l => l.Appartement)
            .WithMany(a => a.Locations)
            .HasForeignKey(l => l.AppartementId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Location>()
            .HasOne(l => l.Candidature)
            .WithMany()
            .HasForeignKey(l => l.CandidatureId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Location>()
            .HasOne(l => l.FinancementLocation)
            .WithOne(f => f.Location)
            .HasForeignKey<FinancementLocation>(f => f.LocationId);

        modelBuilder.Entity<Mensualite>()
            .HasOne(m => m.FinancementLocation)
            .WithMany(f => f.Mensualites)
            .HasForeignKey(m => m.FinancementLocationId);

        modelBuilder.Entity<Paiement>()
            .HasOne(p => p.Mensualite)
            .WithMany(m => m.Paiements)
            .HasForeignKey(p => p.MensualiteId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Paiement>()
            .HasOne(p => p.Loyer)
            .WithMany(l => l.Paiements)
            .HasForeignKey(p => p.LoyerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Loyer>()
            .HasOne(l => l.Location)
            .WithMany(l => l.Loyers)
            .HasForeignKey(l => l.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
        // Une échéance par numéro : deux lectures simultanées des loyers (ou les rappels) ne peuvent pas les créer en double.
        modelBuilder.Entity<Loyer>().HasIndex(l => new { l.LocationId, l.Numero }).IsUnique();
        modelBuilder.Entity<Mensualite>().HasIndex(m => new { m.FinancementLocationId, m.NumeroEcheance }).IsUnique();
        modelBuilder.Entity<Loyer>().Property(l => l.MontantDu).HasPrecision(18, 2);
        modelBuilder.Entity<Loyer>().Property(l => l.MontantPaye).HasPrecision(18, 2);

        modelBuilder.Entity<Paiement>()
            .HasOne(p => p.Reservation)
            .WithMany(r => r.Paiements)
            .HasForeignKey(p => p.ReservationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Candidature>()
            .HasOne(c => c.Locataire)
            .WithMany(u => u.Candidatures)
            .HasForeignKey(c => c.LocataireId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Candidature>()
            .HasOne(c => c.Appartement)
            .WithMany(a => a.Candidatures)
            .HasForeignKey(c => c.AppartementId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Candidature>()
            .HasOne(c => c.Garant)
            .WithOne(g => g.Candidature)
            .HasForeignKey<Garant>(g => g.CandidatureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Candidature>()
            .HasMany(c => c.Documents)
            .WithOne(d => d.Candidature)
            .HasForeignKey(d => d.CandidatureId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.Locataire)
            .WithMany(u => u.Reservations)
            .HasForeignKey(r => r.LocataireId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.Appartement)
            .WithMany(a => a.Reservations)
            .HasForeignKey(r => r.AppartementId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Avis>()
            .HasOne(a => a.Reservation)
            .WithMany(r => r.Avis)
            .HasForeignKey(a => a.ReservationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Avis>()
            .HasOne(a => a.Location)
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Avis>()
            .HasOne(a => a.Auteur)
            .WithMany()
            .HasForeignKey(a => a.AuteurId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Avis>()
            .HasOne(a => a.Cible)
            .WithMany()
            .HasForeignKey(a => a.CibleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Avis>()
            .HasOne(a => a.Appartement)
            .WithMany()
            .HasForeignKey(a => a.AppartementId)
            .OnDelete(DeleteBehavior.Restrict);
        // Un seul avis par sens et par séjour.
        modelBuilder.Entity<Avis>().HasIndex(a => new { a.ReservationId, a.Sens }).IsUnique();
        // Un seul avis par sens et par location longue durée (pendant la location ou après sa fin, jamais les deux).
        modelBuilder.Entity<Avis>().HasIndex(a => new { a.LocationId, a.Sens }).IsUnique();

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Reservation)
            .WithMany(r => r.Messages)
            .HasForeignKey(m => m.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Message>()
            .HasOne(m => m.Auteur)
            .WithMany()
            .HasForeignKey(m => m.AuteurId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TransactionMobile>()
            .HasOne(t => t.Reservation)
            .WithMany(r => r.TransactionsMobiles)
            .HasForeignKey(t => t.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TransactionMobile>()
            .HasOne(t => t.Utilisateur)
            .WithMany()
            .HasForeignKey(t => t.UtilisateurId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TransactionMobile>()
            .HasOne(t => t.Paiement)
            .WithMany()
            .HasForeignKey(t => t.PaiementId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        // Le webhook retrouve la transaction par la référence du fournisseur.
        modelBuilder.Entity<TransactionMobile>().HasIndex(t => t.ReferenceFournisseur).IsUnique()
            .HasFilter("\"ReferenceFournisseur\" IS NOT NULL");
        // Une seule transaction en attente par réservation, même en cas de double clic.
        modelBuilder.Entity<TransactionMobile>().HasIndex(t => t.ReservationId).IsUnique()
            .HasFilter("\"Statut\" = 0");
        modelBuilder.Entity<TransactionMobile>().Property(t => t.Montant).HasPrecision(18, 2);

        modelBuilder.Entity<PhotoEtatDesLieux>()
            .HasOne(p => p.Reservation)
            .WithMany(r => r.PhotosEtatDesLieux)
            .HasForeignKey(p => p.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PhotoEtatDesLieux>()
            .HasOne(p => p.Auteur)
            .WithMany()
            .HasForeignKey(p => p.AuteurId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Favori>().HasKey(f => new { f.UtilisateurId, f.AppartementId });
        modelBuilder.Entity<Favori>()
            .HasOne(f => f.Utilisateur)
            .WithMany()
            .HasForeignKey(f => f.UtilisateurId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Favori>()
            .HasOne(f => f.Appartement)
            .WithMany(a => a.Favoris)
            .HasForeignKey(f => f.AppartementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotificationSms>()
            .HasOne(n => n.Utilisateur)
            .WithMany()
            .HasForeignKey(n => n.UtilisateurId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<NotificationSms>().Property(n => n.Telephone).HasMaxLength(30);
        modelBuilder.Entity<NotificationSms>().Property(n => n.Texte).HasMaxLength(320);
        modelBuilder.Entity<NotificationSms>().Property(n => n.Evenement).HasMaxLength(50);
        modelBuilder.Entity<NotificationSms>().HasIndex(n => new { n.UtilisateurId, n.DateEnvoi });
        modelBuilder.Entity<NotificationSms>().HasIndex(n => n.DateEnvoi);

        // Journal : sans clé étrangère (la trace survit à la suppression de l'utilisateur ou de la cible).
        modelBuilder.Entity<JournalAction>().Property(j => j.UtilisateurNom).HasMaxLength(100);
        modelBuilder.Entity<JournalAction>().Property(j => j.Role).HasMaxLength(20);
        modelBuilder.Entity<JournalAction>().Property(j => j.Action).HasMaxLength(60);
        modelBuilder.Entity<JournalAction>().Property(j => j.Cible).HasMaxLength(30);
        modelBuilder.Entity<JournalAction>().Property(j => j.Details).HasMaxLength(1000);
        modelBuilder.Entity<JournalAction>().Property(j => j.AdresseIp).HasMaxLength(64);
        modelBuilder.Entity<JournalAction>().HasIndex(j => j.Date);
        modelBuilder.Entity<JournalAction>().HasIndex(j => new { j.UtilisateurId, j.Date });
        modelBuilder.Entity<JournalAction>().HasIndex(j => new { j.Cible, j.CibleId });

        modelBuilder.Entity<Signalement>()
            .HasOne(s => s.Auteur)
            .WithMany()
            .HasForeignKey(s => s.AuteurId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Signalement>()
            .HasOne(s => s.TraitePar)
            .WithMany()
            .HasForeignKey(s => s.TraiteParId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Signalement>().Property(s => s.AuteurEmail).HasMaxLength(200);
        modelBuilder.Entity<Signalement>().Property(s => s.Cible).HasMaxLength(20);
        modelBuilder.Entity<Signalement>().Property(s => s.Texte).HasMaxLength(1000);
        modelBuilder.Entity<Signalement>().Property(s => s.DecisionAdmin).HasMaxLength(1000);
        modelBuilder.Entity<Signalement>().Property(s => s.ActionAppliquee).HasMaxLength(30);
        modelBuilder.Entity<Signalement>().Property(s => s.AdresseIp).HasMaxLength(64);
        modelBuilder.Entity<Signalement>().HasIndex(s => new { s.Statut, s.DateCreation });
        modelBuilder.Entity<Signalement>().HasIndex(s => new { s.Cible, s.CibleId });

        modelBuilder.Entity<PieceIdentite>()
            .HasOne(p => p.Utilisateur)
            .WithMany()
            .HasForeignKey(p => p.UtilisateurId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PieceIdentite>()
            .HasOne(p => p.DecidePar)
            .WithMany()
            .HasForeignKey(p => p.DecideParId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PieceIdentite>().Property(p => p.MotifRefus).HasMaxLength(500);
        // Une seule demande en attente par utilisateur, même en cas de double envoi.
        modelBuilder.Entity<PieceIdentite>().HasIndex(p => p.UtilisateurId).IsUnique()
            .HasFilter("\"Statut\" = 0");
        modelBuilder.Entity<PieceIdentite>().HasIndex(p => new { p.Statut, p.DateEnvoi });

        // --- Précision des montants (obligatoire pour SQLite) ---
        modelBuilder.Entity<Appartement>().Property(a => a.LoyerMensuel).HasPrecision(18, 2);
        modelBuilder.Entity<Appartement>().Property(a => a.PrixParNuit).HasPrecision(18, 2);
        modelBuilder.Entity<FinancementLocation>().Property(f => f.MontantTotal).HasPrecision(18, 2);
        modelBuilder.Entity<FinancementLocation>().Property(f => f.ApportInitial).HasPrecision(18, 2);
        modelBuilder.Entity<FinancementLocation>().Property(f => f.MontantARembourser).HasPrecision(18, 2);
        modelBuilder.Entity<FinancementLocation>().Property(f => f.TauxFrais).HasPrecision(5, 4);
        modelBuilder.Entity<FinancementLocation>().Property(f => f.MontantMensualite).HasPrecision(18, 2);
        modelBuilder.Entity<Mensualite>().Property(m => m.MontantDu).HasPrecision(18, 2);
        modelBuilder.Entity<Mensualite>().Property(m => m.MontantPaye).HasPrecision(18, 2);
        modelBuilder.Entity<Paiement>().Property(p => p.Montant).HasPrecision(18, 2);
        modelBuilder.Entity<Appartement>().Property(a => a.FraisMenage).HasPrecision(18, 2);
        modelBuilder.Entity<Appartement>().Property(a => a.Caution).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.MontantTotal).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.PrixParNuit).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.ReductionMontant).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.FraisMenage).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.Caution).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.MontantARembourser).HasPrecision(18, 2);
        modelBuilder.Entity<Reservation>().Property(r => r.MontantRembourse).HasPrecision(18, 2);
        modelBuilder.Entity<Candidature>().Property(c => c.ApportInitial).HasPrecision(18, 2);
    }
}
