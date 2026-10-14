namespace ImmoPlus.Api.Models;

public class PhotoAppartement
{
    public int Id { get; set; }
    public int AppartementId { get; set; }
    public string NomFichierOriginal { get; set; } = string.Empty;
    public string CheminStockage { get; set; } = string.Empty;
    /// <summary>Position dans la galerie : la photo d'ordre le plus bas sert de couverture.</summary>
    public int Ordre { get; set; }
    /// <summary>Panorama 360° (équirectangulaire) pour la visite virtuelle, plutôt qu'une photo classique.</summary>
    public bool Est360 { get; set; }
    /// <summary>Nom de la pièce affiché dans la visite 360° (ex. « Salon »).</summary>
    public string? Libelle { get; set; }
    public DateTime DateUpload { get; set; } = DateTime.UtcNow;

    public Appartement? Appartement { get; set; }
}
