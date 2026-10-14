namespace ImmoPlus.Api.Dtos;

public class FinancementLocationResponseDto
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    /// <summary>Dossier de candidature à l'origine de la location (référence affichée et recherchable).</summary>
    public int? CandidatureId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public string AppartementAdresse { get; set; } = string.Empty;
    public decimal MontantTotal { get; set; }
    public int DureeMois { get; set; }
    public decimal ApportInitial { get; set; }
    public decimal MontantARembourser { get; set; }
    public decimal TauxFrais { get; set; }
    public int NombreMensualites { get; set; }
    public decimal MontantMensualite { get; set; }
    public string Statut { get; set; } = string.Empty;

    // Bilan de ponctualité des mensualités
    public int MensualitesPayeesATemps { get; set; }
    public int MensualitesPayeesEnRetard { get; set; }
    public int MensualitesEnRetard { get; set; }
    public int JoursRetardCumules { get; set; }
    public decimal MontantEnRetard { get; set; }
}
