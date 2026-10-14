namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Visite du logement proposée par le bailleur à un candidat présélectionné.</summary>
public class Visite
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    public DateTime DateHeure { get; set; }
    /// <summary>Message du bailleur (point de rendez-vous, contact sur place…).</summary>
    public string? Message { get; set; }
    public StatutVisite Statut { get; set; } = StatutVisite.Proposee;
    /// <summary>Message du candidat quand il répond (ex. disponibilités).</summary>
    public string? ReponseLocataire { get; set; }
    /// <summary>Notes du bailleur après la visite : jamais montrées au candidat.</summary>
    public string? NotesBailleur { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public Candidature? Candidature { get; set; }

    public bool EstActive => Statut is StatutVisite.Proposee or StatutVisite.Confirmee or StatutVisite.Indisponible;
}
