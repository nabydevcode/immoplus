namespace ImmoPlus.Api.Dtos;

public class DonnerPreavisDto
{
    /// <summary>Pour l'admin : « Locataire » ou « Bailleur » (le locataire donne toujours son propre préavis).</summary>
    public string? DonnePar { get; set; }
    public string? Motif { get; set; }
}
