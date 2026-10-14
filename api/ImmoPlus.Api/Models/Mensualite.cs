namespace ImmoPlus.Api.Models;

using System.Text.Json.Serialization;
using ImmoPlus.Api.Models.Enums;

public class Mensualite
{
    public int Id { get; set; }
    public int FinancementLocationId { get; set; }
    public int NumeroEcheance { get; set; }
    public DateTime DateEcheance { get; set; }
    public decimal MontantDu { get; set; }
    public decimal MontantPaye { get; set; }
    public StatutMensualite Statut { get; set; } = StatutMensualite.AVenir;

    // Rappels automatiques (RappelsServices) : mémorisés pour ne jamais envoyer deux fois le même email.
    /// <summary>Envoi du rappel « échéance dans les 3 jours » (une seule fois par échéance).</summary>
    public DateTime? DateRappelEcheance { get; set; }
    /// <summary>Dernière alerte de retard envoyée (renouvelée tous les 7 jours tant que l'échéance n'est pas réglée).</summary>
    public DateTime? DateDernierRappelRetard { get; set; }

    [JsonIgnore]
    public FinancementLocation? FinancementLocation { get; set; }

    [JsonIgnore]
    public List<Paiement> Paiements { get; set; } = new();
}
