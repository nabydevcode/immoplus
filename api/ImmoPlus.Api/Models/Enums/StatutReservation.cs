namespace ImmoPlus.Api.Models.Enums;

public enum StatutReservation
{
    EnAttente,
    Confirmee,
    Annulee,
    Terminee,
    /// <summary>Demande en attente de la réponse de l'hôte (annonce sans réservation instantanée).</summary>
    DemandeEnvoyee,
    Refusee
}
