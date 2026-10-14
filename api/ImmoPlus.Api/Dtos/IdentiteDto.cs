namespace ImmoPlus.Api.Dtos;

/// <summary>Demande de vérification d'identité (sans chemin de fichier : les fichiers passent par la route admin).</summary>
public class PieceIdentiteDto
{
    public int Id { get; set; }
    public int UtilisateurId { get; set; }
    /// <summary>Renseignés pour l'admin uniquement.</summary>
    public string? UtilisateurNom { get; set; }
    public string? UtilisateurEmail { get; set; }
    public string? UtilisateurRole { get; set; }
    /// <summary>« CNI », « Passeport », « PermisConduire » ou « CarteConsulaire ».</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>« EnAttente », « Validee » ou « Refusee ».</summary>
    public string Statut { get; set; } = string.Empty;
    public string? MotifRefus { get; set; }
    public DateTime DateEnvoi { get; set; }
    public DateTime? DateDecision { get; set; }
    public string? DecideParNom { get; set; }
    /// <summary>« image/jpeg » ou « application/pdf ».</summary>
    public string TypeRecto { get; set; } = string.Empty;
    public bool AVerso { get; set; }
    public string? TypeVerso { get; set; }
}

/// <summary>GET /utilisateur/moi/identite : état du badge et dernière demande (null s'il n'y en a jamais eu).</summary>
public class StatutIdentiteDto
{
    public bool IdentiteVerifiee { get; set; }
    public DateTime? DateVerificationIdentite { get; set; }
    public PieceIdentiteDto? Demande { get; set; }
}

public class RefuserIdentiteDto
{
    public string Motif { get; set; } = string.Empty;
}
