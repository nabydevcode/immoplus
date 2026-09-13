namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class FinancementLocation
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
    public StatutFinancement Statut { get; set; } = StatutFinancement.EnAttenteValidation;

    public Location? Location { get; set; }
    public List<Mensualite> Mensualites { get; set; } = new();
}
