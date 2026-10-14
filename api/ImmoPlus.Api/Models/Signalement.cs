namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Signalement d'une annonce, d'un utilisateur, d'un avis ou d'un message, traité par un admin.</summary>
public class Signalement
{
    public int Id { get; set; }
    /// <summary>null si le signalement vient d'un visiteur non connecté (AuteurEmail est alors renseigné).</summary>
    public int? AuteurId { get; set; }
    public string? AuteurEmail { get; set; }
    /// <summary>« Appartement », « Utilisateur », « Avis » ou « Message ».</summary>
    public string Cible { get; set; } = string.Empty;
    public int CibleId { get; set; }
    public MotifSignalement Motif { get; set; }
    public string Texte { get; set; } = string.Empty;
    public StatutSignalement Statut { get; set; } = StatutSignalement.Nouveau;
    public string? DecisionAdmin { get; set; }
    /// <summary>Action appliquée lors du traitement (« desactiverAnnonce », « desactiverUtilisateur », « masquerAvis »).</summary>
    public string? ActionAppliquee { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime? DateTraitement { get; set; }
    public int? TraiteParId { get; set; }
    /// <summary>Adresse du client : limite les signalements anonymes (5 par heure).</summary>
    public string? AdresseIp { get; set; }

    public Utilisateur? Auteur { get; set; }
    public Utilisateur? TraitePar { get; set; }
}
