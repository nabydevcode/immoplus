namespace ImmoPlus.Api.Dtos;

public class AppartementResponseDto
{
    public int Id { get; set; }
    public int BailleurId { get; set; }
    public string BailleurNom { get; set; } = string.Empty;
    /// <summary>Séjour court vu par le public : « Quartier, Ville » seulement (voir AdresseMasquee).</summary>
    public string Adresse { get; set; } = string.Empty;
    /// <summary>true quand Adresse est un libellé approximatif (séjour court, appelant ni propriétaire ni admin).</summary>
    public bool AdresseMasquee { get; set; }
    /// <summary>Logement supprimé ou hôte désactivé : photos servies seulement avec le jeton du propriétaire ou d'un admin.</summary>
    public bool PhotosProtegees { get; set; }
    /// <summary>L'hôte est désactivé (annonce masquée au public) : propriétaire et admin seulement, false sinon.</summary>
    public bool BailleurDesactive { get; set; }
    public string? Description { get; set; }
    public bool Disponible { get; set; }
    public bool EstDesactive { get; set; }
    public DateTime? DateDesactivation { get; set; }
    /// <summary>Une location est en cours sur ce logement : il ne peut pas être modifié.</summary>
    public bool EnLocation { get; set; }
    public List<PhotoAppartementDto> Photos { get; set; } = new();
    public string TypeOffre { get; set; } = string.Empty;

    public string? Ville { get; set; }
    public string? Quartier { get; set; }
    public int? Capacite { get; set; }
    public int? Chambres { get; set; }
    public int? Lits { get; set; }
    public int? SallesDeBain { get; set; }
    public List<string> Equipements { get; set; } = new();

    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
    public decimal? FraisMenage { get; set; }
    public decimal? Caution { get; set; }
    public int? ReductionSemainePct { get; set; }
    public int? ReductionMoisPct { get; set; }
    public int? NuitsMin { get; set; }
    public bool ReservationInstantanee { get; set; }
    /// <summary>« Flexible », « Moderee » ou « Stricte » ; null pour une location longue durée.</summary>
    public string? PolitiqueAnnulation { get; set; }

    /// <summary>Moyenne des avis des voyageurs (1 décimale), null sans avis.</summary>
    public decimal? NoteMoyenne { get; set; }
    public int NombreAvis { get; set; }
    public HoteDto? Hote { get; set; }

    /// <summary>« HH:mm » ; null pour une location longue durée.</summary>
    public string? HeureArrivee { get; set; }
    public string? HeureDepart { get; set; }
    /// <summary>Uniquement pour le propriétaire et l'admin (null sinon).</summary>
    public string? InstructionsArrivee { get; set; }
    /// <summary>Exactes pour le propriétaire et l'admin, arrondies à 3 décimales (≈ 100 m) pour le public.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>Nombre d'utilisateurs ayant mis le logement en favori : propriétaire et admin seulement (0 sinon).</summary>
    public int NombreFavoris { get; set; }
}
