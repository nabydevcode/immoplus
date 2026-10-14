namespace ImmoPlus.Api.Dtos;

public class PhotoAppartementDto
{
    public int Id { get; set; }
    /// <summary>Chemin relatif à l'URL de l'API, accessible sans authentification (les annonces sont publiques).</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Miniature 480 px (JPEG), mêmes règles d'accès que Url : cartes, listes, vignettes de galerie.</summary>
    public string UrlMiniature { get; set; } = string.Empty;
    public int Ordre { get; set; }
    public bool Est360 { get; set; }
    public string? Libelle { get; set; }
}
