// Copie des fichiers existants d'ImmoPlus (api/ImmoPlus.Api/Storage/) vers le bucket S3 (Garage), puis contrôle.
//
//   dotnet run --project api/ImmoPlus.CopieFichiers -- --source api/ImmoPlus.Api/Storage \
//     --fichier-env infra/stockage.env --url http://localhost:9000 [--controle-seul] [--rapport copie.csv]
//
// --fichier-env : lit Stockage__S3__Bucket, __CleAcces, __CleSecrete, __Region (les secrets ne sont jamais affichés).
// --url        : remplace Stockage__S3__Url (http://garage:9000 n'est joignable que depuis Docker ; depuis le Mac : localhost:9000).
// --controle-seul : ne copie rien, compare seulement.
// --rapport    : écrit la liste clé ; taille ; SHA-256 de chaque fichier (aucun contenu).
//
// Les clés reprennent les chemins relatifs de Storage/ (photos/12/abc.jpg…), comme l'API. Les originaux ne sont jamais
// modifiés ni supprimés (lecture seule). Rejouable : un objet déjà présent avec la même taille et le même SHA-256
// (métadonnée sha256 posée à la copie) n'est pas renvoyé. Contrôle : chaque objet est RELU depuis le bucket et son
// SHA-256 recalculé, comparé à l'original. Code de sortie 0 seulement si tous les fichiers sont identiques.

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

string[] Prefixes = ["photos", "miniatures", "documents", "etats-des-lieux", "identites"];

var options = LireArguments(args);
if (options is null)
{
    Console.Error.WriteLine("Usage : --source <dossier Storage> --fichier-env <infra/stockage.env> [--url http://localhost:9000] [--bucket immoplus] [--controle-seul] [--rapport fichier.csv]");
    return 2;
}

var source = Path.GetFullPath(options.Source);
if (!Directory.Exists(source))
{
    Console.Error.WriteLine($"Dossier source introuvable : {source}");
    return 2;
}
var reglages = File.ReadAllLines(options.FichierEnv)
    .Select(l => l.Trim())
    .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
    .Select(l => (Cle: l[..l.IndexOf('=')].Trim(), Valeur: l[(l.IndexOf('=') + 1)..].Trim().Trim('"')))
    .GroupBy(r => r.Cle).ToDictionary(g => g.Key, g => g.Last().Valeur);
string Reglage(string nom) => reglages.TryGetValue(nom, out var v) && !string.IsNullOrWhiteSpace(v) && v != "a-remplir"
    ? v
    : throw new InvalidOperationException($"{nom} absent de {options.FichierEnv}");

var url = options.Url ?? Reglage("Stockage__S3__Url");
var bucket = options.Bucket ?? Reglage("Stockage__S3__Bucket");
var region = reglages.GetValueOrDefault("Stockage__S3__Region") is { Length: > 0 } r ? r : "us-east-1";
using var s3 = new AmazonS3Client(new BasicAWSCredentials(Reglage("Stockage__S3__CleAcces"), Reglage("Stockage__S3__CleSecrete")), new AmazonS3Config
{
    ServiceURL = url,
    ForcePathStyle = true,
    AuthenticationRegion = region,
    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    Timeout = TimeSpan.FromSeconds(60)
});

Console.WriteLine($"Source : {source} (lecture seule)");
Console.WriteLine($"Cible  : {url}, bucket « {bucket} » (région {region})");

// 1. Inventaire local : SHA-256 et taille de chaque fichier (ni fichiers cachés ni fichiers temporaires .tmp).
var ignores = new List<string>();
var fichiers = new List<Fichier>();
foreach (var chemin in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var cle = Path.GetRelativePath(source, chemin).Replace('\\', '/');
    var nom = Path.GetFileName(chemin);
    if (nom.StartsWith('.') || nom.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || !Prefixes.Contains(cle.Split('/')[0])
        || cle.Split('/').Any(s => s is "" or "." or ".."))
    {
        ignores.Add(cle);
        continue;
    }
    await using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read);
    var (sha, taille) = await Empreinte(flux);
    fichiers.Add(new Fichier(cle, chemin, taille, sha));
}
Console.WriteLine($"Inventaire : {fichiers.Count} fichier(s), {Octets(fichiers.Sum(f => f.Taille))} ; ignorés : {ignores.Count} ({string.Join(", ", ignores)})");
foreach (var groupe in fichiers.GroupBy(f => f.Cle.Split('/')[0]))
{
    Console.WriteLine($"  {groupe.Key,-16} {groupe.Count(),5} fichier(s)  {Octets(groupe.Sum(f => f.Taille))}");
}

// 2. Copie (rejouable, sans doublon : même clé, contenu déjà identique → ignoré).
if (!options.ControleSeul)
{
    int copies = 0, dejaPresents = 0;
    foreach (var f in fichiers)
    {
        var existant = await Metadonnees(s3, bucket, f.Cle);
        if (existant is { } m && m.ContentLength == f.Taille && m.Metadata["sha256"] == f.Sha256)
        {
            dejaPresents++;
            continue;
        }
        await using var flux = new FileStream(f.Chemin, FileMode.Open, FileAccess.Read, FileShare.Read);
        var requete = new PutObjectRequest
        {
            BucketName = bucket,
            Key = f.Cle,
            InputStream = flux,
            ContentType = TypeMime(f.Cle),
            AutoCloseStream = false
        };
        requete.Metadata.Add("sha256", f.Sha256);
        await s3.PutObjectAsync(requete);
        copies++;
    }
    Console.WriteLine($"Copie : {copies} objet(s) envoyé(s), {dejaPresents} déjà présent(s) et identique(s)");
}

