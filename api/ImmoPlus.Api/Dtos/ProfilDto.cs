namespace ImmoPlus.Api.Dtos;

/// <summary>Modification de son propre profil par l'utilisateur connecté (rôle et statut non modifiables).</summary>
public class ModifierProfilDto
{
    public string? Nom { get; set; }
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    /// <summary>Recevoir les notifications par SMS / WhatsApp ; null = inchangé.</summary>
    public bool? NotificationsSms { get; set; }
}

public class ChangerMotDePasseDto
{
    public string? MotDePasseActuel { get; set; }
    public string? NouveauMotDePasse { get; set; }
}
