namespace ImmoPlus.Api.Services;

using System.Text.RegularExpressions;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;

/// <summary>
/// Sauvegardes de la base PostgreSQL : produites hors de l'API par le service docker compose « sauvegarde-db »
/// (pg_dump -Fc quotidien, rétention 14 jours, restauration par infra/restaurer-postgres.sh). L'API se contente de les
/// lister et de les proposer au téléchargement à l'admin ; elle n'en crée ni n'en supprime aucune.
/// Dossier : Sauvegardes:Dossier (défaut : « Sauvegardes/postgres » sous la racine de l'API, partagé avec sauvegarde-db).
/// </summary>
public partial class SauvegardeServices
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environnement;

    public SauvegardeServices(IConfiguration configuration, IHostEnvironment environnement)
    {
        _configuration = configuration;
        _environnement = environnement;
    }

    public string Dossier
    {
        get
        {
            var configure = _configuration["Sauvegardes:Dossier"];
            return string.IsNullOrWhiteSpace(configure)
                ? Path.Combine(_environnement.ContentRootPath, "Sauvegardes", "postgres")
                : Path.GetFullPath(configure, _environnement.ContentRootPath);
        }
    }

    /// <summary>Sauvegardes terminées, de la plus récente à la plus ancienne (les « .tmp » en cours d'écriture sont ignorés).</summary>
    public List<SauvegardeDto> Lister()
    {
        if (!Directory.Exists(Dossier))
        {
            return [];
        }
        return new DirectoryInfo(Dossier).GetFiles("immoplus-*.dump")
            .Where(f => NomValide().IsMatch(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name)
            .Select(f => new SauvegardeDto { Nom = f.Name, Taille = f.Length, Date = f.LastWriteTimeUtc })
            .ToList();
    }

    /// <summary>Création à la demande non prise en charge par l'API (pas de pg_dump dans son image) : 409.</summary>
    public static SauvegardeDto Creer() =>
        throw new ConflictException("Sauvegarde gérée par le serveur : une sauvegarde automatique est faite chaque jour");

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

    // « immoplus-AAAAMMJJ-HHMM.dump » (quotidienne) ou « immoplus-avant-restauration-AAAAMMJJ-HHMMSS.dump » (filet de sécurité).
    [GeneratedRegex(@"^immoplus-(avant-restauration-)?\d{8}-\d{4}(\d{2})?\.dump$")]
    private static partial Regex NomValide();
}
