namespace ImmoPlus.Api.Services;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Fournisseur d'envoi de SMS / WhatsApp. Un vrai opérateur (Orange, MTN, passerelle WhatsApp) implémentera cette interface ;
/// en attendant, FournisseurSmsSimule n'envoie rien.
/// </summary>
public interface IFournisseurSms
{
    /// <returns>Référence du message chez le fournisseur.</returns>
    Task<string> EnvoyerAsync(string telephone, string texte, CanalMessage canal);
}

/// <summary>Simulation locale : aucun SMS réel, une référence factice est renvoyée.</summary>
public class FournisseurSmsSimule : IFournisseurSms
{
    public Task<string> EnvoyerAsync(string telephone, string texte, CanalMessage canal) =>
        Task.FromResult($"SIM-{Guid.NewGuid():N}"[..20]);
}

/// <summary>
/// Notifications courtes par SMS / WhatsApp, en plus de l'email (jamais à la place). Mode configuré par Sms:Mode :
/// « Desactive » (rien n'est envoyé ni enregistré), « Simulation » (tracé avec le statut Simule, rien ne part) ou
/// « PasserelleAndroid » (vrais SMS). Les vrais envois sont limités aux numéros de Sms:NumerosAutorises, sauf si
/// Sms:EnvoyerATous=true : les autres destinataires sont tracés en Simule, pour ne jamais écrire par erreur à des inconnus.
/// Les SMS passent par la boîte d'envoi (MessagesSortants) : l'action métier n'attend jamais le fournisseur, et une erreur
/// du fournisseur est réessayée (trace en Echec seulement à l'abandon). Un numéro invalide donne une trace en Echec.
/// </summary>
public partial class SmsServices
{
    public const int LongueurMaxTexte = 320;

    private readonly AppDbContext _context;
    private readonly IFournisseurSms _fournisseur;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmsServices> _logger;
    private readonly BoiteEnvoiServices _boite;

    public SmsServices(AppDbContext context, IFournisseurSms fournisseur, IConfiguration configuration, ILogger<SmsServices> logger,
        BoiteEnvoiServices boite)
    {
        _boite = boite;
        _context = context;
        _fournisseur = fournisseur;
        _configuration = configuration;
        _logger = logger;
    }

    public bool EstActif => !string.Equals(_configuration["Sms:Mode"] ?? "Desactive", "Desactive", StringComparison.OrdinalIgnoreCase);
    private bool EstSimulation => string.Equals(_configuration["Sms:Mode"], "Simulation", StringComparison.OrdinalIgnoreCase);
    private CanalMessage Canal => Enum.TryParse<CanalMessage>(_configuration["Sms:Canal"], true, out var canal) && Enum.IsDefined(canal)
        ? canal
        : CanalMessage.Sms;

    /// <summary>Adresse du site sans « https:// », pour un lien court dans le SMS (toujours Frontend:Url).</summary>
    public string Site => Regex.Replace((_configuration["Frontend:Url"] ?? "http://localhost:5173").TrimEnd('/'), "^https?://", "");

