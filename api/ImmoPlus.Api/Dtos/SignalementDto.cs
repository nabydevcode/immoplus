namespace ImmoPlus.Api.Dtos;

public class CreerSignalementDto
{
    /// <summary>« Appartement », « Utilisateur », « Avis » ou « Message ».</summary>
    public string Cible { get; set; } = string.Empty;
    public int CibleId { get; set; }
    /// <summary>Nom du motif : Arnaque, AnnonceTrompeuse, ContenuInapproprie, Doublon, LogementIndisponible, ComportementAbusif, Autre.</summary>
    public string Motif { get; set; } = string.Empty;
    public string? Texte { get; set; }
    /// <summary>Obligatoire si l'auteur n'est pas connecté.</summary>
    public string? Email { get; set; }
}

public class TraiterSignalementDto
{
    /// <summary>« EnCours », « Traite » ou « Rejete ».</summary>
    public string Statut { get; set; } = string.Empty;
    public string? Decision { get; set; }
    /// <summary>« aucune » (défaut), « desactiverAnnonce », « desactiverUtilisateur » ou « masquerAvis ».</summary>
    public string? Action { get; set; }
}

public class SignalementDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int? AuteurId { get; set; }
    public string? AuteurNom { get; set; }
    public string? AuteurEmail { get; set; }
    public string Cible { get; set; } = string.Empty;
    public int CibleId { get; set; }
    /// <summary>Description courte de la cible (adresse de l'annonce, nom de l'utilisateur, extrait de l'avis ou du message).</summary>
    public string? CibleResume { get; set; }
    /// <summary>Liens utiles pour le front : annonce concernée (annonce, avis), réservation (message), location (avis longue durée).</summary>
    public int? AppartementId { get; set; }
    public int? ReservationId { get; set; }
    public int? LocationId { get; set; }
    /// <summary>Utilisateur visé (cible Utilisateur), auteur de l'avis / du message, ou bailleur de l'annonce.</summary>
    public int? UtilisateurCibleId { get; set; }
    public string Motif { get; set; } = string.Empty;
    public string Texte { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    public string? DecisionAdmin { get; set; }
    public string? ActionAppliquee { get; set; }
    public DateTime DateCreation { get; set; }
    public DateTime? DateTraitement { get; set; }
    public int? TraiteParId { get; set; }
    public string? TraiteParNom { get; set; }
}

/// <summary>Pastilles du menu admin.</summary>
public class CompteursAdminDto
{
    public int SignalementsNouveaux { get; set; }
    public int IdentitesEnAttente { get; set; }
}
