namespace ImmoPlus.Api.Dtos;

public class AppartementDto
{
    public int BailleurId { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeOffre { get; set; } = string.Empty;

    // Longue durée
    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    // Séjour court
    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }
}
