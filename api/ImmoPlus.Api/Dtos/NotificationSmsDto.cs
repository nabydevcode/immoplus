namespace ImmoPlus.Api.Dtos;

public class NotificationSmsDto
{
    public int Id { get; set; }
    public int UtilisateurId { get; set; }
    /// <summary>Historique admin uniquement (null dans « mes SMS »).</summary>
    public string? UtilisateurNom { get; set; }
    public string Telephone { get; set; } = string.Empty;
    /// <summary>« Sms » ou « WhatsApp ».</summary>
    public string Canal { get; set; } = string.Empty;
    public string Texte { get; set; } = string.Empty;
    /// <summary>« Envoye », « Echec » ou « Simule ».</summary>
    public string Statut { get; set; } = string.Empty;
    public string? ReferenceFournisseur { get; set; }
    public string? Erreur { get; set; }
    public DateTime DateEnvoi { get; set; }
    public string Evenement { get; set; } = string.Empty;
}
