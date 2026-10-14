namespace ImmoPlus.Api.Models.Enums;

public enum SensAvis
{
    /// <summary>Le voyageur évalue le logement (et donc son hôte).</summary>
    VoyageurVersLogement,
    /// <summary>L'hôte évalue le voyageur.</summary>
    HoteVersVoyageur,
    /// <summary>Longue durée : le locataire évalue le logement (et donc son bailleur).</summary>
    LocataireVersLogement,
    /// <summary>Longue durée : le bailleur évalue son locataire.</summary>
    BailleurVersLocataire
}
