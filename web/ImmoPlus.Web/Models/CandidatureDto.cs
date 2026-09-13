namespace ImmoPlus.Web.Models;

public class GarantDto
{
    public string Nom { get; set; } = "";
    public string Telephone { get; set; } = "";
    public string? Email { get; set; }
    public string SituationProfessionnelle { get; set; } = "";
}

public class DocumentDto
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    public string Proprietaire { get; set; } = "";
    public string TypeDocument { get; set; } = "";
    public string NomFichierOriginal { get; set; } = "";
    public long TailleOctets { get; set; }
    public DateTime DateUpload { get; set; }
}

public class CandidatureModele
{
    public int AppartementId { get; set; }
    public decimal ApportInitial { get; set; }
    public string SituationProfessionnelle { get; set; } = "CDI";
    public GarantDto? Garant { get; set; }
}

public class CandidatureDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = "";
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = "";
    public decimal ApportInitial { get; set; }
    public string SituationProfessionnelle { get; set; } = "";
    public string Statut { get; set; } = "";
    public DateTime DateSoumission { get; set; }
    public DateTime? DateTraitement { get; set; }
    public string? MotifRefus { get; set; }
    public GarantDto? Garant { get; set; }
    public List<DocumentDto> Documents { get; set; } = new();
    public int? LocationId { get; set; }
    public int? FinancementLocationId { get; set; }
}
