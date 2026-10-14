namespace ImmoPlus.Api.Models.Enums;

/// <summary>Qui a annulé une réservation : détermine le remboursement (l'hôte et l'admin remboursent toujours intégralement).</summary>
public enum AuteurAnnulation
{
    Voyageur,
    Hote,
    Admin,
    /// <summary>Annulation automatique (ex. paiement non reçu sous 24 h).</summary>
    Systeme
}
