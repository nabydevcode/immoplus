namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Reservation
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public int AppartementId { get; set; }
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public decimal MontantTotal { get; set; }
    public StatutReservation Statut { get; set; } = StatutReservation.EnAttente;
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public Utilisateur? Locataire { get; set; }
    public Appartement? Appartement { get; set; }
    public List<Paiement> Paiements { get; set; } = new();
}
