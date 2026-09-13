namespace ImmoPlus.Api.Dtos;

public class AppartementResponseDto
{
    public int Id { get; set; }
    public int BailleurId { get; set; }
    public string BailleurNom { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Disponible { get; set; }
    public string TypeOffre { get; set; } = string.Empty;

    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
}
