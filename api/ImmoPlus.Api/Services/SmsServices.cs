namespace ImmoPlus.Api.Services;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
/// Un numéro invalide ou une erreur du fournisseur donne une trace en Echec, sans jamais bloquer l'action métier.
/// </summary>
public partial class SmsServices
{
    public const int LongueurMaxTexte = 320;

    private readonly AppDbContext _context;
    private readonly IFournisseurSms _fournisseur;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmsServices> _logger;

    public SmsServices(AppDbContext context, IFournisseurSms fournisseur, IConfiguration configuration, ILogger<SmsServices> logger)
    {
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

    /// <summary>Envoie (ou simule) un SMS ; ne lève jamais d'exception.</summary>
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
            var sms = new NotificationSms
            {
                UtilisateurId = destinataire.Id,
                Canal = Canal,
                Texte = texte,
                Evenement = evenement.Length > 50 ? evenement[..50] : evenement,
                DateEnvoi = DateTime.UtcNow
            };

            var numero = NormaliserTelephone(destinataire.Telephone);
            if (numero is null)
            {
                sms.Telephone = Tronquer(destinataire.Telephone ?? "", 30);
                sms.Statut = StatutNotificationSms.Echec;
                sms.Erreur = "Numéro de téléphone invalide (attendu : 9 chiffres commençant par 6, indicatif +224)";
            }
            else
            {
                sms.Telephone = numero;
                var autorises = _configuration.GetSection("Sms:NumerosAutorises").Get<string[]>() ?? [];
                // Par sécurité, sans Sms:EnvoyerATous=true, seuls les numéros listés reçoivent un vrai SMS.
                var autorise = _configuration.GetValue<bool>("Sms:EnvoyerATous") || autorises.Select(NormaliserTelephone).Contains(numero);
                if (!EstSimulation && !autorise)
                {
                    sms.Statut = StatutNotificationSms.Simule;
                    sms.Erreur = "Numéro hors de la liste de test (Sms:NumerosAutorises) : SMS non envoyé";
                    await EnregistrerAsync(sms);
                    return;
                }
                try
                {
                    sms.ReferenceFournisseur = await _fournisseur.EnvoyerAsync(numero, texte, sms.Canal);
                    sms.Statut = EstSimulation ? StatutNotificationSms.Simule : StatutNotificationSms.Envoye;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Échec de l'envoi du SMS « {Evenement} » à l'utilisateur {Id}", evenement, destinataire.Id);
                    sms.Statut = StatutNotificationSms.Echec;
                    sms.Erreur = Tronquer(ex.Message, 500);
                }
            }
            await EnregistrerAsync(sms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'enregistrer le SMS « {Evenement} » de l'utilisateur {Id}", evenement, destinataire.Id);
        }
    }

    /// <summary>
    /// Enregistré par un contexte à part sur la même connexion (et la même transaction s'il y en a une) : les modifications
    /// encore en attente de l'appelant ne sont pas enregistrées à sa place, et SQLite ne se bloque pas sur deux connexions.
    /// </summary>
    private async Task EnregistrerAsync(NotificationSms sms)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_context.Database.GetDbConnection()).Options;
        await using var contexte = new AppDbContext(options);
        if (_context.Database.CurrentTransaction is { } transaction)
        {
            await contexte.Database.UseTransactionAsync(transaction.GetDbTransaction());
        }
        contexte.NotificationsSms.Add(sms);
        await contexte.SaveChangesAsync();
    }

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
