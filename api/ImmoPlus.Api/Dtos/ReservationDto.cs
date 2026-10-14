namespace ImmoPlus.Api.Dtos;

public class ReservationDto
{
    public int AppartementId { get; set; }
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public int NombreVoyageurs { get; set; } = 1;
}

public class RefuserReservationDto
{
    public string? Motif { get; set; }
}

public class AnnulerReservationDto
{
    public string? Motif { get; set; }
}

/// <summary>Remboursement calculé pour l'appelant avant de confirmer l'annulation.</summary>
public class ApercuAnnulationDto
{
    /// <summary>« Flexible », « Moderee » ou « Stricte ».</summary>
    public string Politique { get; set; } = string.Empty;
    public decimal MontantPaye { get; set; }
    public decimal MontantRembourse { get; set; }
    public int PourcentageNuitees { get; set; }
    public string Explication { get; set; } = string.Empty;
}

public class RemboursementDto
{
    public decimal Montant { get; set; }
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>Prix détaillé d'un séjour avant réservation (même calcul que la création de la réservation).</summary>
public class DevisReservationDto
{
    public int NombreNuits { get; set; }
    public decimal PrixParNuit { get; set; }
    public decimal SousTotal { get; set; }
    public int ReductionPct { get; set; }
    public decimal ReductionMontant { get; set; }
    public decimal FraisMenage { get; set; }
    /// <summary>SousTotal − ReductionMontant + FraisMenage (la caution est à part).</summary>
    public decimal MontantTotal { get; set; }
    public decimal Caution { get; set; }
    public bool ReservationInstantanee { get; set; }
    public bool Disponible { get; set; }
    public string? Raison { get; set; }
}

public class ReservationResponseDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = string.Empty;
    public string? AppartementVille { get; set; }
    public string? AppartementQuartier { get; set; }
    public string BailleurNom { get; set; } = string.Empty;
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public int NombreVoyageurs { get; set; }
    public int NombreNuits { get; set; }
    public decimal PrixParNuit { get; set; }
    public decimal ReductionMontant { get; set; }
    public decimal FraisMenage { get; set; }
    public decimal Caution { get; set; }
    public decimal MontantTotal { get; set; }
    public decimal MontantPaye { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string? MotifRefus { get; set; }
    public DateTime? DateReponseHote { get; set; }
    public DateTime DateCreation { get; set; }

    // Avis (après un séjour terminé)
    public bool AvisVoyageurDonne { get; set; }
    public bool AvisHoteDonne { get; set; }
    /// <summary>Dernier jour pour laisser un avis (DateDepart + 14), si le séjour est terminé.</summary>
    public DateOnly? AvisPossibleJusquau { get; set; }

    /// <summary>Messages reçus par l'appelant et non lus.</summary>
    public int MessagesNonLus { get; set; }

    // Annulation et remboursement
    public string PolitiqueAnnulation { get; set; } = string.Empty;
    public decimal? MontantARembourser { get; set; }
    public decimal? MontantRembourse { get; set; }
    public DateOnly? DateRemboursement { get; set; }
    /// <summary>« Voyageur », « Hote », « Admin » ou « Systeme » (annulation automatique).</summary>
    public string? AnnuleePar { get; set; }
    public string? MotifAnnulation { get; set; }
    public DateTime? DateAnnulation { get; set; }

    /// <summary>Réservation en attente sans aucun paiement : annulée automatiquement si rien n'est payé avant cette date (UTC). Null sinon.</summary>
    public DateTime? PayerAvant { get; set; }

    // Arrivée : heures toujours visibles ; adresse exacte, instructions, position et téléphone de l'hôte une fois le séjour confirmé.
    public string? HeureArrivee { get; set; }
    public string? HeureDepart { get; set; }
    public string? InstructionsArrivee { get; set; }
    public string? AdresseComplete { get; set; }
    public string? TelephoneHote { get; set; }
    public double? LatitudeExacte { get; set; }
    public double? LongitudeExacte { get; set; }
}

/// <summary>Paiement d'une réservation (historique affiché au voyageur et à l'hôte).</summary>
public class PaiementReservationDto
{
    public int Id { get; set; }
    public decimal Montant { get; set; }
    public DateTime DatePaiement { get; set; }
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