// 3. Contrôle : chaque objet relu depuis le bucket, SHA-256 recalculé.
int identiques = 0;
var anomalies = new List<string>();
foreach (var f in fichiers)
{
    try
    {
        using var objet = await s3.GetObjectAsync(bucket, f.Cle);
        await using var flux = objet.ResponseStream;
        var (sha, taille) = await Empreinte(flux);
        if (sha == f.Sha256 && taille == f.Taille)
        {
            identiques++;
        }
        else
        {
            anomalies.Add($"DIFFÉRENT  {f.Cle} (disque {f.Taille} o, bucket {taille} o)");
        }
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        anomalies.Add($"MANQUANT   {f.Cle}");
    }
}
// Objets du bucket absents du disque (ex. fichiers envoyés après le passage de l'API en S3) : signalés, pas une erreur.
var surDisque = fichiers.Select(f => f.Cle).ToHashSet(StringComparer.Ordinal);
var objetsBucket = new List<(string Cle, long Taille)>();
foreach (var prefixe in Prefixes)
{
    string? suite = null;
    do
    {
        var page = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefixe + "/", ContinuationToken = suite });
        objetsBucket.AddRange((page.S3Objects ?? []).Select(o => (o.Key, o.Size ?? 0)));
        suite = page.IsTruncated == true ? page.NextContinuationToken : null;
    } while (suite is not null);
}
var enPlus = objetsBucket.Where(o => !surDisque.Contains(o.Cle)).ToList();

Console.WriteLine();
Console.WriteLine($"Contrôle : {identiques}/{fichiers.Count} fichier(s) identique(s) (taille et SHA-256 relus depuis le bucket)");
Console.WriteLine($"           disque {fichiers.Count} fichier(s) / {Octets(fichiers.Sum(f => f.Taille))} ; " +
    $"bucket {objetsBucket.Count} objet(s) / {Octets(objetsBucket.Sum(o => o.Taille))} sous {string.Join(", ", Prefixes)}");
if (enPlus.Count > 0)
{
    Console.WriteLine($"           {enPlus.Count} objet(s) du bucket absent(s) du disque (envoyés depuis le passage en S3 ?) : {string.Join(", ", enPlus.Take(20).Select(o => o.Cle))}");
}
anomalies.ForEach(a => Console.WriteLine("  " + a));

if (options.Rapport is not null)
{
    await File.WriteAllLinesAsync(options.Rapport,
        new[] { "cle;taille;sha256" }.Concat(fichiers.Select(f => $"{f.Cle};{f.Taille.ToString(CultureInfo.InvariantCulture)};{f.Sha256}")));
    Console.WriteLine($"Rapport : {Path.GetFullPath(options.Rapport)}");
}

var ok = anomalies.Count == 0 && identiques == fichiers.Count;
Console.WriteLine(ok ? "RÉSULTAT : copie IDENTIQUE — les originaux restent sur le disque." : "RÉSULTAT : ÉCARTS — ne pas passer l'API en Stockage:Mode=S3.");
return ok ? 0 : 1;

static async Task<(string Sha256, long Taille)> Empreinte(Stream flux)
{
    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var tampon = new byte[81920];
    long taille = 0;
    int lus;
    while ((lus = await flux.ReadAsync(tampon)) > 0)
    {
        sha.AppendData(tampon, 0, lus);
        taille += lus;
    }
    return (Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), taille);
}

static async Task<GetObjectMetadataResponse?> Metadonnees(IAmazonS3 s3, string bucket, string cle)
{
    try
    {
        return await s3.GetObjectMetadataAsync(bucket, cle);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        return null;
    }
}

static string TypeMime(string cle) => Path.GetExtension(cle).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".webp" => "image/webp",
    ".pdf" => "application/pdf",
    _ => "application/octet-stream"
};

static string Octets(long n) => n >= 1024 * 1024
    ? $"{n / 1024d / 1024d:0.00} Mio ({n:N0} o)".Replace(' ', ' ')
    : $"{n:N0} o".Replace(' ', ' ');

static Options? LireArguments(string[] args)
{
    string? sourceArg = null, env = null, urlArg = null, bucketArg = null, rapport = null;
    var controle = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--source" when i + 1 < args.Length: sourceArg = args[++i]; break;
            case "--fichier-env" when i + 1 < args.Length: env = args[++i]; break;
            case "--url" when i + 1 < args.Length: urlArg = args[++i]; break;
            case "--bucket" when i + 1 < args.Length: bucketArg = args[++i]; break;
            case "--rapport" when i + 1 < args.Length: rapport = args[++i]; break;
            case "--controle-seul": controle = true; break;
            default: return null;
        }
    }
    return sourceArg is null || env is null ? null : new Options(sourceArg, env, urlArg, bucketArg, controle, rapport);
}

sealed record Fichier(string Cle, string Chemin, long Taille, string Sha256);

sealed record Options(string Source, string FichierEnv, string? Url, string? Bucket, bool ControleSeul, string? Rapport);
