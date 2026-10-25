namespace ImmoPlus.Api.Services;

using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;

/// <summary>
/// Copie de chaque email envoyé dans le dossier « Envoyés » de la boîte de l'expéditeur, par IMAP APPEND (drapeau \Seen),
/// pour que l'équipe retrouve dans son webmail tout ce que le site a envoyé. Réglages Imap:Actif (false par défaut),
/// Host, Port (993), Securite (comme Smtp:Securite : 993 → SSL implicite, 143 → STARTTLS), Utilisateur et MotDePasse
/// (ceux du SMTP s'ils sont vides), Dossier (vide : dossier spécial « Sent » annoncé par le serveur, sinon noms usuels).
/// Une seule connexion par instance, réutilisée d'un email à l'autre (reconnexion si coupée, inactive ou réglages changés) :
/// le verrou ne protège que cette connexion, aucune règle métier n'en dépend.
/// </summary>
public sealed class CopieEnvoyesServices : IAsyncDisposable
{
    /// <summary>Un serveur IMAP muet ne retient pas la boîte d'envoi plus de 30 s.</summary>
    public static readonly TimeSpan Delai = TimeSpan.FromSeconds(30);

    /// <summary>Au-delà, la connexion est rouverte plutôt que réutilisée (les serveurs coupent souvent les sessions inactives sans prévenir).</summary>
    public static readonly TimeSpan InactiviteMax = TimeSpan.FromMinutes(5);

    /// <summary>Noms usuels essayés quand le serveur n'annonce pas de dossier spécial \Sent.</summary>
    public static readonly string[] DossiersUsuels = ["Sent", "INBOX.Sent", "Sent Messages", "Envoyés"];

    private readonly IConfiguration _configuration;
    private readonly ILogger<CopieEnvoyesServices> _logger;
    private readonly SemaphoreSlim _connexion = new(1, 1);
    private ImapClient? _client;
    private IMailFolder? _dossier;
    private string? _cleReglages;
    private DateTime _derniereUtilisation;

    public CopieEnvoyesServices(IConfiguration configuration, ILogger<CopieEnvoyesServices> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool EstActive => _configuration.GetValue("Imap:Actif", false);

    public int Port => _configuration.GetValue("Imap:Port", 993);

    public SecuriteSmtp Securite => EmailServices.LireSecurite(_configuration, "Imap:Securite");

    public static SecureSocketOptions OptionSocket(SecuriteSmtp securite, int port) => securite switch
    {
        SecuriteSmtp.Aucune => SecureSocketOptions.None,
        SecuriteSmtp.SslImplicite => SecureSocketOptions.SslOnConnect,
        SecuriteSmtp.StartTls => SecureSocketOptions.StartTls,
        _ => port switch
        {
            993 => SecureSocketOptions.SslOnConnect,
            143 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.StartTlsWhenAvailable
        }
    };

    /// <summary>Utilisateur IMAP : Imap:Utilisateur, sinon celui du SMTP (même boîte chez la plupart des hébergeurs).</summary>
    public string? Utilisateur => Premier(_configuration["Imap:Utilisateur"], _configuration["Smtp:Utilisateur"]);

    /// <summary>
    /// Dépose <paramref name="message"/> (déjà envoyé) dans le dossier des envoyés. Lève une exception en cas d'échec : l'appelant
    /// la trace sans toucher à l'email. Une connexion réutilisée qui s'avère coupée est rouverte une fois.
    /// </summary>
    public async Task DeposerAsync(MimeMessage message, CancellationToken annulation = default)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(Delai);
        await _connexion.WaitAsync(delai.Token);
        try
        {
            var reutilisee = await OuvrirAsync(delai.Token);
            try
            {
                await DeposerDansDossierAsync(message, delai.Token);
            }
            catch (Exception ex) when (reutilisee && (ex is IOException or ImapProtocolException or ServiceNotConnectedException)
                                       && !delai.IsCancellationRequested)
            {
                // Session fermée côté serveur depuis le dernier email : nouvelle connexion, un seul nouvel essai.
                _logger.LogInformation("Connexion IMAP réutilisée coupée ({Erreur}) : reconnexion", ex.GetType().Name);
                await FermerAsync();
                await OuvrirAsync(delai.Token);
                await DeposerDansDossierAsync(message, delai.Token);
            }
            _derniereUtilisation = DateTime.UtcNow;
        }
        catch
        {
            // État de la connexion incertain après une erreur : la prochaine copie repart d'une connexion neuve.
            await FermerAsync();
            throw;
        }
        finally
        {
            _connexion.Release();
        }
    }

