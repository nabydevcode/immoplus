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

    public Utilisateur? Locataire { get; set; }
    public Appartement? Appartement { get; set; }
    public Candidature? Candidature { get; set; }
    public FinancementLocation? FinancementLocation { get; set; }
}
