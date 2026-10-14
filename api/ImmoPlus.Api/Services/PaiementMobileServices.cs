namespace ImmoPlus.Api.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>Fournisseur de paiement mobile (Orange Money, MTN MoMo) : demande le paiement sur le téléphone du client.</summary>
public interface IFournisseurPaiementMobile
{
    /// <summary>Lance la demande auprès de l'opérateur ; renvoie la référence du fournisseur, rappelée par son webhook.</summary>
    Task<string> InitierAsync(TransactionMobile t);
}

/// <summary>Fournisseur simulé (développement, pas de contrat marchand) : ne contacte rien, le résultat vient de /simuler.</summary>
public class FournisseurSimule : IFournisseurPaiementMobile
{
    public Task<string> InitierAsync(TransactionMobile t) =>
        Task.FromResult($"SIM-{RandomNumberGenerator.GetHexString(8, lowercase: false)}");
}

/// <summary>
/// Paiement mobile d'une réservation : initiation, suivi (expiration au bout de 10 min sans validation),
/// résultat reçu par le webhook signé du fournisseur. Un succès crée le Paiement via PaiementServices
/// (confirmation de la réservation et emails comme pour un paiement enregistré à la main).
/// </summary>
public partial class PaiementMobileServices
{
    public const string ModeSimulation = "Simulation";
    public const string ModeDesactive = "Desactive";
    public static readonly TimeSpan DureeValidite = TimeSpan.FromMinutes(10);
    private const string Instructions =
        "Validez le paiement sur votre téléphone : composez #144# (Orange) / *440# (MTN) puis saisissez votre code secret.";

    private readonly AppDbContext _context;
    private readonly IFournisseurPaiementMobile _fournisseur;
    private readonly PaiementServices _paiementServices;
    private readonly NotificationServices _notificationServices;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaiementMobileServices> _logger;
    private readonly ReservationServices _reservationServices;
    private readonly JournalServices _journal;

    public PaiementMobileServices(AppDbContext context, IFournisseurPaiementMobile fournisseur, PaiementServices paiementServices,
        NotificationServices notificationServices, IConfiguration configuration, ILogger<PaiementMobileServices> logger,
        ReservationServices reservationServices, JournalServices journal)
    {
        _journal = journal;
        _reservationServices = reservationServices;
        _context = context;
        _fournisseur = fournisseur;
        _paiementServices = paiementServices;
        _notificationServices = notificationServices;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>« Simulation » ou « Desactive » (par défaut, faute de configuration).</summary>
    public string Mode => _configuration["PaiementMobile:Mode"] is { Length: > 0 } mode ? mode : ModeDesactive;
    public bool EstSimulation => string.Equals(Mode, ModeSimulation, StringComparison.OrdinalIgnoreCase);

    private void VerifierActif()
    {
        if (string.Equals(Mode, ModeDesactive, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Paiement mobile indisponible");
        }
    }

    public async Task<TransactionMobileDto> InitierAsync(int utilisateurId, InitierPaiementMobileDto dto)
    {
        VerifierActif();
        // Réservation restée impayée plus de 24 h : annulée avant toute tentative (elle n'est alors plus payable).
        await _reservationServices.ExpirerImpayeesAsync(r => r.Id == dto.ReservationId);
        var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == dto.ReservationId)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (reservation.LocataireId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez payer que vos propres réservations");
        }
        var dejaPaye = await _context.Paiements.Where(p => p.ReservationId == reservation.Id).SumAsync(p => (decimal?)p.Montant) ?? 0;
        var resteDu = Math.Max(0, reservation.MontantTotal - dejaPaye);
        var payable = reservation.Statut == StatutReservation.EnAttente
            || (reservation.Statut == StatutReservation.Confirmee && resteDu > 0);
        if (!payable || resteDu <= 0)
        {
            throw new ConflictException(reservation.Statut == StatutReservation.DemandeEnvoyee
                ? "L'hôte n'a pas encore accepté cette demande : le paiement n'est pas encore possible"
                : "Aucun paiement n'est attendu pour cette réservation");
        }
        // Payer pour confirmer un séjour dont l'arrivée est passée n'a plus de sens (le solde d'un séjour confirmé reste payable).
        if (reservation.Statut != StatutReservation.Confirmee && reservation.DateArrivee.Date < DateTime.UtcNow.Date)
        {
            throw new ConflictException("La date d'arrivée de cette réservation est passée : elle ne peut plus être payée");
        }
        if (!Enum.TryParse<OperateurMobile>(dto.Operateur?.Trim(), true, out var operateur) || !Enum.IsDefined(operateur)
            || int.TryParse(dto.Operateur, out _))
        {
            throw new InvalidOperationException("Opérateur invalide. Utilisez OrangeMoney ou MtnMomo");
        }
        var telephone = NormaliserTelephone(dto.Telephone);
        var montant = dto.Montant ?? resteDu;
        if (montant <= 0 || montant > resteDu)
        {
            throw new InvalidOperationException($"Le montant doit être compris entre 1 et le reste dû ({Montant(resteDu)})");
        }

        // Une transaction en attente bloque les autres tant qu'elle n'a pas expiré (l'index unique filtré le garantit aussi).
        var enCours = await _context.TransactionsMobiles
            .Where(t => t.ReservationId == reservation.Id && t.Statut == StatutTransactionMobile.EnAttente)
            .ToListAsync();
        foreach (var t in enCours)
        {
            if (!ExpirerSiBesoin(t))
            {
                throw new ConflictException("Un paiement mobile est déjà en attente de validation pour cette réservation");
            }
        }

        var maintenant = DateTime.UtcNow;
        var transaction = new TransactionMobile
        {
            ReservationId = reservation.Id,
            UtilisateurId = utilisateurId,
            Operateur = operateur,
            Telephone = telephone,
            Montant = montant,
            Statut = StatutTransactionMobile.EnAttente,
            DateCreation = maintenant,
            DateMaj = maintenant
        };
        await _context.TransactionsMobiles.AddAsync(transaction);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Un paiement mobile est déjà en attente de validation pour cette réservation");
        }

        try
        {
            transaction.ReferenceFournisseur = await _fournisseur.InitierAsync(transaction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'initiation du paiement mobile {Id} auprès du fournisseur", transaction.Id);
            transaction.Statut = StatutTransactionMobile.Echouee;
            transaction.MessageErreur = "L'opérateur n'a pas pu lancer le paiement. Réessayez dans quelques instants.";
        }
        transaction.DateMaj = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return VersDto(transaction);
    }

    /// <summary>Propriétaire de la transaction ou admin. Une transaction en attente depuis plus de 10 min passe en « Expiree ».</summary>
    public async Task<TransactionMobileDto> ObtenirAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var transaction = await _context.TransactionsMobiles.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Cette transaction n'existe pas");
        if (role != RoleUtilisateur.Admin && transaction.UtilisateurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette transaction");
        }
        if (ExpirerSiBesoin(transaction))
        {
            await _context.SaveChangesAsync();
        }
        return VersDto(transaction);
    }

