namespace ImmoPlus.Api.Services;

using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

/// <summary>
/// Fichiers envoyés par les utilisateurs (photos, miniatures, documents, états des lieux, pièces d'identité).
/// Les clés reprennent les chemins relatifs historiques sous Storage/ (« photos/12/abc.jpg », « documents/48/def.pdf »…) :
/// les colonnes Chemin / CheminStockage de la base restent valables quel que soit le mode (Stockage:Mode = Disque ou S3).
/// </summary>
public interface IStockageFichiers
{
    /// <summary>Écrit (ou remplace) le fichier ; jamais de fichier à moitié écrit visible par un lecteur.</summary>
    Task EnregistrerAsync(string cle, Stream flux, string typeMime, CancellationToken annulation = default);

    /// <summary>Flux en lecture (à libérer par l'appelant), ou null si le fichier n'existe pas.</summary>
    Task<Stream?> OuvrirAsync(string cle, CancellationToken annulation = default);

    /// <summary>Supprime le fichier s'il existe (aucune erreur s'il est déjà absent).</summary>
    Task SupprimerAsync(string cle, CancellationToken annulation = default);

    Task<bool> ExisteAsync(string cle, CancellationToken annulation = default);
}

/// <summary>Construction et contrôle des clés : séparateur « / », aucun « .. » ni chemin absolu (pas de sortie de Storage/).</summary>
public static class ClesStockage
{
    public const string Photos = "photos";
    public const string Miniatures = "miniatures";
    public const string Documents = "documents";
    public const string EtatsDesLieux = "etats-des-lieux";
    public const string Identites = "identites";

    /// <summary>Clé d'un fichier : préfixe + chemin relatif tel qu'enregistré en base (« 12/abc.jpg », ou « 12\abc.jpg » d'un ancien Windows).</summary>
    public static string Cle(string prefixe, string cheminRelatif) => Valider($"{prefixe}/{cheminRelatif}");

    public static string Valider(string cle)
    {
        var normalisee = (cle ?? "").Replace('\\', '/');
        var segments = normalisee.Split('/');
        if (normalisee.Length == 0 || normalisee.Length > 512 || normalisee.StartsWith('/')
            || segments.Any(s => s.Length == 0 || s == "." || s == "..") || normalisee.Any(char.IsControl))
        {
            throw new InvalidOperationException("Chemin de fichier invalide");
        }
        return normalisee;
    }
}

/// <summary>Mode Disque (défaut, une seule instance) : fichiers sous Stockage:Disque:Dossier ({ContentRoot}/Storage par défaut).</summary>
public class StockageDisque : IStockageFichiers
{
    private readonly string _racine;

    public StockageDisque(string racine)
    {
        _racine = Path.GetFullPath(racine);
    }

    public string Racine => _racine;

    /// <summary>Stockage:Disque:Dossier (absolu, ou relatif à la racine de l'API) ; défaut : {racine de l'API}/Storage.</summary>
    public static string DossierConfigure(IConfiguration configuration, string racineContenu)
    {
        var dossier = configuration["Stockage:Disque:Dossier"];
        return Path.GetFullPath(string.IsNullOrWhiteSpace(dossier)
            ? Path.Combine(racineContenu, "Storage")
            : Path.Combine(racineContenu, dossier.Trim()));
    }

