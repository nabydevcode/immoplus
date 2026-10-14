namespace ImmoPlus.Api.Models;

/// <summary>Période bloquée par l'hôte sur un séjour court (DateFin exclue, comme le départ d'une réservation).</summary>
public class Indisponibilite
{
    public int Id { get; set; }
    public int AppartementId { get; set; }
    public DateTime DateDebut { get; set; }
    public DateTime DateFin { get; set; }
    public string? Motif { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public Appartement? Appartement { get; set; }
}
