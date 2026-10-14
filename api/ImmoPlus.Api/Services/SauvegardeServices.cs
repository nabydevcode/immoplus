namespace ImmoPlus.Api.Services;

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;

/// <summary>
/// Sauvegardes de la base SQLite par « VACUUM INTO » : copie cohérente à chaud (compatible WAL), sans arrêter l'API.
/// Fichiers « immoplus-yyyyMMdd-HHmm.db » dans Sauvegardes:Dossier (défaut : « Sauvegardes » à côté de la base) ;
/// seules les Sauvegardes:Conserver plus récentes (14 par défaut) sont gardées.
/// </summary>
public partial class SauvegardeServices
{
    // Planification et route admin peuvent se croiser : une seule sauvegarde à la fois.
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environnement;
    private readonly ILogger<SauvegardeServices> _logger;

    public SauvegardeServices(IConfiguration configuration, IHostEnvironment environnement, ILogger<SauvegardeServices> logger)
    {
        _configuration = configuration;
        _environnement = environnement;
        _logger = logger;
    }

    private string ChaineConnexion => _configuration.GetConnectionString("Default") ?? "Data Source=immoplus.db";

    /// <summary>Fichier de la base (chemin relatif résolu comme le fait SQLite : depuis le dossier courant).</summary>
    private string CheminBase => Path.GetFullPath(new SqliteConnectionStringBuilder(ChaineConnexion).DataSource);

    public string Dossier
    {
        get
        {
            var configure = _configuration["Sauvegardes:Dossier"];
            var dossierBase = Path.GetDirectoryName(CheminBase) ?? _environnement.ContentRootPath;
            return string.IsNullOrWhiteSpace(configure)
                ? Path.Combine(dossierBase, "Sauvegardes")
                : Path.GetFullPath(configure, dossierBase);
        }
    }

    /// <summary>Base dans un fichier (pas « :memory: » ni Mode=Memory, ex. tests) : sinon rien à sauvegarder.</summary>
    public bool BaseEnFichier
    {
        get
        {
            var chaine = new SqliteConnectionStringBuilder(ChaineConnexion);
            return chaine.Mode != SqliteOpenMode.Memory && !string.IsNullOrWhiteSpace(chaine.DataSource)
                && !chaine.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
        }
    }

    private int Conserver => Math.Max(1, _configuration.GetValue("Sauvegardes:Conserver", 14));

    public List<SauvegardeDto> Lister()
    {
        if (!Directory.Exists(Dossier))
        {
            return [];
        }
        return new DirectoryInfo(Dossier).GetFiles("immoplus-*.db")
            .Where(f => NomValide().IsMatch(f.Name))
            .OrderByDescending(f => f.Name)
            .Select(f => new SauvegardeDto { Nom = f.Name, Taille = f.Length, Date = f.LastWriteTimeUtc })
            .ToList();
    }

    /// <summary>Date (UTC) de la sauvegarde la plus récente, null s'il n'y en a pas.</summary>
    public DateTime? DerniereSauvegarde() => Lister().Select(s => (DateTime?)s.Date).FirstOrDefault();

    public async Task<SauvegardeDto> CreerAsync(CancellationToken annulation = default)
    {
        if (!BaseEnFichier)
        {
            throw new InvalidOperationException("La base n'est pas stockée dans un fichier : aucune sauvegarde possible");
        }
        await Verrou.WaitAsync(annulation);
        try
        {
            Directory.CreateDirectory(Dossier);
            var nom = $"immoplus-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.db";
            var destination = Path.Combine(Dossier, nom);
            // VACUUM INTO refuse un fichier existant : on écrit dans un temporaire, remplacé ensuite d'un seul coup
            // (deux sauvegardes dans la même minute : la seconde remplace la première).
            var temporaire = Path.Combine(Dossier, $".{nom}.{Guid.NewGuid():N}.tmp");
            try
            {
                // Connexion hors pool : rien ne reste ouvert après la sauvegarde.
                var chaine = new SqliteConnectionStringBuilder(ChaineConnexion) { Pooling = false }.ToString();
                await using (var connexion = new SqliteConnection(chaine))
                {
                    await connexion.OpenAsync(annulation);
                    await using var commande = connexion.CreateCommand();
                    commande.CommandText = "VACUUM INTO $chemin";
                    commande.Parameters.AddWithValue("$chemin", temporaire);
                    await commande.ExecuteNonQueryAsync(annulation);
                }
                File.Move(temporaire, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaire))
                {
                    File.Delete(temporaire);
                }
            }

            foreach (var ancienne in Lister().Skip(Conserver))
            {
                File.Delete(Path.Combine(Dossier, ancienne.Nom));
            }

            var fichier = new FileInfo(destination);
            _logger.LogInformation("Sauvegarde de la base créée : {Nom} ({Taille} octets)", nom, fichier.Length);
            return new SauvegardeDto { Nom = nom, Taille = fichier.Length, Date = fichier.LastWriteTimeUtc };
        }
        finally
        {
            Verrou.Release();
        }
    }

    /// <summary>Chemin d'une sauvegarde à télécharger : nom strictement contrôlé (aucun chemin, aucun autre fichier).</summary>
    public string Chemin(string nom)
    {
        if (string.IsNullOrEmpty(nom) || !NomValide().IsMatch(nom))
        {
            throw new NotFoundException("Cette sauvegarde n'existe pas");
        }
        var chemin = Path.Combine(Dossier, nom);
        if (!File.Exists(chemin))
        {
            throw new NotFoundException("Cette sauvegarde n'existe pas");
        }
        return chemin;
    }

    [GeneratedRegex(@"^immoplus-\d{8}-\d{4}\.db$")]
    private static partial Regex NomValide();
}

/// <summary>
/// Sauvegarde au démarrage puis toutes les 24 h (Sauvegardes:IntervalleHeures). Au démarrage, si la dernière sauvegarde a
/// moins de 24 h (redémarrages fréquents, ex. dotnet watch), on attend son échéance au lieu d'en refaire une.
/// Désactivable par Sauvegardes:Actif = false. Une erreur est journalisée et n'arrête pas l'API.
/// </summary>
public class SauvegardeBaseService : BackgroundService
{
    private readonly SauvegardeServices _sauvegardes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SauvegardeBaseService> _logger;

    public SauvegardeBaseService(SauvegardeServices sauvegardes, IConfiguration configuration, ILogger<SauvegardeBaseService> logger)
    {
        _sauvegardes = sauvegardes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Sauvegardes:Actif", true) || !_sauvegardes.BaseEnFichier)
        {
            _logger.LogInformation("Sauvegardes automatiques désactivées (Sauvegardes:Actif = false ou base en mémoire)");
            return;
        }
        var intervalle = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("Sauvegardes:IntervalleHeures", 24.0)));
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var attente = intervalle;
            try
            {
                var derniere = _sauvegardes.DerniereSauvegarde();
                var depuis = derniere is null ? TimeSpan.MaxValue : DateTime.UtcNow - derniere.Value;
                if (depuis >= intervalle)
                {
                    await _sauvegardes.CreerAsync(stoppingToken);
                }
                else
                {
                    attente = intervalle - depuis;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la sauvegarde automatique de la base");
                attente = TimeSpan.FromHours(1);
            }

            try
            {
                await Task.Delay(attente, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
