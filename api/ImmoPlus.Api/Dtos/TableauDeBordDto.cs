namespace ImmoPlus.Api.Dtos;

/// <summary>Tableau de bord de l'admin : compteurs et actions en attente.</summary>
public class TableauDeBordDto
{
    public int UtilisateursAValider { get; set; }
    public int CandidaturesAEtudier { get; set; }
    public int VisitesAVenir { get; set; }
    public int Logements { get; set; }
    public int LocationsEnCours { get; set; }
    public List<DossierAPrendreEnChargeDto> DossiersAPrendreEnCharge { get; set; } = new();
    public List<CleARemettreDto> ClesARemettre { get; set; } = new();
    public List<EcheancierAGenererDto> EcheanciersAGenerer { get; set; } = new();
    public RetardsTableauDeBordDto Retards { get; set; } = new();
    public List<PreavisEnCoursDto> PreavisEnCours { get; set; } = new();
    public int ReservationsEnAttentePaiement { get; set; }
    public int ReservationsAVenir7Jours { get; set; }
    public List<RemboursementAEffectuerDto> RemboursementsAEffectuer { get; set; } = new();
}

/// <summary>Réservation annulée dont le remboursement n'est pas encore (entièrement) enregistré.</summary>
public class RemboursementAEffectuerDto
{
    public int ReservationId { get; set; }
    public string VoyageurNom { get; set; } = string.Empty;
    public decimal MontantARembourser { get; set; }
    public decimal MontantRembourse { get; set; }
    public DateTime? DateAnnulation { get; set; }
}

/// <summary>Dossier accepté dont l'apport n'est pas encore payé.</summary>
public class DossierAPrendreEnChargeDto
{
    public int CandidatureId { get; set; }
    public int LocationId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public string AppartementAdresse { get; set; } = string.Empty;
    public decimal ApportInitial { get; set; }
    public DateTime DateAttribution { get; set; }
}

public class CleARemettreDto
{
    public int? CandidatureId { get; set; }
    public int LocationId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public string AppartementAdresse { get; set; } = string.Empty;
    public DateTime DateApportPaye { get; set; }
}

public class EcheancierAGenererDto
{
    public int FinancementId { get; set; }
    public int? CandidatureId { get; set; }
    public int LocationId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public decimal MontantARembourser { get; set; }
}

public class RetardsTableauDeBordDto
{
    public int Nombre { get; set; }
    public decimal Montant { get; set; }
    /// <summary>Les 10 échéances les plus en retard.</summary>
    public List<RetardTableauDeBordDto> Liste { get; set; } = new();
}

public class RetardTableauDeBordDto
{
    /// <summary>« Mensualite » ou « Loyer ».</summary>
    public string Type { get; set; } = string.Empty;
    public int? CandidatureId { get; set; }
    public int LocationId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public DateTime DateEcheance { get; set; }
    public int JoursRetard { get; set; }
    public decimal ResteDu { get; set; }
}

public class PreavisEnCoursDto
{
    public int LocationId { get; set; }
    public int? CandidatureId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public string AppartementAdresse { get; set; } = string.Empty;
    public DateTime DateFinPrevue { get; set; }
    public bool FinAtteinte { get; set; }
}
