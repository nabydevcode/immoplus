namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Document
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    public ProprietaireDocument Proprietaire { get; set; }
    public TypeDocument TypeDocument { get; set; }
    public string NomFichierOriginal { get; set; } = string.Empty;
    public string CheminStockage { get; set; } = string.Empty;
    public long TailleOctets { get; set; }
    public DateTime DateUpload { get; set; } = DateTime.UtcNow;

    public Candidature? Candidature { get; set; }
}
