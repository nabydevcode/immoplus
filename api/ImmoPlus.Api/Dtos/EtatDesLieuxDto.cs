namespace ImmoPlus.Api.Dtos;

public class PhotoEtatDesLieuxDto
{
    public int Id { get; set; }
    /// <summary>« Arrivee » ou « Depart ».</summary>
    public string Moment { get; set; } = string.Empty;
    public int AuteurId { get; set; }
    public string AuteurNom { get; set; } = string.Empty;
    /// <summary>Chemin relatif à l'URL de l'API, accessible avec le token (voyageur, hôte, admin).</summary>
    public string Url { get; set; } = string.Empty;
    public string? Commentaire { get; set; }
    public DateTime DateCreation { get; set; }
}
