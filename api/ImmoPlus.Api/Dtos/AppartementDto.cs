namespace ImmoPlus.Api.Dtos;

public class AppartementDto
{
    public int BailleurId { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeOffre { get; set; } = string.Empty;

    // Localisation et description (tous types d'offre)
    public string? Ville { get; set; }
    public string? Quartier { get; set; }
    public int? Capacite { get; set; }
    public int? Chambres { get; set; }
    public int? Lits { get; set; }
    public int? SallesDeBain { get; set; }
    public List<string>? Equipements { get; set; }

    // Longue durée
    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    // Séjour court
    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
    public decimal? FraisMenage { get; set; }
    public decimal? Caution { get; set; }
    public int? ReductionSemainePct { get; set; }
    public int? ReductionMoisPct { get; set; }
    public int? NuitsMin { get; set; }
    public bool ReservationInstantanee { get; set; }
    /// <summary>« Flexible », « Moderee » ou « Stricte » (séjour court ; Moderee par défaut).</summary>
    public string? PolitiqueAnnulation { get; set; }
    /// <summary>« HH:mm » (séjour court ; 14:00 par défaut).</summary>
    public string? HeureArrivee { get; set; }
    /// <summary>« HH:mm » (séjour court ; 11:00 par défaut).</summary>
    public string? HeureDepart { get; set; }
    /// <summary>Séjour court, 2000 caractères max : communiquées au voyageur une fois la réservation confirmée.</summary>
    public string? InstructionsArrivee { get; set; }

    /// <summary>Position sur la carte (tous types) : les deux ou aucune.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
