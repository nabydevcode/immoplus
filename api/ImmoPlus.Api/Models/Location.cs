namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Location
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public int AppartementId { get; set; }
    public int? CandidatureId { get; set; }
    public DateTime DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public StatutLocation Statut { get; set; } = StatutLocation.EnCours;

    // Étapes après l'attribution : le locataire paie son apport, puis le bailleur lui remet les clés.
    public DateTime? DateApportPaye { get; set; }
    public string? ModePaiementApport { get; set; }
    public string? ReferenceApport { get; set; }
    public DateTime? DateRemiseCles { get; set; }

    // Fin de location : préavis de 3 mois, puis clôture par l'admin à la date de fin prévue.
    public DateTime? DatePreavis { get; set; }
    public DateTime? DateFinPrevue { get; set; }
    /// <summary>« Locataire » ou « Bailleur ».</summary>
    public string? PreavisDonnePar { get; set; }
    public string? MotifPreavis { get; set; }

    public Utilisateur? Locataire { get; set; }
    public Appartement? Appartement { get; set; }
    public Candidature? Candidature { get; set; }
    public FinancementLocation? FinancementLocation { get; set; }
    public List<Loyer> Loyers { get; set; } = new();
}
