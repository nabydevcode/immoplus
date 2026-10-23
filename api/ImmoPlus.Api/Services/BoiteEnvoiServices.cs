namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Boîte d'envoi des emails et SMS. Mise en file : par un contexte annexe sur la connexion de l'action métier, donc dans
/// sa transaction quand elle en a une (le message disparaît si l'action est annulée), sinon juste après son enregistrement.
/// </summary>
public class BoiteEnvoiServices
{
    /// <summary>Messages envoyés ou abandonnés conservés 90 jours (ils contiennent des données personnelles).</summary>
    public const int ConservationJours = 90;

    private readonly AppDbContext _context;

    public BoiteEnvoiServices(AppDbContext context)
    {
        _context = context;
    }

    public Task AjouterEmailAsync(string destinataire, int? utilisateurId, string sujet, string html, string evenement, string? reference) =>
        AjouterAsync(new MessageSortant
        {
            Type = TypeMessageSortant.Email,
            Destinataire = Tronquer(destinataire, 256),
            UtilisateurId = utilisateurId,
            Sujet = Tronquer(sujet, 300),
            Contenu = html,
            Evenement = Tronquer(evenement, 60),
            Reference = reference is null ? null : Tronquer(reference, 60)
        });

    public Task AjouterSmsAsync(Utilisateur destinataire, string evenement, string texte, CanalMessage canal) =>
        AjouterAsync(new MessageSortant
        {
            Type = TypeMessageSortant.Sms,
            Destinataire = Tronquer(destinataire.Telephone ?? "", 256),
            UtilisateurId = destinataire.Id,
            Contenu = texte,
            Evenement = Tronquer(evenement, 60),
            Canal = canal
        });

    private async Task AjouterAsync(MessageSortant message)
    {
        var maintenant = DateTime.UtcNow;
        message.DateCreation = maintenant;
        message.ProchaineTentative = maintenant;
        message.Statut = StatutMessageSortant.EnAttente;
        await using var contexte = await AppDbContext.AnnexeAsync(_context);
        contexte.MessagesSortants.Add(message);
        await contexte.SaveChangesAsync();
    }

    /// <summary>Admin : messages du plus récent au plus ancien, filtrés par statut ; 100 par page au plus.</summary>
    public async Task<PagedResultDto<MessageSortantDto>> ListerAsync(string? statut, int page, int taille)
    {
        if (page < 1) page = 1;
        taille = taille < 1 ? 20 : Math.Min(taille, 100);
        var requete = _context.MessagesSortants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (int.TryParse(statut, out _) || !Enum.TryParse<StatutMessageSortant>(statut.Trim(), true, out var s) || !Enum.IsDefined(s))
            {
                throw new InvalidOperationException("Statut invalide. Utilisez EnAttente, Envoye, Echec ou Abandonne");
            }
            requete = requete.Where(m => m.Statut == s);
        }
        var total = await requete.CountAsync();
        var messages = await requete
            .OrderByDescending(m => m.DateCreation).ThenByDescending(m => m.Id)
            .Skip((page - 1) * taille).Take(taille)
            .Select(m => new MessageSortantDto
            {
                Id = m.Id,
                Type = m.Type.ToString(),
                Destinataire = m.Destinataire,
                UtilisateurId = m.UtilisateurId,
                Sujet = m.Sujet,
                // Texte du SMS seulement : le HTML des emails reste en base.
                Texte = m.Type == TypeMessageSortant.Sms ? m.Contenu : null,
                Reference = m.Reference,
                Evenement = m.Evenement,
                Statut = m.Statut.ToString(),
                Tentatives = m.Tentatives,
                ProchaineTentative = m.Statut == StatutMessageSortant.EnAttente || m.Statut == StatutMessageSortant.Echec ? m.ProchaineTentative : null,
                DerniereErreur = m.DerniereErreur,
                DateCreation = m.DateCreation,
                DateDerniereTentative = m.DateDerniereTentative,
                DateEnvoi = m.DateEnvoi
            })
            .ToListAsync();
        return new PagedResultDto<MessageSortantDto>
        {
            Donnees = messages,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = total,
            TotalPages = (int)Math.Ceiling(total / (double)taille)
        };
    }

    /// <summary>Supprime les messages envoyés ou abandonnés depuis plus de 90 jours.</summary>
    public Task<int> PurgerAsync(CancellationToken annulation = default)
    {
        var limite = DateTime.UtcNow.AddDays(-ConservationJours);
        return _context.MessagesSortants
            .Where(m => (m.Statut == StatutMessageSortant.Envoye || m.Statut == StatutMessageSortant.Abandonne) && m.DateCreation < limite)
            .ExecuteDeleteAsync(annulation);
    }

    private static string Tronquer(string valeur, int longueur) => valeur.Length > longueur ? valeur[..longueur] : valeur;
}

/// <summary>
/// Dépile la boîte d'envoi : un message à la fois, verrouillé par SELECT … FOR UPDATE SKIP LOCKED dans une transaction.
/// Plusieurs instances (ou plusieurs dépileurs) se partagent ainsi la file sans jamais prendre le même message.
/// Échec : nouvel essai après 1 min, 5 min, 30 min puis 2 h ; abandon au 5e échec.
/// Un message n'est marqué envoyé qu'après la réponse du serveur : si l'instance s'arrête entre les deux, il repartira
/// (au plus un doublon dans ce cas extrême, jamais de perte).
/// </summary>
public class EnvoiMessagesServices
{
    public const int MaxTentatives = 5;
    public static readonly TimeSpan[] Delais =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    private readonly AppDbContext _context;
    private readonly EmailServices _email;
    private readonly SmsServices _sms;
    private readonly ILogger<EnvoiMessagesServices> _logger;

