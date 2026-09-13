namespace ImmoPlus.Api.Dtos;

public class MensualiteResponseDto
{
    public int Id { get; set; }
    public int FinancementLocationId { get; set; }
    public int NumeroEcheance { get; set; }
    public DateTime DateEcheance { get; set; }
    public decimal MontantDu { get; set; }
    public decimal MontantPaye { get; set; }
    public string Statut { get; set; } = string.Empty;
}
