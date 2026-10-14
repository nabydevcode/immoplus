namespace ImmoPlus.Api.Models.Enums;

public enum StatutTransactionMobile
{
    EnAttente,
    Reussie,
    Echouee,
    /// <summary>Restée sans validation sur le téléphone pendant 10 minutes.</summary>
    Expiree
}
