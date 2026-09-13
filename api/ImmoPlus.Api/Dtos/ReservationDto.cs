namespace ImmoPlus.Api.Dtos;

public class ReservationDto
{
    public int AppartementId { get; set; }
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
}

public class ReservationResponseDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = string.Empty;
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = string.Empty;
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public decimal MontantTotal { get; set; }
    public decimal MontantPaye { get; set; }
    public string Statut { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; }
}
