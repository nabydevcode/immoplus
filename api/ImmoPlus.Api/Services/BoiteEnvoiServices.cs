namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
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
                DateEnvoi = m.DateEnvoi,
                CopieEnvoyes = m.CopieEnvoyes.HasValue ? m.CopieEnvoyes.Value.ToString() : null,
                CopieTentatives = m.CopieTentatives,
                CopieErreur = m.CopieErreur
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

    /// <summary>
    /// Admin : remet en file les messages Abandonne ou Echec (SMTP corrigé…), avec 5 nouvelles tentatives et un envoi immédiat ;
    /// <paramref name="id"/> : ce message seulement. Mise à jour conditionnelle en une requête : un message en cours d'envoi
    /// (verrouillé par le dépileur) est réévalué après son traitement, un message parti entre-temps n'est pas relancé.
    /// </summary>
    public async Task<int> RelancerAsync(string? statut, int? id = null)
    {
        StatutMessageSortant? filtre = null;
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (int.TryParse(statut, out _) || !Enum.TryParse<StatutMessageSortant>(statut.Trim(), true, out var s)
                || s is not (StatutMessageSortant.Abandonne or StatutMessageSortant.Echec))
            {
                throw new InvalidOperationException("Statut invalide. Seuls les messages Abandonne ou Echec peuvent être relancés");
            }
            filtre = s;
        }
        else if (id is null)
        {
            throw new InvalidOperationException("Précisez le statut des messages à relancer (Abandonne ou Echec)");
        }

        var requete = _context.MessagesSortants
            .Where(m => m.Statut == StatutMessageSortant.Abandonne || m.Statut == StatutMessageSortant.Echec);
        if (filtre is { } f)
        {
            requete = requete.Where(m => m.Statut == f);
        }
        if (id is { } identifiant)
        {
            if (!await _context.MessagesSortants.AnyAsync(m => m.Id == identifiant))
            {
                throw new NotFoundException("Message introuvable");
            }
            requete = requete.Where(m => m.Id == identifiant);
        }
        var maintenant = DateTime.UtcNow;
        var relances = await requete.ExecuteUpdateAsync(m => m
            .SetProperty(x => x.Statut, StatutMessageSortant.EnAttente)
            .SetProperty(x => x.Tentatives, 0)
            .SetProperty(x => x.ProchaineTentative, maintenant));
        if (id is not null && relances == 0)
        {
            throw new ConflictException("Ce message n'est ni abandonné ni en échec : il ne peut pas être relancé");
        }
        return relances;
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
/// Imap:Actif : chaque email parti est aussi copié dans le dossier « Envoyés » de l'expéditeur, sous le même verrou ; un échec
/// de cette copie n'affecte jamais l'email (déjà parti) : il est tracé (CopieEnvoyes) et la copie seule est réessayée
/// après 1 min, 5 min puis 30 min, abandonnée au 4e échec.
/// </summary>
public class EnvoiMessagesServices
{
    public const int MaxTentatives = 5;
    public static readonly TimeSpan[] Delais =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];
    public const int MaxTentativesCopie = 4;
    public static readonly TimeSpan[] DelaisCopie = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    private readonly AppDbContext _context;
    private readonly EmailServices _email;
    private readonly SmsServices _sms;
    private readonly CopieEnvoyesServices _copie;
    private readonly ILogger<EnvoiMessagesServices> _logger;

    public EnvoiMessagesServices(AppDbContext context, EmailServices email, SmsServices sms, CopieEnvoyesServices copie,
        ILogger<EnvoiMessagesServices> logger)
    {
        _context = context;
        _email = email;
        _sms = sms;
        _copie = copie;
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
        // SMTP non configuré : les emails restent en attente (sans tentative consommée) et ne bloquent pas les SMS derrière eux.
        var emailsActifs = _email.EstConfigure;
        var message = (await _context.MessagesSortants
            .FromSql($"""
                SELECT * FROM "MessagesSortants"
                WHERE "Statut" IN ({(int)StatutMessageSortant.EnAttente}, {(int)StatutMessageSortant.Echec}) AND "ProchaineTentative" <= {maintenant}
                  AND ({emailsActifs} OR "Type" <> {(int)TypeMessageSortant.Email})
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
                var dateEnvoi = DateTime.UtcNow;
                var mime = ConstruireEmail(message, dateEnvoi);
                await _email.EnvoyerAsync(mime);
                MarquerEnvoye(message, dateEnvoi);
                if (_copie.EstActive)
                {
                    await CopierDansEnvoyesAsync(message, mime, annulation);
                }
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

    /// <summary>Vide la file (messages dus) puis réessaie les copies « Envoyés » dues ; renvoie le nombre de messages traités.</summary>
    public async Task<int> TraiterToutAsync(CancellationToken annulation = default)
    {
        var traites = 0;
        while (!annulation.IsCancellationRequested && await TraiterSuivantAsync(annulation))
        {
            traites++;
            _context.ChangeTracker.Clear();
        }
        while (!annulation.IsCancellationRequested && await TraiterCopieSuivanteAsync(annulation))
        {
            _context.ChangeTracker.Clear();
        }
        return traites;
    }

    /// <summary>Nouvel essai de la prochaine copie « Envoyés » due (email déjà parti, copie en échec) ; false s'il n'y en a pas.</summary>
    public async Task<bool> TraiterCopieSuivanteAsync(CancellationToken annulation = default)
    {
        if (!_copie.EstActive)
        {
            return false;
        }
        await using var transaction = await _context.Database.BeginTransactionAsync(annulation);
        var maintenant = new NpgsqlParameter("maintenant", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        };
        var message = (await _context.MessagesSortants
            .FromSql($"""
                SELECT * FROM "MessagesSortants"
                WHERE "CopieEnvoyes" = {(int)StatutCopieEnvoyes.Echec} AND "CopieProchaineTentative" <= {maintenant}
                ORDER BY "CopieProchaineTentative", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(annulation))
            .FirstOrDefault();
        if (message is null)
        {
            return false;
        }
        MimeKit.MimeMessage? mime = null;
        try
        {
            // Même date et même Message-Id que l'email envoyé : la copie est identique à l'original.
            mime = ConstruireEmail(message, message.DateEnvoi ?? message.DateDerniereTentative ?? DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            NoterEchecCopie(message, ex);
        }
        if (mime is not null)
        {
            await CopierDansEnvoyesAsync(message, mime, annulation);
        }
        await _context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }

    /// <summary>Message-Id stable (n° du message et date de création) : une copie réessayée garde l'identifiant de l'original.</summary>
    private MimeKit.MimeMessage ConstruireEmail(MessageSortant message, DateTime dateEnvoi) =>
        _email.Construire(message.Destinataire, message.Sujet ?? "", message.Contenu, dateEnvoi,
            $"immoplus.{message.Id}.{message.DateCreation:yyyyMMddHHmmssfff}");

    /// <summary>Copie IMAP de l'email parti : ne lève jamais d'exception (l'email reste marqué envoyé), l'échec est tracé.</summary>
    private async Task CopierDansEnvoyesAsync(MessageSortant message, MimeKit.MimeMessage mime, CancellationToken annulation)
    {
        message.CopieTentatives++;
        try
        {
            await _copie.DeposerAsync(mime, annulation);
            message.CopieEnvoyes = StatutCopieEnvoyes.Ok;
            message.CopieErreur = null;
            message.CopieProchaineTentative = null;
        }
        catch (Exception ex)
        {
            NoterEchecCopie(message, ex);
        }
    }

    private void NoterEchecCopie(MessageSortant message, Exception ex)
    {
        var erreur = $"{ex.GetType().Name} : {ex.Message}";
        message.CopieErreur = erreur.Length > 1000 ? erreur[..1000] : erreur;
        if (message.CopieTentatives >= MaxTentativesCopie)
        {
            message.CopieEnvoyes = StatutCopieEnvoyes.Abandonnee;
            message.CopieProchaineTentative = null;
            _logger.LogWarning("Copie « Envoyés » de l'email n° {Id} « {Evenement} » abandonnée après {Tentatives} essais : {Erreur}",
                message.Id, message.Evenement, message.CopieTentatives, message.CopieErreur);
            return;
        }
        message.CopieEnvoyes = StatutCopieEnvoyes.Echec;
        message.CopieProchaineTentative = DateTime.UtcNow + DelaisCopie[Math.Min(message.CopieTentatives, DelaisCopie.Length) - 1];
        // Avertissement sans pile ni contenu : l'email est parti, seule sa copie IMAP manque.
        _logger.LogWarning("Copie « Envoyés » de l'email n° {Id} « {Evenement} » en échec (essai {Tentatives}), nouvel essai à {Prochaine:HH:mm} UTC : {Erreur}",
            message.Id, message.Evenement, message.CopieTentatives, message.CopieProchaineTentative, message.CopieErreur);
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

    private static void MarquerEnvoye(MessageSortant message, DateTime? dateEnvoi = null)
    {
        message.Statut = StatutMessageSortant.Envoye;
        message.DateEnvoi = dateEnvoi ?? DateTime.UtcNow;
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
