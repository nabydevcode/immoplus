namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Demande de vérification d'identité : recto (obligatoire) et verso, contrôlés par un admin. Fichiers privés.</summary>
public class PieceIdentite
{
    public int Id { get; set; }
    public int UtilisateurId { get; set; }
    public TypePieceIdentite Type { get; set; }
    /// <summary>Recto, relatif à « identites/ » (clé de stockage) (« {userId}/{guid}.jpg|pdf ») : jamais servi publiquement.</summary>
    public string Chemin { get; set; } = string.Empty;
    public string? CheminVerso { get; set; }
    public StatutPieceIdentite Statut { get; set; } = StatutPieceIdentite.EnAttente;
    public string? MotifRefus { get; set; }
    public DateTime DateEnvoi { get; set; } = DateTime.UtcNow;
    public DateTime? DateDecision { get; set; }
    public int? DecideParId { get; set; }

    public Utilisateur? Utilisateur { get; set; }
    public Utilisateur? DecidePar { get; set; }
}
