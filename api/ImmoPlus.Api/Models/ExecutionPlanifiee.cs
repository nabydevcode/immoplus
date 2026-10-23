namespace ImmoPlus.Api.Models;

/// <summary>
/// Dernière exécution d'un traitement planifié (rappels…), partagée par toutes les instances de l'API :
/// celle qui obtient le verrou consulte cette date et ne relance pas un traitement déjà fait dans l'intervalle.
/// </summary>
public class ExecutionPlanifiee
{
    /// <summary>Nom du traitement, ex. « rappels ».</summary>
    public string Nom { get; set; } = string.Empty;
    public DateTime DerniereExecution { get; set; }
    /// <summary>Machine (conteneur) qui l'a exécuté, pour le diagnostic.</summary>
    public string Instance { get; set; } = string.Empty;
}
