namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Loyer mensuel (sans frais) dû après la période couverte par l'avance.
/// Les loyers sont créés au fil des mois tant que la location est en cours.
/// </summary>
public class Loyer
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public int Numero { get; set; }
    /// <summary>Mois couvert par ce loyer (il est dû le premier jour de cette période).</summary>
    public DateTime DateEcheance { get; set; }
    public decimal MontantDu { get; set; }
    public decimal MontantPaye { get; set; }
    public StatutMensualite Statut { get; set; } = StatutMensualite.AVenir;

    // Rappels automatiques (RappelsServices) : mémorisés pour ne jamais envoyer deux fois le même email.
    /// <summary>Envoi du rappel « échéance dans les 3 jours » (une seule fois par échéance).</summary>
    public DateTime? DateRappelEcheance { get; set; }
    /// <summary>Dernière alerte de retard envoyée (renouvelée tous les 7 jours tant que l'échéance n'est pas réglée).</summary>
    public DateTime? DateDernierRappelRetard { get; set; }

    public Location? Location { get; set; }
    public List<Paiement> Paiements { get; set; } = new();
}
