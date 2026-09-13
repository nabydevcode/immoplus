namespace ImmoPlus.Api.Dtos;

public class PaiementResponseDto
{
    public int Id { get; set; }
    public int? MensualiteId { get; set; }
    public int? ReservationId { get; set; }
    public decimal Montant { get; set; }
    public DateTime DatePaiement { get; set; }
    public string ModePaiement { get; set; } = string.Empty;
    public string? Reference { get; set; }
}
