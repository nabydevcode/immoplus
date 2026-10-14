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

    /// <summary>AVenir, ATemps (réglée avant l'échéance), PayeeEnRetard, ou EnRetard (échéance dépassée, pas encore réglée).</summary>
    public string Ponctualite { get; set; } = string.Empty;
    /// <summary>Jours de retard : au règlement complet, ou à aujourd'hui si la mensualité est encore en retard.</summary>
    public int JoursRetard { get; set; }
    /// <summary>Date du paiement qui a soldé la mensualité.</summary>
    public DateTime? DateReglement { get; set; }
    /// <summary>Historique des paiements (y compris partiels) de cette mensualité.</summary>
    public List<PaiementResponseDto> Paiements { get; set; } = new();
}