    public EnvoiMessagesServices(AppDbContext context, EmailServices email, SmsServices sms, ILogger<EnvoiMessagesServices> logger)
    {
        _context = context;
        _email = email;
        _sms = sms;
        _logger = logger;
    }

    /// <summary>Traite le prochain message dû ; false si la file est vide (ou entièrement prise par d'autres dépileurs).</summary>
    public async Task<bool> TraiterSuivantAsync(CancellationToken annulation = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(annulation);
        // Colonne « timestamp without time zone » en UTC : paramètre typé explicitement (sinon EF le type en timestamptz
        // et Npgsql refuse une date sans genre), comparé tel quel.
        var maintenant = new NpgsqlParameter("maintenant", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        };
        var message = (await _context.MessagesSortants
            .FromSql($"""
                SELECT * FROM "MessagesSortants"
                WHERE "Statut" IN ({(int)StatutMessageSortant.EnAttente}, {(int)StatutMessageSortant.Echec}) AND "ProchaineTentative" <= {maintenant}
                ORDER BY "ProchaineTentative", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(annulation))
            .FirstOrDefault();
        if (message is null)
        {
            return false;
        }

        message.Tentatives++;
        message.DateDerniereTentative = DateTime.UtcNow;
        try
        {
            if (message.Type == TypeMessageSortant.Email)
            {
                await _email.EnvoyerAsync(message.Destinataire, message.Sujet ?? "", message.Contenu);
                MarquerEnvoye(message);
            }
            else
            {
                await TransmettreSmsAsync(message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !annulation.IsCancellationRequested)
        {
            MarquerEchec(message, ex);
        }

        await _context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }

    /// <summary>Vide la file (messages dus) ; renvoie le nombre de messages traités.</summary>
    public async Task<int> TraiterToutAsync(CancellationToken annulation = default)
    {
        var traites = 0;
        while (!annulation.IsCancellationRequested && await TraiterSuivantAsync(annulation))
        {
            traites++;
            _context.ChangeTracker.Clear();
        }
        return traites;
    }

    private async Task TransmettreSmsAsync(MessageSortant message)
    {
        if (!_sms.EstActif || message.UtilisateurId is null)
        {
            message.Statut = StatutMessageSortant.Abandonne;
            message.DerniereErreur = "SMS désactivés (Sms:Mode) : message non envoyé";
            return;
        }
        // Trace NotificationsSms (historique admin et « mes SMS ») dans la même transaction que le statut du message.
        var trace = await _sms.TransmettreAsync(message);
        if (trace.Statut == StatutNotificationSms.Echec)
        {
            // Numéro invalide : inutile de réessayer.
            message.Statut = StatutMessageSortant.Abandonne;
            message.DerniereErreur = trace.Erreur;
        }
        else
        {
            MarquerEnvoye(message);
        }
        _context.NotificationsSms.Add(trace);
    }

    private static void MarquerEnvoye(MessageSortant message)
    {
        message.Statut = StatutMessageSortant.Envoye;
        message.DateEnvoi = DateTime.UtcNow;
        message.DerniereErreur = null;
    }

    private void MarquerEchec(MessageSortant message, Exception ex)
    {
        var erreur = $"{ex.GetType().Name} : {ex.Message}";
        message.DerniereErreur = erreur.Length > 1000 ? erreur[..1000] : erreur;
        if (message.Tentatives >= MaxTentatives)
        {
            message.Statut = StatutMessageSortant.Abandonne;
            _logger.LogError(ex, "{Type} n° {Id} « {Evenement} » abandonné après {Tentatives} tentatives",
                message.Type, message.Id, message.Evenement, message.Tentatives);
            if (message.Type == TypeMessageSortant.Sms && message.UtilisateurId is { } utilisateurId)
            {
                _context.NotificationsSms.Add(_sms.TraceEchec(message, utilisateurId, message.DerniereErreur));
            }
            return;
        }
        message.Statut = StatutMessageSortant.Echec;
        message.ProchaineTentative = DateTime.UtcNow + Delais[Math.Min(message.Tentatives, Delais.Length) - 1];
        _logger.LogWarning(ex, "{Type} n° {Id} « {Evenement} » en échec (tentative {Tentatives}), nouvel essai à {Prochaine:HH:mm} UTC",
            message.Type, message.Id, message.Evenement, message.Tentatives, message.ProchaineTentative);
    }
}

/// <summary>
/// Envoie en continu les messages de la boîte d'envoi (Boite:IntervalleSecondes, 2 s par défaut, entre deux vérifications
/// quand la file est vide). Tourne sur chaque instance : SKIP LOCKED répartit les messages sans doublon.
/// </summary>
public class EnvoiMessagesHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvoiMessagesHostedService> _logger;

    public EnvoiMessagesHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<EnvoiMessagesHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalle = TimeSpan.FromSeconds(Math.Clamp(_configuration.GetValue("Boite:IntervalleSecondes", 2.0), 0.2, 60));
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EnvoiMessagesServices>().TraiterToutAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Base momentanément indisponible… : on réessaie au tour suivant.
                _logger.LogError(ex, "Échec du traitement de la boîte d'envoi");
            }
            try
            {
                await Task.Delay(intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
