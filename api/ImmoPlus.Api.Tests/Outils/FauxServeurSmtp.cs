namespace ImmoPlus.Api.Tests.Outils;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

/// <summary>Email capturé par le faux serveur SMTP (rien ne sort de la machine, MailHog n'est jamais contacté).</summary>
public sealed record EmailCapture(string Destinataire, string Sujet, string CorpsHtml);

/// <summary>
/// Serveur SMTP minimal en mémoire, sur un port libre de la boucle locale : l'API y envoie ses emails comme à un vrai serveur,
/// les tests lisent ensuite ce qui a été « envoyé ». Suffisant pour le client MailKit de l'API (EHLO, AUTH PLAIN, MAIL, RCPT, DATA, QUIT), sans TLS.
/// </summary>
public sealed class FauxServeurSmtp : IAsyncDisposable
{
    private readonly TcpListener _ecoute = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _arret = new();
    private readonly ConcurrentQueue<EmailCapture> _emails = new();
    private readonly Task _boucle;

    public FauxServeurSmtp()
    {
        _ecoute.Start();
        Port = ((IPEndPoint)_ecoute.LocalEndpoint).Port;
        _boucle = Task.Run(AccepterAsync);
    }

    public int Port { get; }

    private int _refusAVenir;

    /// <summary>Les <paramref name="nombre"/> prochains envois sont refusés (451, panne passagère du serveur) : nouvelles tentatives.</summary>
    public void RefuserProchainsEnvois(int nombre) => Interlocked.Exchange(ref _refusAVenir, nombre);

    public IReadOnlyList<EmailCapture> Emails => _emails.ToArray();

    /// <summary>Renseigné : AUTH PLAIN proposé et exigé avant MAIL FROM (530 sinon, 535 si identifiants faux).</summary>
    public (string Utilisateur, string MotDePasse)? IdentifiantsExiges { get; set; }

    private int _authentificationsReussies;

    public int AuthentificationsReussies => Volatile.Read(ref _authentificationsReussies);

    public IReadOnlyList<EmailCapture> EmailsPour(string destinataire) =>
        _emails.Where(e => string.Equals(e.Destinataire, destinataire, StringComparison.OrdinalIgnoreCase)).ToList();

    private async Task AccepterAsync()
    {
        while (!_arret.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _ecoute.AcceptTcpClientAsync(_arret.Token);
            }
            catch
            {
                return;
            }
            _ = Task.Run(() => DialoguerAsync(client));
        }
    }

    private async Task DialoguerAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var flux = client.GetStream();
            using var lecteur = new StreamReader(flux, Encoding.ASCII);
            await using var ecrivain = new StreamWriter(flux, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await ecrivain.WriteLineAsync("220 faux-smtp.immoplus.test");
            var destinataires = new List<string>();
            var identifiants = IdentifiantsExiges;
            var authentifie = identifiants is null;
            while (await lecteur.ReadLineAsync() is { } ligne)
            {
                var commande = ligne.ToUpperInvariant();
                if (commande.StartsWith("EHLO") || commande.StartsWith("HELO"))
                {
                    if (identifiants is not null)
                    {
                        await ecrivain.WriteLineAsync("250-faux-smtp.immoplus.test");
                        await ecrivain.WriteLineAsync("250 AUTH PLAIN");
                    }
                    else
                    {
                        await ecrivain.WriteLineAsync("250 faux-smtp.immoplus.test");
                    }
                }
                else if (commande.StartsWith("AUTH PLAIN"))
                {
                    var reponse = ligne.Length > "AUTH PLAIN ".Length ? ligne["AUTH PLAIN ".Length..].Trim() : "";
                    if (reponse.Length == 0)
                    {
                        await ecrivain.WriteLineAsync("334 ");
                        reponse = (await lecteur.ReadLineAsync() ?? "").Trim();
                    }
                    // « \0utilisateur\0mot de passe » en base 64 (RFC 4616).
                    var parties = Encoding.UTF8.GetString(Convert.FromBase64String(reponse)).Split('\0');
                    if (identifiants is { } attendu && parties.Length == 3 && parties[1] == attendu.Utilisateur && parties[2] == attendu.MotDePasse)
                    {
                        authentifie = true;
                        Interlocked.Increment(ref _authentificationsReussies);
                        await ecrivain.WriteLineAsync("235 Authentification reussie");
                    }
                    else
                    {
                        await ecrivain.WriteLineAsync("535 Identifiants refuses");
                    }
                }
                else if (commande.StartsWith("MAIL FROM"))
                {
                    destinataires.Clear();
                    if (!authentifie)
                    {
                        await ecrivain.WriteLineAsync("530 Authentification requise");
                        continue;
                    }
                    if (Interlocked.Decrement(ref _refusAVenir) >= 0)
                    {
                        await ecrivain.WriteLineAsync("451 Service momentanement indisponible");
                        continue;
                    }
                    Interlocked.Exchange(ref _refusAVenir, 0);
                    await ecrivain.WriteLineAsync("250 OK");
                }
                else if (commande.StartsWith("RCPT TO"))
                {
                    destinataires.Add(ligne[(ligne.IndexOf(':') + 1)..].Trim().Trim('<', '>'));
                    await ecrivain.WriteLineAsync("250 OK");
                }
                else if (commande == "DATA")
                {
                    await ecrivain.WriteLineAsync("354 Fin par <CRLF>.<CRLF>");
                    var brut = new StringBuilder();
                    while (await lecteur.ReadLineAsync() is { } donnee && donnee != ".")
                    {
                        brut.Append(donnee.StartsWith("..") ? donnee[1..] : donnee).Append("\r\n");
                    }
                    Enregistrer(destinataires, brut.ToString());
                    await ecrivain.WriteLineAsync("250 OK");
                }
                else if (commande == "QUIT")
                {
                    await ecrivain.WriteLineAsync("221 Au revoir");
                    return;
                }
                else
                {
                    await ecrivain.WriteLineAsync("250 OK");
                }
            }
        }
        catch
        {
            // Connexion coupée : l'API journalise l'échec d'envoi, le test le verra par l'absence d'email.
        }
    }

    private void Enregistrer(List<string> destinataires, string brut)
    {
        var message = MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(brut)));
        var corps = message.HtmlBody ?? message.TextBody ?? "";
        foreach (var destinataire in destinataires)
        {
            _emails.Enqueue(new EmailCapture(destinataire, message.Subject ?? "", corps));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _arret.Cancel();
        _ecoute.Stop();
        try { await _boucle; } catch { /* arrêt */ }
        _arret.Dispose();
    }
}
