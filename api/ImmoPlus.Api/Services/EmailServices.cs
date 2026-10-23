namespace ImmoPlus.Api.Services;

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

/// <summary>Chiffrement de la connexion SMTP (Smtp:Securite).</summary>
public enum SecuriteSmtp
{
    /// <summary>Selon le port : 465 → SSL implicite, 587 → STARTTLS, 25 / 1025 → aucun, autre → STARTTLS si le serveur le propose.</summary>
    Auto,
    /// <summary>Connexion en clair (MailHog en développement, relais local).</summary>
    Aucune,
    /// <summary>TLS dès la connexion (port 465).</summary>
    SslImplicite,
    /// <summary>Connexion en clair puis STARTTLS obligatoire (port 587).</summary>
    StartTls
}

/// <summary>
/// Envoi SMTP (MailKit) appelé par la boîte d'envoi. Réglages Smtp:Host, Port, Utilisateur, MotDePasse, Securite, Expediteur,
/// NomExpediteur, relus à chaque envoi. Smtp:Host vide : rien n'est envoyé, les emails attendent dans la boîte d'envoi.
/// </summary>
public class EmailServices
{
    /// <summary>Un serveur muet ne bloque pas la boîte d'envoi (qui envoie dans une transaction) plus de 30 s.</summary>
    public static readonly TimeSpan Delai = TimeSpan.FromSeconds(30);

    private readonly IConfiguration _configuration;

    public EmailServices(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool EstConfigure => !string.IsNullOrWhiteSpace(_configuration["Smtp:Host"]);

    /// <summary>Smtp:Securite lu (Auto si vide) ; valeur inconnue refusée pour ne jamais envoyer en clair par erreur de frappe.</summary>
    public SecuriteSmtp Securite
    {
        get
        {
            var valeur = _configuration["Smtp:Securite"];
            if (string.IsNullOrWhiteSpace(valeur))
            {
                return SecuriteSmtp.Auto;
            }
            if (int.TryParse(valeur, out _) || !Enum.TryParse<SecuriteSmtp>(valeur.Trim(), true, out var securite))
            {
                throw new InvalidOperationException($"Smtp:Securite « {valeur} » inconnu : utilisez Auto, Aucune, SslImplicite ou StartTls");
            }
            return securite;
        }
    }

    public int Port => _configuration.GetValue("Smtp:Port", 25);

    /// <summary>Option MailKit correspondant au réglage et au port.</summary>
    public static SecureSocketOptions OptionSocket(SecuriteSmtp securite, int port) => securite switch
    {
        SecuriteSmtp.Aucune => SecureSocketOptions.None,
        SecuriteSmtp.SslImplicite => SecureSocketOptions.SslOnConnect,
        SecuriteSmtp.StartTls => SecureSocketOptions.StartTls,
        _ => port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            25 or 1025 => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTlsWhenAvailable
        }
    };

    public async Task EnvoyerAsync(string destinataire, string sujet, string corpsHtml)
    {
        var hote = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(hote))
        {
            throw new InvalidOperationException("SMTP non configuré (Smtp:Host vide)");
        }
        // Adresse sans « @ » (MimeKit l'accepterait comme boîte locale) : échec explicite plutôt qu'un envoi vers nulle part.
        if (!MailboxAddress.TryParse(destinataire, out var adresse) || !adresse.Address.Contains('@'))
        {
            throw new FormatException("Adresse email du destinataire invalide");
        }

        var message = new MimeMessage();
        var nomExpediteur = _configuration["Smtp:NomExpediteur"];
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(nomExpediteur) ? "ImmoPlus" : nomExpediteur,
            _configuration["Smtp:Expediteur"] is { Length: > 0 } expediteur ? expediteur : "no-reply@immoplus.local"));
        message.To.Add(adresse);
        message.Subject = sujet;
        message.Body = new BodyBuilder { HtmlBody = corpsHtml }.ToMessageBody();

        var port = Port;
        using var delai = new CancellationTokenSource(Delai);
        using var client = new SmtpClient { Timeout = (int)Delai.TotalMilliseconds };
        await client.ConnectAsync(hote.Trim(), port, OptionSocket(Securite, port), delai.Token);
        var utilisateur = _configuration["Smtp:Utilisateur"];
        if (!string.IsNullOrWhiteSpace(utilisateur))
        {
            // Mot de passe jamais journalisé : une erreur d'authentification ne cite que la réponse du serveur.
            await client.AuthenticateAsync(utilisateur.Trim(), _configuration["Smtp:MotDePasse"] ?? "", delai.Token);
        }
        await client.SendAsync(message, delai.Token);
        await client.DisconnectAsync(true, delai.Token);
    }
}
