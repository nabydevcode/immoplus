namespace ImmoPlus.Api.Dtos;

/// <summary>Message de la boîte d'envoi (GET /admin/messages-sortants).</summary>
public class MessageSortantDto
{
    public int Id { get; set; }
    /// <summary>« Email » ou « Sms ».</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Adresse email ou téléphone.</summary>
    public string Destinataire { get; set; } = string.Empty;
    public int? UtilisateurId { get; set; }
    /// <summary>Objet de l'email (null pour un SMS).</summary>
    public string? Sujet { get; set; }
    /// <summary>Texte du SMS (null pour un email : le HTML n'est pas renvoyé).</summary>
    public string? Texte { get; set; }
    /// <summary>Ex. « Dossier n° 000048 ».</summary>
    public string? Reference { get; set; }
    public string Evenement { get; set; } = string.Empty;
    /// <summary>« EnAttente », « Envoye », « Echec » (nouvel essai prévu) ou « Abandonne ».</summary>
    public string Statut { get; set; } = string.Empty;
    public int Tentatives { get; set; }
    /// <summary>Prochain essai (EnAttente / Echec uniquement).</summary>
    public DateTime? ProchaineTentative { get; set; }
    public string? DerniereErreur { get; set; }
    public DateTime DateCreation { get; set; }
    public DateTime? DateDerniereTentative { get; set; }
    public DateTime? DateEnvoi { get; set; }
}

/// <summary>Résultat de POST /admin/messages-sortants/relancer (et /{id}/relancer) : nombre de messages remis en file.</summary>
public class RelanceMessagesDto
{
    public int Relances { get; set; }
}