    /// <summary>
    /// Transaction encore en attente de validation pour cette réservation et cet utilisateur (null sinon) :
    /// permet de reprendre le suivi depuis un autre appareil ou après un rechargement.
    /// </summary>
    public async Task<TransactionMobileDto?> EnCoursAsync(int reservationId, int utilisateurId)
    {
        var transaction = await _context.TransactionsMobiles
            .Where(t => t.ReservationId == reservationId && t.UtilisateurId == utilisateurId && t.Statut == StatutTransactionMobile.EnAttente)
            .OrderByDescending(t => t.DateCreation)
            .FirstOrDefaultAsync();
        if (transaction is null)
        {
            return null;
        }
        if (ExpirerSiBesoin(transaction))
        {
            await _context.SaveChangesAsync();
            return null;
        }
        return VersDto(transaction);
    }

    /// <summary>
    /// Webhook du fournisseur : la signature HMAC-SHA256 (hex) du corps brut est vérifiée en temps constant avant toute lecture.
    /// </summary>
    /// <returns>Statut de la transaction après traitement.</returns>
    public async Task<string> TraiterWebhookAsync(string operateurRoute, byte[] corps, string? signature)
    {
        VerifierActif();
        if (!SignatureValide(corps, signature))
        {
            throw new SignatureInvalideException();
        }
        if (!Enum.TryParse<OperateurMobile>(operateurRoute, true, out var operateur) || !Enum.IsDefined(operateur)
            || int.TryParse(operateurRoute, out _))
        {
            throw new NotFoundException("Opérateur inconnu");
        }

        WebhookPaiementMobileDto? notification;
        try
        {
            notification = JsonSerializer.Deserialize<WebhookPaiementMobileDto>(corps, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Corps de la notification invalide");
        }
        if (notification is null || string.IsNullOrWhiteSpace(notification.ReferenceFournisseur))
        {
            throw new InvalidOperationException("referenceFournisseur est obligatoire");
        }
        bool succes = notification.Statut?.Trim().ToUpperInvariant() switch
        {
            "SUCCESS" => true,
            "FAILED" => false,
            _ => throw new InvalidOperationException("statut doit valoir SUCCESS ou FAILED")
        };

        var transaction = await _context.TransactionsMobiles.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ReferenceFournisseur == notification.ReferenceFournisseur.Trim() && t.Operateur == operateur)
            ?? throw new NotFoundException("Transaction introuvable");
        return (await AppliquerResultatAsync(transaction.Id, succes, notification.Message)).ToString();
    }

