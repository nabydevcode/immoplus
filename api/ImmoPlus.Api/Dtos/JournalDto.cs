namespace ImmoPlus.Api.Dtos;

public class JournalActionDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int? UtilisateurId { get; set; }
    public string UtilisateurNom { get; set; } = string.Empty;
    /// <summary>« Admin », « Bailleur », « Locataire », « Systeme » ou « Anonyme ».</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Code court, ex. « utilisateur.desactiver ».</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Ex. « Utilisateur », « Appartement », « Candidature », « Location », « Reservation », « Paiement »…</summary>
    public string Cible { get; set; } = string.Empty;
    public int? CibleId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? AdresseIp { get; set; }
}
