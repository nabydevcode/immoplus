namespace ImmoPlus.Api.Dtos;

public class PaiementDto
{
    // Exactement un des deux doit être renseigné.
    public int? MensualiteId { get; set; }
    public int? ReservationId { get; set; }

    public decimal Montant { get; set; }
    public DateTime DatePaiement { get; set; }
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
