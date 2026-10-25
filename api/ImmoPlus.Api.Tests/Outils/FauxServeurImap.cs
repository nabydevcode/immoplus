namespace ImmoPlus.Api.Tests.Outils;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

/// <summary>Message déposé par IMAP APPEND dans le faux serveur.</summary>
public sealed record DepotImap(string Dossier, string Drapeaux, MimeMessage Message);

/// <summary>
/// Serveur IMAP minimal en mémoire, sur un port libre de la boucle locale (sans TLS) : ce qu'il faut au client MailKit de l'API
/// pour copier un email dans « Envoyés » (CAPABILITY, AUTHENTICATE PLAIN / LOGIN, LIST avec ou sans SPECIAL-USE, APPEND avec
/// littéral synchronisant, NOOP, LOGOUT). Les tests choisissent les dossiers, provoquent des refus ou des coupures et lisent les dépôts.
/// </summary>
public sealed partial class FauxServeurImap : IAsyncDisposable
{
    private const char Separateur = '.';
    private readonly TcpListener _ecoute = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _arret = new();
    private readonly ConcurrentQueue<DepotImap> _depots = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly Task _boucle;
    private int _connexions;
    private int _refusAVenir;

    /// <param name="dossiers">Dossiers existants ; « \Sent » en second élément : dossier spécial annoncé si SPECIAL-USE est proposé.</param>
    public FauxServeurImap(bool specialUse, params (string Nom, string? Attribut)[] dossiers)
    {
        SpecialUse = specialUse;
        Dossiers = dossiers.Length > 0 ? dossiers : [("INBOX", null), ("Sent", @"\Sent")];
        _ecoute.Start();
        Port = ((IPEndPoint)_ecoute.LocalEndpoint).Port;
        _boucle = Task.Run(AccepterAsync);
    }

    public int Port { get; }

    public bool SpecialUse { get; }

    public (string Nom, string? Attribut)[] Dossiers { get; }

    /// <summary>Renseigné : identifiants exigés (NO sinon).</summary>
    public (string Utilisateur, string MotDePasse)? IdentifiantsExiges { get; set; }

    public int Connexions => Volatile.Read(ref _connexions);

    public IReadOnlyList<DepotImap> Depots => _depots.ToArray();

    /// <summary>Les <paramref name="nombre"/> prochains APPEND sont refusés (NO) : la copie échoue, l'email est déjà parti.</summary>
    public void RefuserProchainsDepots(int nombre) => Interlocked.Exchange(ref _refusAVenir, nombre);

