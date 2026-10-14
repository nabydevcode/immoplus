namespace ImmoPlus.Api.Dtos;

public class PieceDemandeeDto
{
    public string Proprietaire { get; set; } = string.Empty;
    public string TypeDocument { get; set; } = string.Empty;
}

public class DemanderComplementDto
{
    public List<PieceDemandeeDto> Pieces { get; set; } = new();
    public string? Message { get; set; }
    public DateTime DateLimite { get; set; }
}

public class PieceDemandeeResponseDto
{
    public string Proprietaire { get; set; } = string.Empty;
    public string TypeDocument { get; set; } = string.Empty;
    public bool Recue { get; set; }
}

public class DemandeComplementResponseDto
{
    public int Id { get; set; }
    public List<PieceDemandeeResponseDto> Pieces { get; set; } = new();
    public string? Message { get; set; }
    public DateTime DateLimite { get; set; }
    public DateTime DateDemande { get; set; }
    public bool Complete { get; set; }
    /// <summary>Date limite passée sans que toutes les pièces soient arrivées.</summary>
    public bool Depassee { get; set; }
}
