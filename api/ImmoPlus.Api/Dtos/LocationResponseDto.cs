namespace ImmoPlus.Api.Dtos;

public class LocationResponseDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = string.Empty;
    public int? CandidatureId { get; set; }
    public DateTime DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public string Statut { get; set; } = string.Empty;
    public DateTime? DateApportPaye { get; set; }
    public DateTime? DateRemiseCles { get; set; }
    public DateTime? DatePreavis { get; set; }
    public DateTime? DateFinPrevue { get; set; }
    public string? PreavisDonnePar { get; set; }
    public string? MotifPreavis { get; set; }

    // Avis longue durée : un par sens pour toute la location.
    public bool AvisLocataireDonne { get; set; }
    public bool AvisBailleurDonne { get; set; }
    /// <summary>La fenêtre d'avis est ouverte aujourd'hui (location terminée depuis 30 jours au plus, ou en cours depuis 6 mois).</summary>
    public bool AvisPossible { get; set; }
    /// <summary>Début de la fenêtre : remise des clés + 6 mois (en cours) ou date de fin (terminée) ; null si inconnue.</summary>
    public DateTime? AvisPossibleDepuis { get; set; }
    /// <summary>Fin de la fenêtre : date de fin + 30 jours (ou fin prévue + 30 jours si un préavis est en cours) ; null si inconnue.</summary>
    public DateTime? AvisPossibleJusquau { get; set; }
}
