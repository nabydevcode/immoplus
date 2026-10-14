namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Trace de chaque SMS / WhatsApp envoyé (ou simulé) : historique admin et « mes SMS ».</summary>
public class NotificationSms
{
    public int Id { get; set; }
    public int UtilisateurId { get; set; }
    /// <summary>Numéro normalisé (+224…) ou, en cas d'échec, tel que saisi dans le profil.</summary>
    public string Telephone { get; set; } = string.Empty;
    public CanalMessage Canal { get; set; }
    /// <summary>320 caractères au plus (deux SMS).</summary>
    public string Texte { get; set; } = string.Empty;
    public StatutNotificationSms Statut { get; set; }
    public string? ReferenceFournisseur { get; set; }
    public string? Erreur { get; set; }
    public DateTime DateEnvoi { get; set; } = DateTime.UtcNow;
    /// <summary>Événement déclencheur, ex. « reservation-confirmee ».</summary>
    public string Evenement { get; set; } = string.Empty;

    public Utilisateur? Utilisateur { get; set; }
}
