namespace ImmoPlus.Api.Dtos;

/// <summary>Tableau de bord de l'hôte : revenus, occupation des séjours courts, arrivées et actions en attente.</summary>
public class TableauDeBordHoteDto
{
    public int Annee { get; set; }
    /// <summary>Revenus nets de l'année : paiements reçus (séjours courts + loyers) − remboursements versés aux voyageurs.</summary>
    public decimal RevenusTotal { get; set; }
    /// <summary>Remboursements versés aux voyageurs sur l'année (valeur positive, déjà déduite de RevenusTotal).</summary>
    public decimal RemboursementsTotal { get; set; }
    public List<RevenusMoisDto> RevenusParMois { get; set; } = new();
    /// <summary>0–100 (1 décimale) ; null sans séjour court ou sans nuit écoulée.</summary>
    public decimal? TauxOccupation { get; set; }
    public List<OccupationLogementDto> OccupationParLogement { get; set; } = new();
    public List<ProchaineArriveeDto> ProchainesArrivees { get; set; } = new();
    public int DemandesEnAttente { get; set; }
    public int MessagesNonLus { get; set; }
    public decimal? NoteMoyenne { get; set; }
    public int NombreAvis { get; set; }
    public bool Superhote { get; set; }
    public decimal? TauxReponse { get; set; }
}

public class RevenusMoisDto
{
    public int Mois { get; set; }
    public decimal Sejours { get; set; }
    public decimal Loyers { get; set; }
    /// <summary>Remboursements versés aux voyageurs ce mois-ci (mois de DateRemboursement), valeur positive à déduire.</summary>
    public decimal Remboursements { get; set; }
}

public class OccupationLogementDto
{
    public int AppartementId { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public int NuitsReservees { get; set; }
    public int NuitsPossibles { get; set; }
    /// <summary>0–100 (1 décimale) ; null si aucune nuit possible.</summary>
    public decimal? Taux { get; set; }
}

public class ProchaineArriveeDto
{
    public int ReservationId { get; set; }
    public string VoyageurNom { get; set; } = string.Empty;
    public string AppartementAdresse { get; set; } = string.Empty;
    public DateOnly DateArrivee { get; set; }
    public DateOnly DateDepart { get; set; }
    public int NombreVoyageurs { get; set; }
}
