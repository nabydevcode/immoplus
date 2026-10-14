namespace ImmoPlus.Api.Dtos;

public class SauvegardeDto
{
    /// <summary>« immoplus-yyyyMMdd-HHmm.db » (heure UTC).</summary>
    public string Nom { get; set; } = string.Empty;
    /// <summary>Taille en octets.</summary>
    public long Taille { get; set; }
    /// <summary>Date (UTC) de la sauvegarde.</summary>
    public DateTime Date { get; set; }
}
