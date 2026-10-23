namespace ImmoPlus.Api.Dtos;

/// <summary>Période occupée d'un séjour court (Fin exclue). Id et Motif ne sont remplis que pour l'hôte et l'admin.</summary>
public class PeriodeCalendrierDto
{
    public DateOnly Debut { get; set; }
    public DateOnly Fin { get; set; }
    /// <summary>« Reservee » ou « Bloquee ».</summary>
    public string Type { get; set; } = string.Empty;
    public int? Id { get; set; }
    public string? Motif { get; set; }
}

public class IndisponibiliteDto
{
    public DateTime DateDebut { get; set; }
    public DateTime DateFin { get; set; }
    public string? Motif { get; set; }
}

/// <summary>Villes et quartiers des annonces disponibles, pour les listes déroulantes de la recherche.</summary>
public class VilleQuartiersDto
{
    public string Ville { get; set; } = string.Empty;
    public List<string> Quartiers { get; set; } = new();
}

/// <summary>Critères de recherche des annonces (tous optionnels).</summary>
public class RechercheAppartementDto
{
    public string? Ville { get; set; }
    public string? Quartier { get; set; }
    public int? Voyageurs { get; set; }
    public DateTime? Arrivee { get; set; }
    public DateTime? Depart { get; set; }
    public List<string> Equipements { get; set; } = new();
    public decimal? PrixMax { get; set; }
    /// <summary>Bornes de la carte (les 4 ensemble) : seuls les logements géolocalisés dans la zone.</summary>
    public double? Nord { get; set; }
    public double? Sud { get; set; }
    public double? Est { get; set; }
    public double? Ouest { get; set; }
    /// <summary>Logements d'un seul hôte (ex. « Mes logements » : évite de charger toutes les annonces).</summary>
    public int? BailleurId { get; set; }
    // Longue durée (les annonces sans loyer / sans avance renseignés sont exclues quand le filtre est fourni).
    /// <summary>Loyer mensuel maximum (GNF).</summary>
    public decimal? LoyerMax { get; set; }
    /// <summary>Nombre de chambres minimum (annonces sans nombre de chambres exclues).</summary>
    public int? ChambresMin { get; set; }
    /// <summary>Durée d'avance exigée maximum (mois).</summary>
    public int? AvanceMax { get; set; }
    /// <summary>Annonces supprimées (true) ou publiées (false) ; sans effet pour le public, qui ne voit jamais les supprimées.</summary>
    public bool? Supprimes { get; set; }
    /// <summary>Recherche libre : n° d'annonce, adresse, ville, quartier ou nom du bailleur (sans accents ni casse).</summary>
    public string? Q { get; set; }
}