    private string Chemin(string cle)
    {
        var complet = Path.GetFullPath(Path.Combine(_racine, ClesStockage.Valider(cle)));
        // Seconde barrière (liens, séparateurs exotiques) : le fichier doit rester sous la racine.
        if (!complet.StartsWith(_racine + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chemin de fichier invalide");
        }
        return complet;
    }

    public async Task EnregistrerAsync(string cle, Stream flux, string typeMime, CancellationToken annulation = default)
    {
        var chemin = Chemin(cle);
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        // Écrit à côté puis renommé : un lecteur ne voit jamais un fichier incomplet.
        var temporaire = $"{chemin}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var cible = new FileStream(temporaire, FileMode.CreateNew, FileAccess.Write))
            {
                await flux.CopyToAsync(cible, annulation);
            }
            File.Move(temporaire, chemin, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaire))
            {
                File.Delete(temporaire);
            }
        }
    }

    public Task<Stream?> OuvrirAsync(string cle, CancellationToken annulation = default)
    {
        var chemin = Chemin(cle);
        try
        {
            return Task.FromResult<Stream?>(new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task SupprimerAsync(string cle, CancellationToken annulation = default)
    {
        File.Delete(Chemin(cle)); // sans erreur si le fichier n'existe pas
        return Task.CompletedTask;
    }

    public Task<bool> ExisteAsync(string cle, CancellationToken annulation = default) => Task.FromResult(File.Exists(Chemin(cle)));
}

/// <summary>Réglages Stockage:S3 (MinIO en local : Url http://minio:9000, CheminStyle = true).</summary>
public class OptionsStockageS3
{
    public string Url { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string CleAcces { get; set; } = "";
    public string CleSecrete { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    /// <summary>Adresses « http://hote/bucket/cle » (MinIO) plutôt que « http://bucket.hote/cle ».</summary>
    public bool CheminStyle { get; set; } = true;

    public static OptionsStockageS3 Lire(IConfiguration configuration) =>
        configuration.GetSection("Stockage:S3").Get<OptionsStockageS3>() ?? new OptionsStockageS3();

    public AmazonS3Client CreerClient()
    {
        if (string.IsNullOrWhiteSpace(Url) || string.IsNullOrWhiteSpace(Bucket) || string.IsNullOrWhiteSpace(CleAcces) || string.IsNullOrWhiteSpace(CleSecrete))
        {
            throw new InvalidOperationException("Stockage:Mode = S3 mais Stockage:S3 (Url, Bucket, CleAcces, CleSecrete) est incomplet");
        }
        var config = new AmazonS3Config
        {
            ServiceURL = Url,
            ForcePathStyle = CheminStyle,
            AuthenticationRegion = Region,
            // Sommes de contrôle CRC envoyées seulement si l'opération l'exige : compatibilité avec MinIO et les autres S3.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 3
        };
        return new AmazonS3Client(new BasicAWSCredentials(CleAcces, CleSecrete), config);
    }
}

/// <summary>
/// Mode S3 (plusieurs instances) : bucket privé, l'API reste le seul point d'accès aux fichiers (mêmes règles de droits).
/// Client AWSSDK.S3 (Apache-2.0), compatible MinIO et tout service S3.
/// </summary>
public class StockageS3 : IStockageFichiers, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public StockageS3(IAmazonS3 client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    public string Bucket => _bucket;

    public async Task EnregistrerAsync(string cle, Stream flux, string typeMime, CancellationToken annulation = default)
    {
        // Taille connue d'avance (pas d'envoi « chunked ») : fichiers de 15 Mo au plus, lus en mémoire si le flux n'est pas positionnable.
        Stream contenu = flux;
        MemoryStream? copie = null;
        if (!flux.CanSeek)
        {
            copie = new MemoryStream();
            await flux.CopyToAsync(copie, annulation);
            copie.Position = 0;
            contenu = copie;
        }
        try
        {
            // Un objet S3 n'apparaît qu'une fois entièrement reçu : pas de fichier à moitié écrit.
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = ClesStockage.Valider(cle),
                InputStream = contenu,
                ContentType = typeMime,
                AutoCloseStream = false
            }, annulation);
        }
        finally
        {
            copie?.Dispose();
        }
    }

    public async Task<Stream?> OuvrirAsync(string cle, CancellationToken annulation = default)
    {
        try
        {
            var reponse = await _client.GetObjectAsync(_bucket, ClesStockage.Valider(cle), annulation);
            return new FluxObjetS3(reponse);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SupprimerAsync(string cle, CancellationToken annulation = default) =>
        await _client.DeleteObjectAsync(_bucket, ClesStockage.Valider(cle), annulation);

    public async Task<bool> ExisteAsync(string cle, CancellationToken annulation = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, ClesStockage.Valider(cle), annulation);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Flux de l'objet qui libère aussi la réponse HTTP (connexion rendue au pool) à sa fermeture.</summary>
    private sealed class FluxObjetS3(GetObjectResponse reponse) : Stream
    {
        private readonly Stream _flux = reponse.ResponseStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => reponse.ContentLength;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _flux.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _flux.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _flux.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flux.Dispose();
                reponse.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
