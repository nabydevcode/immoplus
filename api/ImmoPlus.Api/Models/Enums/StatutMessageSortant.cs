namespace ImmoPlus.Api.Models.Enums;

/// <summary>Cycle de vie d'un message de la boîte d'envoi.</summary>
public enum StatutMessageSortant
{
    /// <summary>Pas encore tenté.</summary>
    EnAttente,
    Envoye,
    /// <summary>Dernière tentative en échec : nouvel essai à ProchaineTentative.</summary>
    Echec,
    /// <summary>5 tentatives en échec (ou erreur définitive) : plus aucun essai automatique.</summary>
    Abandonne
}
