namespace ImmoPlus.Api.Dtos;

public class CreerAvisDto
{
    public int Note { get; set; }
    public int? Proprete { get; set; }
    public int? Communication { get; set; }
    public int? Emplacement { get; set; }
    public int? RapportQualitePrix { get; set; }
    public string Commentaire { get; set; } = string.Empty;
}

public class RepondreAvisDto
{
    public string Reponse { get; set; } = string.Empty;
}

public class AvisDto
{
    public int Id { get; set; }
    /// <summary>Avis sur un séjour court (null pour un avis de location longue durée).</summary>
    public int? ReservationId { get; set; }
    /// <summary>Avis sur une location longue durée (null pour un séjour court).</summary>
    public int? LocationId { get; set; }
    /// <summary>Prénom seul (premier mot du nom) : l'avis est public.</summary>
    public string AuteurNom { get; set; } = string.Empty;
    public int Note { get; set; }
    public int? Proprete { get; set; }
    public int? Communication { get; set; }
    public int? Emplacement { get; set; }
    public int? RapportQualitePrix { get; set; }
    public string Commentaire { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; }
    public string? ReponseHote { get; set; }
    public DateTime? DateReponse { get; set; }
    /// <summary>« VoyageurVersLogement », « HoteVersVoyageur », « LocataireVersLogement » ou « BailleurVersLocataire ».</summary>
    public string Sens { get; set; } = string.Empty;
}

public class MoyennesAvisDto
{
    public decimal? Proprete { get; set; }
    public decimal? Communication { get; set; }
    public decimal? Emplacement { get; set; }
    public decimal? RapportQualitePrix { get; set; }
}

public class ListeAvisDto
{
    public decimal? Moyenne { get; set; }
    public int Nombre { get; set; }
    /// <summary>null s'il n'y a aucun avis (et toujours null pour les avis reçus par un voyageur).</summary>
    public MoyennesAvisDto? Moyennes { get; set; }
    /// <summary>Avis de la page demandée (GET appartement/{id}/avis) ; tous les avis pour les avis reçus par un utilisateur.</summary>
    public List<AvisDto> Avis { get; set; } = new();
    /// <summary>Pagination de GET appartement/{id}/avis (Nombre = total des avis) ; null pour les avis d'un utilisateur (non paginés).</summary>
    public int? PageActuelle { get; set; }
    public int? TaillePage { get; set; }
    public int? TotalPages { get; set; }
}

/// <summary>Badges de confiance d'un hôte, calculés à la volée (rien n'est stocké).</summary>
public class HoteDto
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public bool IdentiteVerifiee { get; set; }
    public bool Superhote { get; set; }
    public decimal? TauxReponse { get; set; }
    public int? DelaiReponseHeures { get; set; }
    /// <summary>« yyyy-MM ».</summary>
    public string? MembreDepuis { get; set; }
    public int NombreAvis { get; set; }
    public decimal? NoteMoyenne { get; set; }
}

public class ProfilPublicDto
{
    public int Id { get; set; }
    public string Prenom { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IdentiteVerifiee { get; set; }
    public bool Superhote { get; set; }
    public string? MembreDepuis { get; set; }
    public decimal? NoteMoyenne { get; set; }
    public int NombreAvis { get; set; }
    public decimal? TauxReponse { get; set; }
    public int? DelaiReponseHeures { get; set; }
    /// <summary>Ids des annonces disponibles de cet utilisateur.</summary>
    public List<int> Logements { get; set; } = new();
}
