namespace ImmoPlus.Api.Dtos;

public class UtilisateurResponseDto
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Statut { get; set; } = string.Empty;
    public string? MotifDesactivation { get; set; }
    public DateTime? DateDesactivation { get; set; }
    public string? Token { get; set; }
    /// <summary>Préférence « recevoir les notifications par SMS / WhatsApp ».</summary>
    public bool NotificationsSms { get; set; }
    /// <summary>Pièce d'identité contrôlée par un admin (badge « Identité vérifiée »).</summary>
    public bool IdentiteVerifiee { get; set; }

    /// <summary>Désactivation d'un hôte uniquement (null sinon) : réservations non confirmées annulées.</summary>
    public int? ReservationsAnnulees { get; set; }
    /// <summary>Désactivation d'un hôte uniquement (null sinon) : séjours confirmés à venir ou en cours, laissés à traiter par l'admin.</summary>
    public int? ReservationsConfirmeesAVenir { get; set; }
}