    /// <summary>Mode Simulation uniquement : le propriétaire valide ou refuse « sur son téléphone » (même traitement que le webhook).</summary>
    public async Task<TransactionMobileDto> SimulerAsync(int id, bool succes, int utilisateurId)
    {
        if (!EstSimulation)
        {
            throw new NotFoundException("Simulation indisponible");
        }
        var transaction = await _context.TransactionsMobiles.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Cette transaction n'existe pas");
        if (transaction.UtilisateurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette transaction");
        }
        // Comme un vrai opérateur : une demande expirée ne peut plus être validée sur le téléphone.
        if (ExpirerSiBesoin(transaction))
        {
            await _context.SaveChangesAsync();
        }
        if (transaction.Statut != StatutTransactionMobile.EnAttente)
        {
            throw new ConflictException("Cette transaction n'est plus en attente de validation");
        }
        await AppliquerResultatAsync(id, succes, succes ? null : "Paiement refusé (simulation)");
        // La transaction suivie par le contexte est périmée (mises à jour directes en base) : relecture propre.
        _context.ChangeTracker.Clear();
        return await ObtenirAsync(id, utilisateurId, RoleUtilisateur.Locataire);
    }

    /// <summary>
    /// Traitement unique du résultat (webhook ou simulation), idempotent : seule la première notification change la transaction.
    /// Un succès arrivé après l'expiration est tout de même enregistré (l'argent a été débité) ; un échec ne change plus une transaction expirée.
    /// </summary>
    private async Task<StatutTransactionMobile> AppliquerResultatAsync(int transactionId, bool succes, string? message)
    {
        var maintenant = DateTime.UtcNow;
        var nouveauStatut = succes ? StatutTransactionMobile.Reussie : StatutTransactionMobile.Echouee;
        var messageErreur = succes ? null : Tronquer(string.IsNullOrWhiteSpace(message) ? "Paiement refusé par l'opérateur" : message.Trim(), 300);

        // Mise à jour conditionnelle : deux notifications simultanées ne peuvent pas créer deux paiements.
        var modifiees = await _context.TransactionsMobiles
            .Where(t => t.Id == transactionId && (t.Statut == StatutTransactionMobile.EnAttente
                || (succes && t.Statut == StatutTransactionMobile.Expiree)))
            .ExecuteUpdateAsync(maj => maj
                .SetProperty(t => t.Statut, nouveauStatut)
                .SetProperty(t => t.MessageErreur, messageErreur)
                .SetProperty(t => t.DateMaj, maintenant));

        var transaction = await _context.TransactionsMobiles.AsNoTracking()
            .Include(t => t.Reservation).ThenInclude(r => r!.Appartement)
            .Include(t => t.Utilisateur)
            .FirstAsync(t => t.Id == transactionId);
        if (modifiees == 0)
        {
            return transaction.Statut;
        }

        var reservation = transaction.Reservation!;
        if (!succes)
        {
            await _notificationServices.PaiementMobileEchoueAsync(transaction.Utilisateur!, reservation, reservation.Appartement!,
                LibelleOperateur(transaction.Operateur), transaction.Montant, messageErreur!);
            return nouveauStatut;
        }

        try
        {
            // L'argent est débité : le paiement est toujours tracé, même si la réservation a été annulée entre-temps
            // (il s'ajoute alors au montant à rembourser, suivi par l'admin).
            var (paiement, apresAnnulation) = await _paiementServices.EnregistrerPaiementMobileAsync(new PaiementDto
            {
                ReservationId = transaction.ReservationId,
                Montant = transaction.Montant,
                DatePaiement = maintenant,
                ModePaiement = LibelleOperateur(transaction.Operateur),
                Reference = transaction.ReferenceFournisseur
            }, transaction.UtilisateurId);
            var avertissement = apresAnnulation
                ? "Paiement reçu après l'annulation de la réservation : il vous sera intégralement remboursé par ImmoPlus."
                : null;
            if (apresAnnulation)
            {
                _logger.LogWarning("Paiement mobile {Id} ({Reference}) reçu après l'annulation de la réservation {Reservation} : à rembourser",
                    transactionId, transaction.ReferenceFournisseur, transaction.ReservationId);
            }
            await _context.TransactionsMobiles.Where(t => t.Id == transactionId)
                .ExecuteUpdateAsync(maj => maj.SetProperty(t => t.PaiementId, paiement.Id).SetProperty(t => t.MessageErreur, avertissement));
            // Validé par l'opérateur (webhook ou simulation) : action du système, pas de l'utilisateur connecté.
            await _journal.EnregistrerPourAsync(null, "Paiement mobile", "Systeme", "paiement-mobile.valider", "Reservation", transaction.ReservationId,
                $"Paiement mobile {LibelleOperateur(transaction.Operateur)} de {JournalServices.Montant(transaction.Montant)} validé "
                + $"(réf. {transaction.ReferenceFournisseur}) pour la réservation n° {transaction.ReservationId:D6}"
                + (apresAnnulation ? " — reçu après annulation, à rembourser" : ""));
        }
        catch (Exception ex) when (ex is ConflictException or InvalidOperationException or NotFoundException)
        {
            // Cas imprévu (réservation introuvable…) : l'argent est reçu mais non rattaché, à régulariser par l'admin.
            _logger.LogError(ex, "Paiement mobile {Id} reçu ({Reference}) mais non rattaché à la réservation {Reservation}",
                transactionId, transaction.ReferenceFournisseur, transaction.ReservationId);
            await _context.TransactionsMobiles.Where(t => t.Id == transactionId)
                .ExecuteUpdateAsync(maj => maj.SetProperty(t => t.MessageErreur,
                    "Paiement reçu mais la réservation ne peut plus l'accepter : ImmoPlus vous recontacte pour le remboursement."));
        }
        return nouveauStatut;
    }

