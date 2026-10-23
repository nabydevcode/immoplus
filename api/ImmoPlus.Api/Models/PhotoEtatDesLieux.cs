namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Photo de l'état des lieux d'un séjour (arrivée ou départ), prise par le voyageur ou l'hôte.</summary>
public class PhotoEtatDesLieux
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public MomentEtatDesLieux Moment { get; set; }
    public int AuteurId { get; set; }
    /// <summary>Chemin relatif à « etats-des-lieux/ » (clé de stockage : disque Storage/ ou S3) ({reservationId}/{guid}.ext).</summary>
    public string Chemin { get; set; } = string.Empty;
    public string? Commentaire { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public Reservation? Reservation { get; set; }
    public Utilisateur? Auteur { get; set; }
}
