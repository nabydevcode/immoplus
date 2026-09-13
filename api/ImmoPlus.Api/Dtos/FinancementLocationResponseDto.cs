namespace ImmoPlus.Api.Dtos;

public class FinancementLocationResponseDto
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public decimal MontantTotal { get; set; }
    public int DureeMois { get; set; }
    public decimal ApportInitial { get; set; }
    public decimal MontantARembourser { get; set; }
    public decimal TauxFrais { get; set; }
    public int NombreMensualites { get; set; }
    public decimal MontantMensualite { get; set; }
    public string Statut { get; set; } = string.Empty;
}
