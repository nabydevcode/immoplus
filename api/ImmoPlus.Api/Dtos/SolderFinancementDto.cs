namespace ImmoPlus.Api.Dtos;

/// <summary>Remboursement anticipé : un seul versement qui solde toutes les mensualités restantes.</summary>
public class SolderFinancementDto
{
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime DatePaiement { get; set; }
}
