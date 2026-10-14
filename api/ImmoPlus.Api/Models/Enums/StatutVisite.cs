namespace ImmoPlus.Api.Models.Enums;

public enum StatutVisite
{
    /// <summary>Proposée par le bailleur, en attente de la réponse du candidat.</summary>
    Proposee,
    Confirmee,
    /// <summary>Le candidat a signalé qu'il n'est pas disponible : à reprogrammer.</summary>
    Indisponible,
    Effectuee,
    /// <summary>Le candidat ne s'est pas présenté.</summary>
    Absent,
    Annulee
}
