namespace ImmoPlus.Api.Dtos;

public class SauvegardeDto
{
    /// <summary>« immoplus-yyyyMMdd-HHmm.dump » (pg_dump -Fc, heure UTC) ou « immoplus-avant-restauration-yyyyMMdd-HHmmss.dump ».</summary>
    public string Nom { get; set; } = string.Empty;
    /// <summary>Taille en octets.</summary>
    public long Taille { get; set; }
    /// <summary>Date (UTC) de la sauvegarde.</summary>
    public DateTime Date { get; set; }
}
