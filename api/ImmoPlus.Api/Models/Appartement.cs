namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Appartement
{
    public int Id { get; set; }
    public int BailleurId { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Disponible { get; set; }
    public TypeOffre TypeOffre { get; set; }

    // Champs longue durée (renseignés quand TypeOffre == LongueDuree)
    public decimal? LoyerMensuel { get; set; }
    public int? DureeAvanceExigee { get; set; }

    // Champs séjour court (renseignés quand TypeOffre == SejourCourt)
    public decimal? PrixParNuit { get; set; }
    public int? DureeMaxMois { get; set; }

    public Utilisateur? Bailleur { get; set; }
    public List<Location> Locations { get; set; } = new();
    public List<Candidature> Candidatures { get; set; } = new();
    public List<Reservation> Reservations { get; set; } = new();
}
