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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Relations ---

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
        modelBuilder.Entity<Reservation>().Property(r => r.MontantTotal).HasPrecision(18, 2);
        modelBuilder.Entity<Candidature>().Property(c => c.ApportInitial).HasPrecision(18, 2);
    }
}
