namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Garant
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public SituationProfessionnelle SituationProfessionnelle { get; set; }

    public Candidature? Candidature { get; set; }
}
