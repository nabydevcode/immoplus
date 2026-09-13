namespace ImmoPlus.Api.Dtos;

public class DocumentResponseDto
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    public string Proprietaire { get; set; } = string.Empty;
    public string TypeDocument { get; set; } = string.Empty;
    public string NomFichierOriginal { get; set; } = string.Empty;
    public long TailleOctets { get; set; }
    public DateTime DateUpload { get; set; }
}
