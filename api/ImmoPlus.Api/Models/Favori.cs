namespace ImmoPlus.Api.Models;

/// <summary>Logement mis de côté par un utilisateur (clé composite UtilisateurId + AppartementId).</summary>
public class Favori
{
    public int UtilisateurId { get; set; }
    public int AppartementId { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public Utilisateur? Utilisateur { get; set; }
    public Appartement? Appartement { get; set; }
}
