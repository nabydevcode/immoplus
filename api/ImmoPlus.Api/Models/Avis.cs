namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Évaluation laissée après un séjour terminé (réservation) ou une location longue durée (location) :
/// un avis par sens et par séjour / location. Exactement un de ReservationId et LocationId est renseigné.
/// </summary>
public class Avis
{
    public int Id { get; set; }
    public int? ReservationId { get; set; }
    public int? LocationId { get; set; }
    public int AuteurId { get; set; }
    /// <summary>Utilisateur noté : l'hôte (VoyageurVersLogement) ou le voyageur (HoteVersVoyageur).</summary>
    public int CibleId { get; set; }
    public int AppartementId { get; set; }
    public SensAvis Sens { get; set; }
    public int Note { get; set; }
    // Sous-notes : uniquement pour l'avis du voyageur sur le logement.
    public int? Proprete { get; set; }
    public int? Communication { get; set; }
    public int? Emplacement { get; set; }
    public int? RapportQualitePrix { get; set; }
    public string Commentaire { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public string? ReponseHote { get; set; }
    public DateTime? DateReponse { get; set; }
    /// <summary>Masqué par un admin (signalement) : exclu de l'affichage public et des moyennes.</summary>
    public bool Masque { get; set; }

    public Reservation? Reservation { get; set; }
    public Location? Location { get; set; }
    public Utilisateur? Auteur { get; set; }
    public Utilisateur? Cible { get; set; }
    public Appartement? Appartement { get; set; }
}
