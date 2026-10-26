namespace ImmoPlus.Api.Dtos;

/// <summary>
/// Événement SignalR « utilisateurChange » poussé aux admins connectés : seulement l'id et l'état du compte,
/// le front relit la liste (aucune donnée personnelle dans le flux temps réel).
/// </summary>
public class UtilisateurChangeDto
{
    public int UtilisateurId { get; set; }
    /// <summary>inscrit, emailConfirme, valide, refuse, desactive, reactive, adminCree ou profilModifie.</summary>
    public string Type { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    public bool EmailConfirme { get; set; }
}
