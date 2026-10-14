namespace ImmoPlus.Api.Dtos;

public class LoyerResponseDto
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public int Numero { get; set; }
    public DateTime DateEcheance { get; set; }
    public decimal MontantDu { get; set; }
    public decimal MontantPaye { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string Ponctualite { get; set; } = string.Empty;
    public int JoursRetard { get; set; }
    public DateTime? DateReglement { get; set; }
    public List<PaiementResponseDto> Paiements { get; set; } = new();
}

/// <summary>Loyers d'une location : début après la période couverte par l'avance, puis bilan de ponctualité.</summary>
public class LoyersLocationDto
{
    public int LocationId { get; set; }
    public decimal LoyerMensuel { get; set; }
    /// <summary>Date du premier loyer (fin de la période couverte par l'avance) ; null tant que les clés ne sont pas remises.</summary>
    public DateTime? DebutLoyers { get; set; }
    public List<LoyerResponseDto> Loyers { get; set; } = new();
    public int PayesATemps { get; set; }
    public int PayesEnRetard { get; set; }
    public int EnRetard { get; set; }
    public int JoursRetardCumules { get; set; }
    public decimal MontantEnRetard { get; set; }
}
