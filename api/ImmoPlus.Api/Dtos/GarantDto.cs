namespace ImmoPlus.Api.Dtos;

public class GarantDto
{
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string SituationProfessionnelle { get; set; } = string.Empty;
}

public class GarantResponseDto
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string SituationProfessionnelle { get; set; } = string.Empty;
}
