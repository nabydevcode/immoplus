namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Utilisateur
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MotDePasseHash { get; set; } = string.Empty;
    public RoleUtilisateur Role { get; set; }
    public StatutUtilisateur Statut { get; set; } = StatutUtilisateur.EnAttente;
    /// <summary>Date d'inscription (null pour les comptes antérieurs à ce suivi).</summary>
    public DateTime? DateCreation { get; set; }

    // Réinitialisation du mot de passe : on ne stocke que le hash SHA-256 du jeton envoyé à l'utilisateur.
    public string? JetonReinitialisationHash { get; set; }
    public DateTime? JetonReinitialisationExpiration { get; set; }

    /// <summary>Admin : date du dernier récapitulatif des retards reçu (au plus un par jour).</summary>
    public DateTime? DateDernierRecapRetards { get; set; }
    public string? MotifDesactivation { get; set; }
    public DateTime? DateDesactivation { get; set; }
    /// <summary>Recevoir aussi un SMS / WhatsApp court pour les événements importants (en plus de l'email).</summary>
    public bool NotificationsSms { get; set; } = true;
    /// <summary>Pièce d'identité contrôlée par un admin (badge « Identité vérifiée ») ; indépendant de la validation du compte.</summary>
    public bool IdentiteVerifiee { get; set; }
    public DateTime? DateVerificationIdentite { get; set; }

    public List<Appartement> Appartements { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Candidature> Candidatures { get; set; } = new();
    public List<Reservation> Reservations { get; set; } = new();
}
