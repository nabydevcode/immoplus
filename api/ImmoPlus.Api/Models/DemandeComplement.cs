namespace ImmoPlus.Api.Models;

using ImmoPlus.Api.Models.Enums;

/// <summary>Demande de pièces manquantes envoyée par le bailleur au candidat, avec une date limite.</summary>
public class DemandeComplement
{
    public int Id { get; set; }
    public int CandidatureId { get; set; }
    /// <summary>Pièces demandées, sérialisées « Proprietaire:TypeDocument » séparées par « ; ».</summary>
    public string Pieces { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime DateLimite { get; set; }
    public DateTime DateDemande { get; set; } = DateTime.UtcNow;
    /// <summary>Le bailleur a déjà été prévenu que toutes les pièces sont arrivées.</summary>
    public bool CompletNotifie { get; set; }

    public Candidature? Candidature { get; set; }

    public IReadOnlyList<(ProprietaireDocument Proprietaire, TypeDocument Type)> ListePieces() =>
        Pieces.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(':'))
            .Select(p => (Enum.Parse<ProprietaireDocument>(p[0]), Enum.Parse<TypeDocument>(p[1])))
            .ToList();

    /// <summary>Une pièce est reçue si un document du même type, pour la même personne, a été déposé depuis la demande.</summary>
    public bool EstRecue((ProprietaireDocument Proprietaire, TypeDocument Type) piece, IEnumerable<Document> documents) =>
        documents.Any(d => d.Proprietaire == piece.Proprietaire && d.TypeDocument == piece.Type && d.DateUpload >= DateDemande);

    public bool EstComplete(IEnumerable<Document> documents) => ListePieces().All(p => EstRecue(p, documents));
}
