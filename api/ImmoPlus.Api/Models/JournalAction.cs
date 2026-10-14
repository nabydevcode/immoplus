namespace ImmoPlus.Api.Models;

/// <summary>
/// Trace d'une action importante (connexion, décision d'un admin ou d'un bailleur, paiement…) : qui, quand, sur quoi.
/// Volontairement sans clé étrangère : la trace survit à la suppression de l'utilisateur ou de la cible. Conservée 2 ans.
/// </summary>
public class JournalAction
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    /// <summary>null : action du système (webhook, tâche planifiée) ou d'un visiteur non connecté.</summary>
    public int? UtilisateurId { get; set; }
    public string UtilisateurNom { get; set; } = string.Empty;
    /// <summary>« Admin », « Bailleur », « Locataire », « Systeme » ou « Anonyme ».</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Code court « cible.verbe », ex. « utilisateur.desactiver ».</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Type de la cible, ex. « Utilisateur », « Reservation », « Candidature ».</summary>
    public string Cible { get; set; } = string.Empty;
    public int? CibleId { get; set; }
    /// <summary>Texte lisible en français (1000 caractères au plus), jamais de mot de passe.</summary>
    public string Details { get; set; } = string.Empty;
    public string? AdresseIp { get; set; }
}
