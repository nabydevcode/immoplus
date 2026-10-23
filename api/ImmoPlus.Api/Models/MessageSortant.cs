namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Boîte d'envoi (« outbox ») : chaque email ou SMS est d'abord enregistré ici, dans la transaction de l'action métier
/// qui le déclenche, puis envoyé en arrière-plan par EnvoiMessagesHostedService (nouvelles tentatives en cas d'échec).
/// L'utilisateur n'attend jamais le serveur SMTP ni la passerelle SMS, et un message n'existe que si l'action a réussi.
/// Sans clé étrangère, comme le journal : la trace survit au destinataire. Purgée après 90 jours une fois traitée.
/// </summary>
public class MessageSortant
{
    public int Id { get; set; }
    public TypeMessageSortant Type { get; set; }
    /// <summary>Adresse email, ou numéro de téléphone tel que saisi dans le profil (normalisé à l'envoi).</summary>
    public string Destinataire { get; set; } = string.Empty;
    public int? UtilisateurId { get; set; }
    /// <summary>Objet de l'email (null pour un SMS).</summary>
    public string? Sujet { get; set; }
    /// <summary>Corps HTML de l'email ou texte du SMS.</summary>
    public string Contenu { get; set; } = string.Empty;
    /// <summary>Référence affichée, ex. « Dossier n° 000048 ».</summary>
    public string? Reference { get; set; }
    /// <summary>Événement déclencheur, ex. « DossierAccepte » ou « reservation-confirmee ».</summary>
    public string Evenement { get; set; } = string.Empty;
    /// <summary>SMS ou WhatsApp (SMS uniquement).</summary>
    public CanalMessage? Canal { get; set; }
    public StatutMessageSortant Statut { get; set; } = StatutMessageSortant.EnAttente;
    public int Tentatives { get; set; }
    public DateTime ProchaineTentative { get; set; } = DateTime.UtcNow;
    public string? DerniereErreur { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime? DateDerniereTentative { get; set; }
    /// <summary>Date de l'envoi réussi.</summary>
    public DateTime? DateEnvoi { get; set; }
}
