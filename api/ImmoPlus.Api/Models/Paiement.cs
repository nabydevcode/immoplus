namespace ImmoPlus.Api.Models;

using System.Text.Json.Serialization;

public class Paiement
{
    public int Id { get; set; }

    // Exactement un des deux doit être renseigné (validé en service) :
    // règle un échéancier de financement, ou une réservation de séjour court.
    public int? MensualiteId { get; set; }
    public int? ReservationId { get; set; }

    public decimal Montant { get; set; }
    public DateTime DatePaiement { get; set; }
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }

    [JsonIgnore]
    public Mensualite? Mensualite { get; set; }

    [JsonIgnore]
    public Reservation? Reservation { get; set; }
}
