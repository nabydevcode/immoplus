namespace ImmoPlus.Api.Models;

/// <summary>Message échangé entre le voyageur et l'hôte, rattaché à une réservation.</summary>
public class Message
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public int AuteurId { get; set; }
    public string Contenu { get; set; } = string.Empty;
    public DateTime DateEnvoi { get; set; } = DateTime.UtcNow;
    public DateTime? DateLecture { get; set; }

    public Reservation? Reservation { get; set; }
    public Utilisateur? Auteur { get; set; }
}
