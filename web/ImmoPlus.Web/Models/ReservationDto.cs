namespace ImmoPlus.Web.Models;

public class ReservationModele
{
    public int AppartementId { get; set; }
    public DateTime DateArrivee { get; set; } = DateTime.Today.AddDays(1);
    public DateTime DateDepart { get; set; } = DateTime.Today.AddDays(2);
}

public class ReservationDto
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public string LocataireNom { get; set; } = "";
    public int AppartementId { get; set; }
    public string AppartementAdresse { get; set; } = "";
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public decimal MontantTotal { get; set; }
    public decimal MontantPaye { get; set; }
    public string Statut { get; set; } = "";
    public DateTime DateCreation { get; set; }
}

public class PaiementModele
{
    public int? MensualiteId { get; set; }
    public int? ReservationId { get; set; }
    public decimal Montant { get; set; }
    public DateTime DatePaiement { get; set; } = DateTime.Today;
    public string ModePaiement { get; set; } = "";
    public string? Reference { get; set; }
}
