namespace ImmoPlus.Api.Dtos;

/// <summary>Bilan d'une exécution des rappels automatiques : nombre d'emails envoyés par catégorie.</summary>
public class RappelsResultatDto
{
    public int RappelsMensualites { get; set; }
    public int RappelsLoyers { get; set; }
    public int RetardsMensualites { get; set; }
    public int RetardsLoyers { get; set; }
    public int RecapitulatifsAdmin { get; set; }
    /// <summary>Échéances en retard au moment de l'exécution (envoyées ou non cette fois-ci).</summary>
    public int EcheancesEnRetard { get; set; }
    /// <summary>Demandes de réservation refusées automatiquement faute de réponse de l'hôte sous 48 h.</summary>
    public int DemandesReservationExpirees { get; set; }
    /// <summary>Réservations annulées automatiquement faute de paiement sous 24 h.</summary>
    public int ReservationsImpayeesAnnulees { get; set; }
    /// <summary>Séjours confirmés dont la date de départ est passée, passés en « Terminee ».</summary>
    public int SejoursTermines { get; set; }
    /// <summary>Emails « Votre arrivée demain » envoyés aux voyageurs (la copie à l'hôte n'est pas comptée).</summary>
    public int InstructionsArrivee { get; set; }
    /// <summary>Entrées du journal des actions de plus de 2 ans supprimées (hors total : ce ne sont pas des emails).</summary>
    public int JournalPurge { get; set; }
    /// <summary>Messages de la boîte d'envoi traités depuis plus de 90 jours, supprimés (hors total).</summary>
    public int MessagesPurges { get; set; }
    public int Total => RappelsMensualites + RappelsLoyers + RetardsMensualites + RetardsLoyers + RecapitulatifsAdmin + DemandesReservationExpirees + ReservationsImpayeesAnnulees
        + InstructionsArrivee;
}
