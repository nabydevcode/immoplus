namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Demande de paiement Orange Money / MTN MoMo d'une réservation. Le résultat arrive par le webhook signé du fournisseur
/// (ou par la simulation locale) ; un succès crée le Paiement correspondant.
/// </summary>
public class TransactionMobile
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public int UtilisateurId { get; set; }
    public OperateurMobile Operateur { get; set; }
    /// <summary>Numéro guinéen normalisé sur 9 chiffres (6XXXXXXXX).</summary>
    public string Telephone { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    public StatutTransactionMobile Statut { get; set; } = StatutTransactionMobile.EnAttente;
    /// <summary>Référence attribuée par le fournisseur à l'initiation (null tant qu'il n'a pas répondu).</summary>
    public string? ReferenceFournisseur { get; set; }
    public string? MessageErreur { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime DateMaj { get; set; } = DateTime.UtcNow;
    public int? PaiementId { get; set; }

    public Reservation? Reservation { get; set; }
    public Utilisateur? Utilisateur { get; set; }
    public Paiement? Paiement { get; set; }
}
