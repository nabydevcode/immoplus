namespace ImmoPlus.Api.Models.Enums;

public enum StatutCandidature
{
    EnAttente,
    Acceptee,
    Refusee,
    /// <summary>Le logement a été attribué à un autre candidat : dossier gardé en réserve si l'attribution tombe.</summary>
    ListeAttente,
    /// <summary>Retirée par le candidat, ou attribution annulée (désistement).</summary>
    Annulee,
    /// <summary>Retenue par le bailleur pour une visite (3 candidats maximum par logement).</summary>
    Preselectionnee
}
