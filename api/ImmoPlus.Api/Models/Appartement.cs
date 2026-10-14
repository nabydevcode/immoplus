namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Appartement
{
    public int Id { get; set; }
    public int BailleurId { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Disponible { get; set; }
    /// <summary>« Supprimé » par l'hôte ou l'admin : masqué partout sauf pour eux, historique et photos conservés, réactivable.</summary>
    public bool EstDesactive { get; set; }
    public DateTime? DateDesactivation { get; set; }
    public TypeOffre TypeOffre { get; set; }

    // Localisation et description du logement (tous types d'offre)
    public string? Ville { get; set; }
    public string? Quartier { get; set; }
    public int? Capacite { get; set; }
    public int? Chambres { get; set; }
    public int? Lits { get; set; }
    public int? SallesDeBain { get; set; }
    /// <summary>Clés d'équipements séparées par des virgules (voir AppartementServices.EquipementsAutorises).</summary>
    public string? Equipements { get; set; }

    // Champs longue durée (renseignés quand TypeOffre == LongueDuree)
    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    // Champs séjour court (renseignés quand TypeOffre == SejourCourt)
    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
    public decimal? FraisMenage { get; set; }
    /// <summary>Remboursable, affichée à part : jamais incluse dans le montant du séjour.</summary>
    public decimal? Caution { get; set; }
    public int? ReductionSemainePct { get; set; }
    public int? ReductionMoisPct { get; set; }
    public int? NuitsMin { get; set; }
    /// <summary>true : réservation directe (en attente de paiement) ; false : l'hôte doit d'abord accepter la demande.</summary>
    public bool ReservationInstantanee { get; set; }
    public PolitiqueAnnulation PolitiqueAnnulation { get; set; } = PolitiqueAnnulation.Moderee;
    /// <summary>« HH:mm » ; null = 14:00 par défaut (séjour court).</summary>
    public string? HeureArrivee { get; set; }
    /// <summary>« HH:mm » ; null = 11:00 par défaut (séjour court).</summary>
    public string? HeureDepart { get; set; }
    /// <summary>Accès, clés, code wifi… : jamais public, seulement au propriétaire, à l'admin et au voyageur confirmé.</summary>
    public string? InstructionsArrivee { get; set; }

    // Position saisie par l'hôte sur la carte (tous types) : exacte pour le propriétaire et le voyageur confirmé, arrondie pour le public.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Date de publication (null pour les annonces antérieures à ce suivi).</summary>
    public DateTime? DateCreation { get; set; }

    public Utilisateur? Bailleur { get; set; }
    public List<Location> Locations { get; set; } = new();
    public List<Candidature> Candidatures { get; set; } = new();
    public List<Reservation> Reservations { get; set; } = new();
    public List<PhotoAppartement> Photos { get; set; } = new();
    public List<Indisponibilite> Indisponibilites { get; set; } = new();
    public List<Favori> Favoris { get; set; } = new();
}
