namespace ImmoPlus.Api.Dtos;

public class InitierPaiementMobileDto
{
    public int ReservationId { get; set; }
    /// <summary>« OrangeMoney » ou « MtnMomo ».</summary>
    public string Operateur { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    /// <summary>Reste dû par défaut.</summary>
    public decimal? Montant { get; set; }
}

/// <summary>Corps du webhook envoyé par le fournisseur (signé : en-tête X-Signature).</summary>
public class WebhookPaiementMobileDto
{
    public string ReferenceFournisseur { get; set; } = string.Empty;
    /// <summary>« SUCCESS » ou « FAILED ».</summary>
    public string Statut { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public class SimulerPaiementMobileDto
{
    public bool Succes { get; set; }
}

public class TransactionMobileDto
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public string Operateur { get; set; } = string.Empty;
    /// <summary>Masqué : « 6•• •• •• 12 ».</summary>
    public string Telephone { get; set; } = string.Empty;
    public decimal Montant { get; set; }
    /// <summary>« EnAttente », « Reussie », « Echouee » ou « Expiree ».</summary>
    public string Statut { get; set; } = string.Empty;
    public string? ReferenceFournisseur { get; set; }
    public string? MessageErreur { get; set; }
    public DateTime DateCreation { get; set; }
    public string Instructions { get; set; } = string.Empty;
    /// <summary>Fournisseur simulé (développement) : le front affiche les boutons de simulation.</summary>
    public bool Simulation { get; set; }
}
