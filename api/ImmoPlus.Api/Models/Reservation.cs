namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

public class Reservation
{
    public int Id { get; set; }
    public int LocataireId { get; set; }
    public int AppartementId { get; set; }
    public DateTime DateArrivee { get; set; }
    public DateTime DateDepart { get; set; }
    public int NombreVoyageurs { get; set; } = 1;
    // Détail du prix figé à la création : une modification ultérieure de l'annonce ne change pas le séjour réservé.
    public int NombreNuits { get; set; }
    public decimal PrixParNuit { get; set; }
    public decimal ReductionMontant { get; set; }
    public decimal FraisMenage { get; set; }
    public decimal Caution { get; set; }
    public decimal MontantTotal { get; set; }
    public string? MotifRefus { get; set; }
    public DateTime? DateReponseHote { get; set; }
    public StatutReservation Statut { get; set; } = StatutReservation.EnAttente;
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    /// <summary>Politique de l'annonce figée à la création : le voyageur a réservé sur ces conditions.</summary>
    public PolitiqueAnnulation PolitiqueAnnulation { get; set; } = PolitiqueAnnulation.Moderee;

    // Annulation et remboursement (hors caution)
    public AuteurAnnulation? AnnuleePar { get; set; }
    public string? MotifAnnulation { get; set; }
    public DateTime? DateAnnulation { get; set; }
    public decimal? MontantARembourser { get; set; }
    /// <summary>Cumul des remboursements enregistrés par l'admin.</summary>
    public decimal? MontantRembourse { get; set; }
    public DateTime? DateRemboursement { get; set; }
    public string? ModeRemboursement { get; set; }
    public string? ReferenceRemboursement { get; set; }

    // Anti-spam de la messagerie : au plus un email « Nouveau message » par heure et par destinataire.
    public DateTime? DateDernierEmailMessageVoyageur { get; set; }
    public DateTime? DateDernierEmailMessageHote { get; set; }

    /// <summary>Email « Votre arrivée demain » envoyé la veille (une seule fois).</summary>
    public DateTime? DateEnvoiInstructions { get; set; }

    public Utilisateur? Locataire { get; set; }
    public Appartement? Appartement { get; set; }
    public List<Paiement> Paiements { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<Avis> Avis { get; set; } = new();
    public List<PhotoEtatDesLieux> PhotosEtatDesLieux { get; set; } = new();
    public List<TransactionMobile> TransactionsMobiles { get; set; } = new();
}
