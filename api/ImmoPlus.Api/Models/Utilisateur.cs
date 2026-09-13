namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Utilisateur
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MotDePasseHash { get; set; } = string.Empty;
    public RoleUtilisateur Role { get; set; }

    public List<Appartement> Appartements { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Candidature> Candidatures { get; set; } = new();
    public List<Reservation> Reservations { get; set; } = new();
}
