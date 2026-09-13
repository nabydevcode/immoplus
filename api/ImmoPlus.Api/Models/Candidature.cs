namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Candidature
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public int AppartementId { get; set; }
    public decimal ApportInitial { get; set; }
    public SituationProfessionnelle SituationProfessionnelle { get; set; }
    public StatutCandidature Statut { get; set; } = StatutCandidature.EnAttente;
    public DateTime DateSoumission { get; set; } = DateTime.UtcNow;
    public DateTime? DateTraitement { get; set; }
    public string? MotifRefus { get; set; }

    public Utilisateur? Locataire { get; set; }
    public Appartement? Appartement { get; set; }
    public Garant? Garant { get; set; }
    public List<Document> Documents { get; set; } = new();
}