    /// <summary>
    /// Met le SMS dans la boîte d'envoi (envoyé en arrière-plan, voir EnvoiMessagesServices) ; ne lève jamais d'exception.
    /// Mode et préférence du destinataire vérifiés ici ; numéro, liste de test et fournisseur au moment de l'envoi.
    /// </summary>
    /// <param name="evenement">Code court de l'événement, ex. « reservation-confirmee ».</param>
    public async Task EnvoyerAsync(Utilisateur destinataire, string evenement, string texte)
    {
        if (!EstActif || !destinataire.NotificationsSms)
        {
            return;
        }
        try
        {
            texte = texte.Trim();
            if (texte.Length > LongueurMaxTexte)
            {
                texte = texte[..(LongueurMaxTexte - 1)] + "…";
            }
            await _boite.AjouterSmsAsync(destinataire, evenement.Length > 50 ? evenement[..50] : evenement, texte, Canal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de mettre en file le SMS « {Evenement} » de l'utilisateur {Id}", evenement, destinataire.Id);
        }
    }

    /// <summary>
    /// Envoi réel (ou simulé) d'un SMS de la boîte d'envoi : renvoie la trace NotificationsSms à enregistrer.
    /// Numéro invalide : trace en Echec (définitif). Erreur du fournisseur : exception (nouvelle tentative plus tard).
    /// </summary>
    public async Task<NotificationSms> TransmettreAsync(MessageSortant message)
    {
        var sms = new NotificationSms
        {
            UtilisateurId = message.UtilisateurId ?? 0,
            Canal = message.Canal ?? Canal,
            Texte = message.Contenu,
            Evenement = message.Evenement.Length > 50 ? message.Evenement[..50] : message.Evenement,
            DateEnvoi = DateTime.UtcNow
        };
        var numero = NormaliserTelephone(message.Destinataire);
        if (numero is null)
        {
            sms.Telephone = Tronquer(message.Destinataire, 30);
            sms.Statut = StatutNotificationSms.Echec;
            sms.Erreur = "Numéro de téléphone invalide (attendu : 9 chiffres commençant par 6, indicatif +224)";
            return sms;
        }
        sms.Telephone = numero;
        var autorises = _configuration.GetSection("Sms:NumerosAutorises").Get<string[]>() ?? [];
        // Par sécurité, sans Sms:EnvoyerATous=true, seuls les numéros listés reçoivent un vrai SMS.
        var autorise = _configuration.GetValue<bool>("Sms:EnvoyerATous") || autorises.Select(NormaliserTelephone).Contains(numero);
        if (!EstSimulation && !autorise)
        {
            sms.Statut = StatutNotificationSms.Simule;
            sms.Erreur = "Numéro hors de la liste de test (Sms:NumerosAutorises) : SMS non envoyé";
            return sms;
        }
        sms.ReferenceFournisseur = await _fournisseur.EnvoyerAsync(numero, sms.Texte, sms.Canal);
        sms.Statut = EstSimulation ? StatutNotificationSms.Simule : StatutNotificationSms.Envoye;
        return sms;
    }

    /// <summary>Trace d'un SMS abandonné après plusieurs échecs du fournisseur.</summary>
    public NotificationSms TraceEchec(MessageSortant message, int utilisateurId, string? erreur) => new()
    {
        UtilisateurId = utilisateurId,
        Canal = message.Canal ?? Canal,
        Texte = message.Contenu,
        Evenement = message.Evenement.Length > 50 ? message.Evenement[..50] : message.Evenement,
        Telephone = Tronquer(NormaliserTelephone(message.Destinataire) ?? message.Destinataire, 30),
        Statut = StatutNotificationSms.Echec,
        Erreur = erreur is null ? null : Tronquer(erreur, 500),
        DateEnvoi = DateTime.UtcNow
    };

    /// <summary>
    /// Numéro guinéen normalisé « +2246XXXXXXXX » : 9 chiffres commençant par 6, avec ou sans indicatif (+224, 00224, 224).
    /// Espaces, points, tirets et parenthèses ignorés. null si le numéro n'est pas un mobile guinéen valide.
    /// </summary>
    public static string? NormaliserTelephone(string? telephone)
    {
        if (string.IsNullOrWhiteSpace(telephone))
        {
            return null;
        }
        var brut = Separateurs().Replace(telephone.Trim(), "");
        if (brut.StartsWith('+'))
        {
            brut = brut[1..];
        }
        else if (brut.StartsWith("00"))
        {
            brut = brut[2..];
        }
        if (!brut.All(char.IsAsciiDigit))
        {
            return null;
        }
        if (brut.Length == 12 && brut.StartsWith("224"))
        {
            brut = brut[3..];
        }
        return brut.Length == 9 && brut[0] == '6' ? $"+224{brut}" : null;
    }

    public async Task<PagedResultDto<NotificationSmsDto>> ListerAsync(int page, int taille)
    {
        if (page < 1) page = 1;
        taille = taille < 1 ? 20 : Math.Min(taille, 100);
        var requete = _context.NotificationsSms.AsNoTracking();
        var total = await requete.CountAsync();
        var donnees = await requete
            .OrderByDescending(n => n.DateEnvoi).ThenByDescending(n => n.Id)
            .Skip((page - 1) * taille).Take(taille)
            .Select(n => new { Sms = n, Nom = n.Utilisateur!.Nom })
            .ToListAsync();
        return new PagedResultDto<NotificationSmsDto>
        {
            Donnees = donnees.Select(d => VersDto(d.Sms, d.Nom)).ToList(),
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = total,
            TotalPages = (int)Math.Ceiling(total / (double)taille)
        };
    }

    public async Task<List<NotificationSmsDto>> ListerPourUtilisateurAsync(int utilisateurId) =>
        (await _context.NotificationsSms.AsNoTracking()
            .Where(n => n.UtilisateurId == utilisateurId)
            .OrderByDescending(n => n.DateEnvoi).ThenByDescending(n => n.Id)
            .Take(50)
            .ToListAsync())
        .Select(n => VersDto(n, null)).ToList();

    private static NotificationSmsDto VersDto(NotificationSms n, string? nom) => new()
    {
        Id = n.Id,
        UtilisateurId = n.UtilisateurId,
        UtilisateurNom = nom,
        Telephone = n.Telephone,
        Canal = n.Canal.ToString(),
        Texte = n.Texte,
        Statut = n.Statut.ToString(),
        ReferenceFournisseur = n.ReferenceFournisseur,
        Erreur = n.Erreur,
        DateEnvoi = n.DateEnvoi,
        Evenement = n.Evenement
    };

    private static string Tronquer(string valeur, int longueur) => valeur.Length > longueur ? valeur[..longueur] : valeur;

    [GeneratedRegex(@"[\s.\-()/]")]
    private static partial Regex Separateurs();
}
