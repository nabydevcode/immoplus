namespace ImmoPlus.Api.Dtos;

public class LocationResponseDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = string.Empty;
    public int? CandidatureId { get; set; }
    public DateTime DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public string Statut { get; set; } = string.Empty;
}