    private async Task DeposerDansDossierAsync(MimeMessage message, CancellationToken annulation)
    {
        var requete = new AppendRequest(message, MessageFlags.Seen);
        if (message.Date != default)
        {
            requete.InternalDate = message.Date;
        }
        await _dossier!.AppendAsync(requete, annulation);
    }

    /// <summary>Connexion et dossier prêts ; true si une connexion existante a été réutilisée.</summary>
    private async Task<bool> OuvrirAsync(CancellationToken annulation)
    {
        var hote = _configuration["Imap:Host"]?.Trim();
        if (string.IsNullOrEmpty(hote))
        {
            throw new InvalidOperationException("IMAP non configuré (Imap:Host vide) alors que Imap:Actif = true");
        }
        var port = Port;
        var securite = Securite;
        var utilisateur = Utilisateur;
        var dossierConfigure = _configuration["Imap:Dossier"]?.Trim() ?? "";
        // Clé sans mot de passe : un changement de réglage (hôte, compte, dossier…) impose une nouvelle connexion.
        var cle = $"{hote}|{port}|{securite}|{utilisateur}|{dossierConfigure}";
        if (_client is { IsConnected: true, IsAuthenticated: true } && _dossier is not null && cle == _cleReglages
            && DateTime.UtcNow - _derniereUtilisation < InactiviteMax)
        {
            return true;
        }
        await FermerAsync();

        var client = new ImapClient { Timeout = (int)Delai.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(hote, port, OptionSocket(securite, port), annulation);
            if (!string.IsNullOrWhiteSpace(utilisateur))
            {
                // Mot de passe jamais journalisé : une erreur d'authentification ne cite que la réponse du serveur.
                await client.AuthenticateAsync(utilisateur.Trim(),
                    Premier(_configuration["Imap:MotDePasse"], _configuration["Smtp:MotDePasse"]) ?? "", annulation);
            }
            _dossier = await TrouverDossierAsync(client, dossierConfigure, annulation);
            _client = client;
            _cleReglages = cle;
            _derniereUtilisation = DateTime.UtcNow;
            return false;
        }
        catch
        {
            _dossier = null;
            client.Dispose();
            throw;
        }
    }

    private async Task<IMailFolder> TrouverDossierAsync(ImapClient client, string dossierConfigure, CancellationToken annulation)
    {
        if (dossierConfigure.Length > 0)
        {
            try
            {
                return await client.GetFolderAsync(dossierConfigure, annulation);
            }
            catch (FolderNotFoundException)
            {
                throw new InvalidOperationException($"Dossier IMAP « {dossierConfigure} » (Imap:Dossier) introuvable sur le serveur");
            }
        }
        // Dossier spécial annoncé par le serveur (SPECIAL-USE, RFC 6154, ou XLIST de Gmail) : nom indifférent (Sent, Envoyés…).
        if ((client.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0
            && client.GetFolder(SpecialFolder.Sent) is { } special)
        {
            return special;
        }
        foreach (var nom in DossiersUsuels)
        {
            try
            {
                return await client.GetFolderAsync(nom, annulation);
            }
            catch (FolderNotFoundException)
            {
                // Nom suivant.
            }
        }
        _logger.LogWarning("Aucun dossier des messages envoyés trouvé sur le serveur IMAP (ni dossier spécial \\Sent, ni {Noms}) : "
            + "renseignez Imap:Dossier avec le nom exact du dossier", string.Join(", ", DossiersUsuels));
        throw new InvalidOperationException(
            $"Aucun dossier des messages envoyés trouvé (ni \\Sent, ni {string.Join(", ", DossiersUsuels)}) : renseignez Imap:Dossier");
    }

    private async Task FermerAsync()
    {
        var client = _client;
        _client = null;
        _dossier = null;
        _cleReglages = null;
        if (client is null)
        {
            return;
        }
        try
        {
            if (client.IsConnected)
            {
                using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(true, delai.Token);
            }
        }
        catch
        {
            // Connexion déjà perdue : rien à fermer proprement.
        }
        finally
        {
            client.Dispose();
        }
    }

    private static string? Premier(string? valeur, string? repli) => string.IsNullOrWhiteSpace(valeur) ? repli : valeur;

    public async ValueTask DisposeAsync()
    {
        await FermerAsync();
        _connexion.Dispose();
    }
}
