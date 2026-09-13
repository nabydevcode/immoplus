namespace ImmoPlus.Api.Dtos;

public class CandidatureDto
{
    public int AppartementId { get; set; }
    public decimal ApportInitial { get; set; }
    public string SituationProfessionnelle { get; set; } = string.Empty;
    public GarantDto? Garant { get; set; }
}

public class AccepterCandidatureDto
{
    // Optionnel : entre 0.02 et 0.05. Si omis et qu'un financement est nécessaire, 0.03 est utilisé par défaut.
    public decimal? TauxFrais { get; set; }
}

public class RefuserCandidatureDto
{
    public string? MotifRefus { get; set; }
}

public class CandidatureResponseDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = string.Empty;
    public decimal ApportInitial { get; set; }
    public string SituationProfessionnelle { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    public DateTime DateSoumission { get; set; }
    public DateTime? DateTraitement { get; set; }
    public string? MotifRefus { get; set; }
    public GarantResponseDto? Garant { get; set; }
    public List<DocumentResponseDto> Documents { get; set; } = new();
    public int? LocationId { get; set; }
    public int? FinancementLocationId { get; set; }
}