    /// <summary>Coupe toutes les connexions ouvertes sans prévenir (session expirée côté serveur).</summary>
    public void CouperConnexions()
    {
        foreach (var client in _clients.Keys)
        {
            client.Client.Close();
        }
    }

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
            Interlocked.Increment(ref _connexions);
            _clients[client] = 0;
            _ = Task.Run(() => DialoguerAsync(client));
        }
    }

    private string Capacites => "IMAP4rev1 AUTH=PLAIN" + (SpecialUse ? " SPECIAL-USE" : "");

    private async Task DialoguerAsync(TcpClient client)
    {
        try
        {
            var flux = client.GetStream();
            var lecteur = new LecteurOctets(flux);
            async Task Ecrire(string ligne) => await flux.WriteAsync(Encoding.UTF8.GetBytes(ligne + "\r\n"));

            await Ecrire($"* OK [CAPABILITY {Capacites}] Faux IMAP pret");
            var authentifie = IdentifiantsExiges is null;
            while (await lecteur.LireLigneAsync() is { } ligne)
            {
                var espace = ligne.IndexOf(' ');
                if (espace < 0)
                {
                    continue;
                }
                var etiquette = ligne[..espace];
                var reste = ligne[(espace + 1)..];
                var commande = reste.Split(' ')[0].ToUpperInvariant();
                var arguments = Jetons(reste[commande.Length..]);
                switch (commande)
                {
                    case "CAPABILITY":
                        await Ecrire($"* CAPABILITY {Capacites}");
                        await Ecrire($"{etiquette} OK CAPABILITY termine");
                        break;
                    case "AUTHENTICATE":
                        var reponse = arguments.Count > 1 ? arguments[1] : null;
                        if (reponse is null)
                        {
                            await Ecrire("+ ");
                            reponse = (await lecteur.LireLigneAsync() ?? "").Trim();
                        }
                        var parties = Encoding.UTF8.GetString(Convert.FromBase64String(reponse)).Split('\0');
                        authentifie = Verifier(parties.Length == 3 ? parties[1] : "", parties.Length == 3 ? parties[2] : "");
                        await Ecrire(authentifie ? $"{etiquette} OK Authentifie" : $"{etiquette} NO [AUTHENTICATIONFAILED] Identifiants refuses");
                        break;
                    case "LOGIN":
                        authentifie = arguments.Count == 2 && Verifier(arguments[0], arguments[1]);
                        await Ecrire(authentifie ? $"{etiquette} OK Authentifie" : $"{etiquette} NO [AUTHENTICATIONFAILED] Identifiants refuses");
                        break;
                    case "LIST":
                        if (!authentifie)
                        {
                            await Ecrire($"{etiquette} NO Authentification requise");
                            break;
                        }
                        // LIST [(options de sélection)] référence motif [RETURN (…)]
                        var liste = arguments.Where(a => !a.StartsWith('(')).ToList();
                        var indexReturn = liste.FindIndex(a => a.Equals("RETURN", StringComparison.OrdinalIgnoreCase));
                        if (indexReturn >= 0)
                        {
                            liste = liste[..indexReturn];
                        }
                        var motif = liste.Count >= 2 ? liste[0] + liste[1] : "";
                        if (motif.Length == 0)
                        {
                            await Ecrire($"* LIST (\\Noselect) \"{Separateur}\" \"\"");
                        }
                        else
                        {
                            var expression = new Regex("^" + Regex.Escape(motif).Replace(@"\*", ".*").Replace("%", "[^.]*") + "$",
                                RegexOptions.IgnoreCase);
                            foreach (var (nom, attribut) in Dossiers.Where(d => expression.IsMatch(d.Nom)))
                            {
                                var attributs = SpecialUse && attribut is not null ? $"\\HasNoChildren {attribut}" : "\\HasNoChildren";
                                await Ecrire($"* LIST ({attributs}) \"{Separateur}\" \"{nom}\"");
                            }
                        }
                        await Ecrire($"{etiquette} OK LIST termine");
                        break;
                    case "APPEND":
                        // APPEND dossier [(drapeaux)] ["date"] {taille}
                        var taille = int.Parse(TailleLitteral().Match(reste).Groups[1].Value);
                        await Ecrire("+ Pret pour le litteral");
                        var brut = await lecteur.LireOctetsAsync(taille);
                        await lecteur.LireLigneAsync();
                        if (!authentifie)
                        {
                            await Ecrire($"{etiquette} NO Authentification requise");
                            break;
                        }
                        var dossier = arguments[0];
                        if (!Dossiers.Any(d => d.Nom == dossier))
                        {
                            await Ecrire($"{etiquette} NO [TRYCREATE] Dossier inconnu");
                            break;
                        }
                        if (Interlocked.Decrement(ref _refusAVenir) >= 0)
                        {
                            await Ecrire($"{etiquette} NO [SERVERBUG] Depot momentanement impossible");
                            break;
                        }
                        Interlocked.Exchange(ref _refusAVenir, 0);
                        var drapeaux = arguments.Count > 1 && arguments[1].StartsWith('(') ? arguments[1] : "";
                        _depots.Enqueue(new DepotImap(dossier, drapeaux, MimeMessage.Load(new MemoryStream(brut))));
                        await Ecrire($"{etiquette} OK [APPENDUID 1 {_depots.Count}] APPEND termine");
                        break;
                    case "LOGOUT":
                        await Ecrire("* BYE Au revoir");
                        await Ecrire($"{etiquette} OK LOGOUT termine");
                        return;
                    default:
                        await Ecrire($"{etiquette} OK {commande} termine");
                        break;
                }
            }
        }
        catch
        {
            // Connexion coupée (volontairement par CouperConnexions ou par le client).
        }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Dispose();
        }
    }

    private bool Verifier(string utilisateur, string motDePasse) =>
        IdentifiantsExiges is not { } attendu || (utilisateur == attendu.Utilisateur && motDePasse == attendu.MotDePasse);

    /// <summary>Découpe des arguments IMAP : chaînes entre guillemets (sans les guillemets), groupes entre parenthèses, atomes.</summary>
    private static List<string> Jetons(string texte)
    {
        var jetons = new List<string>();
        var i = 0;
        while (i < texte.Length)
        {
            if (texte[i] == ' ')
            {
                i++;
            }
            else if (texte[i] == '"')
            {
                var valeur = new StringBuilder();
                i++;
                while (i < texte.Length && texte[i] != '"')
                {
                    if (texte[i] == '\\' && i + 1 < texte.Length)
                    {
                        i++;
                    }
                    valeur.Append(texte[i++]);
                }
                i++;
                jetons.Add(valeur.ToString());
            }
            else if (texte[i] == '(')
            {
                var fin = texte.IndexOf(')', i);
                fin = fin < 0 ? texte.Length - 1 : fin;
                jetons.Add(texte[i..(fin + 1)]);
                i = fin + 1;
            }
            else
            {
                var fin = texte.IndexOf(' ', i);
                fin = fin < 0 ? texte.Length : fin;
                jetons.Add(texte[i..fin]);
                i = fin;
            }
        }
        return jetons;
    }

    [GeneratedRegex(@"\{(\d+)\+?\}$")]
    private static partial Regex TailleLitteral();

    public async ValueTask DisposeAsync()
    {
        _arret.Cancel();
        _ecoute.Stop();
        CouperConnexions();
        try { await _boucle; } catch { /* arrêt */ }
        _arret.Dispose();
    }

    /// <summary>Lecture par lignes puis par octets sur le même flux (le littéral d'APPEND est binaire).</summary>
    private sealed class LecteurOctets(Stream flux)
    {
        private readonly byte[] _tampon = new byte[8192];
        private int _debut;
        private int _fin;

        private async Task<bool> RemplirAsync()
        {
            if (_debut < _fin)
            {
                return true;
            }
            _debut = 0;
            _fin = await flux.ReadAsync(_tampon);
            return _fin > 0;
        }

        public async Task<string?> LireLigneAsync()
        {
            var ligne = new List<byte>();
            while (await RemplirAsync())
            {
                var octet = _tampon[_debut++];
                if (octet == '\n')
                {
                    return Encoding.UTF8.GetString(ligne.ToArray()).TrimEnd('\r');
                }
                ligne.Add(octet);
            }
            return null;
        }

        public async Task<byte[]> LireOctetsAsync(int taille)
        {
            var resultat = new byte[taille];
            var lus = 0;
            while (lus < taille && await RemplirAsync())
            {
                var n = Math.Min(taille - lus, _fin - _debut);
                Array.Copy(_tampon, _debut, resultat, lus, n);
                _debut += n;
                lus += n;
            }
            return resultat;
        }
    }
}
