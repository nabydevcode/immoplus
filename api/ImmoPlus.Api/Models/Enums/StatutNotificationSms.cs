namespace ImmoPlus.Api.Models.Enums;

public enum StatutNotificationSms
{
    Envoye,
    /// <summary>Numéro invalide ou erreur du fournisseur : l'action métier n'est jamais bloquée.</summary>
    Echec,
    /// <summary>Mode simulation : enregistré comme s'il était parti, aucun SMS réel.</summary>
    Simule
}
