namespace ImmoPlus.Api.Models;

using System.Text.Json.Serialization;
using ImmoPlus.Api.Models.Enums;

public class Mensualite
{
    public int Id { get; set; }
    public int FinancementLocationId { get; set; }
    public int NumeroEcheance { get; set; }
    public DateTime DateEcheance { get; set; }
    public decimal MontantDu { get; set; }
    public decimal MontantPaye { get; set; }
    public StatutMensualite Statut { get; set; } = StatutMensualite.AVenir;

    [JsonIgnore]
    public FinancementLocation? FinancementLocation { get; set; }

    [JsonIgnore]
    public List<Paiement> Paiements { get; set; } = new();
}