    private bool SignatureValide(byte[] corps, string? signature)
    {
        var secret = _configuration["PaiementMobile:SecretWebhook"];
        // Sans secret configuré, aucune notification n'est acceptée.
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }
        byte[] recue;
        try
        {
            recue = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        var attendue = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), corps);
        return CryptographicOperations.FixedTimeEquals(attendue, recue);
    }

    /// <summary>Passe en « Expiree » une transaction en attente depuis plus de 10 minutes (à enregistrer par l'appelant).</summary>
    private static bool ExpirerSiBesoin(TransactionMobile t)
    {
        if (t.Statut != StatutTransactionMobile.EnAttente || DateTime.UtcNow - t.DateCreation <= DureeValidite)
        {
            return false;
        }
        t.Statut = StatutTransactionMobile.Expiree;
        t.MessageErreur = "Paiement non validé sur le téléphone dans les 10 minutes";
        t.DateMaj = DateTime.UtcNow;
        return true;
    }

    /// <summary>Numéro guinéen : 9 chiffres commençant par 6 ; espaces, points, tirets et indicatif +224 / 00224 tolérés.</summary>
    public static string NormaliserTelephone(string? telephone)
    {
        var chiffres = Separateurs().Replace(telephone ?? "", "");
        if (chiffres.StartsWith('+'))
        {
            chiffres = chiffres[1..];
        }
        else if (chiffres.StartsWith("00"))
        {
            chiffres = chiffres[2..];
        }
        if (chiffres.Length == 12 && chiffres.StartsWith("224"))
        {
            chiffres = chiffres[3..];
        }
        if (!TelephoneGuineen().IsMatch(chiffres))
        {
            throw new InvalidOperationException("Numéro invalide : indiquez un numéro guinéen de 9 chiffres commençant par 6 (ex. 620 00 00 00)");
        }
        return chiffres;
    }

    /// <summary>« 6•• •• •• 12 » : premier et deux derniers chiffres.</summary>
    public static string MasquerTelephone(string telephone) =>
        telephone.Length == 9 ? $"{telephone[0]}•• •• •• {telephone[7..]}" : "•••";

    public static string LibelleOperateur(OperateurMobile operateur) => operateur switch
    {
        OperateurMobile.OrangeMoney => "Orange Money",
        OperateurMobile.MtnMomo => "MTN Mobile Money",
        _ => operateur.ToString()
    };

    private TransactionMobileDto VersDto(TransactionMobile t) => new()
    {
        Id = t.Id,
        ReservationId = t.ReservationId,
        Operateur = t.Operateur.ToString(),
        Telephone = MasquerTelephone(t.Telephone),
        Montant = t.Montant,
        Statut = t.Statut.ToString(),
        ReferenceFournisseur = t.ReferenceFournisseur,
        MessageErreur = t.MessageErreur,
        DateCreation = t.DateCreation,
        Instructions = Instructions,
        Simulation = EstSimulation
    };

    private static string Tronquer(string valeur, int longueur) => valeur.Length <= longueur ? valeur : valeur[..longueur];
    private static string Montant(decimal montant) => $"{montant.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} GNF";

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separateurs();

    [GeneratedRegex(@"^6\d{8}$")]
    private static partial Regex TelephoneGuineen();
}

/// <summary>Signature du webhook absente ou invalide → 401.</summary>
public class SignatureInvalideException : Exception
{
    public SignatureInvalideException() : base("Signature invalide") { }
}
