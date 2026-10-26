namespace ImmoPlus.Api.Dtos;

/// <summary>Jeton reçu dans le lien « {Frontend}/confirmer-email?jeton=… ».</summary>
public class ConfirmerEmailDto
{
    public string Jeton { get; set; } = string.Empty;
}

public class RenvoyerConfirmationDto
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>Réponse de la confirmation : message à afficher et statut du compte (EnAttente en général, Valide si déjà validé).</summary>
public class ConfirmationEmailResponseDto
{
    public string Message { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    /// <summary>Adresse confirmée (donnée seulement à qui détient le jeton) : permet à l'écran d'attente de l'onglet d'origine de reconnaître sa confirmation.</summary>
    public string Email { get; set; } = string.Empty;
}
