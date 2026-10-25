namespace ImmoPlus.Api.Models.Enums;

/// <summary>Copie d'un email envoyé dans le dossier « Envoyés » de la boîte de l'expéditeur (Imap:Actif), après l'envoi SMTP.</summary>
public enum StatutCopieEnvoyes
{
    /// <summary>Copie déposée par IMAP APPEND.</summary>
    Ok,
    /// <summary>Dépôt en échec : nouvel essai de la copie seule à CopieProchaineTentative (l'email, lui, est parti).</summary>
    Echec,
    /// <summary>4 essais en échec : plus aucun essai automatique.</summary>
    Abandonnee
}
