namespace ImmoPlus.Api.Tests.Outils;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ImmoPlus.Api.Services;
using Testcontainers.Redis;

/// <summary>
/// Serveurs jetables (Testcontainers, Docker requis) démarrés une fois pour tout « dotnet test » : Redis et Garage
/// (stockage objet compatible S3, le même que le service compose). Jamais les services du docker-compose.
/// </summary>
public static class ServeursAnnexesTests
{
    public const string ImageGarage = "dxflrs/garage:v2.4.1";
    public const string ImageRedis = "redis:8.8-alpine";

    private static readonly Lazy<Task<string>> Redis = new(DemarrerRedisAsync);
    private static readonly Lazy<Task<Garage>> GarageS3 = new(DemarrerGarageAsync);

    /// <summary>Chaîne StackExchange.Redis du serveur de test.</summary>
    public static Task<string> RedisAsync() => Redis.Value;

    private static async Task<string> DemarrerRedisAsync()
    {
        var conteneur = new RedisBuilder(ImageRedis).Build();
        await conteneur.StartAsync();
        return conteneur.GetConnectionString() + ",abortConnect=false";
    }

    /// <summary>Serveur Garage prêt (nœud configuré) et sa clé (droits complets sur les buckets de test).</summary>
    public sealed record Garage(string Url, string UrlAdmin, string JetonAdmin, string CleAcces, string CleSecrete);

    /// <summary>
    /// Bucket privé dédié (« immoplus-test-… »), créé vide ; à supprimer avec <see cref="SupprimerBucketAsync"/>.
    /// Renvoie les réglages Stockage:* de l'API pour l'utiliser.
    /// </summary>
    public static async Task<Dictionary<string, string?>> CreerBucketAsync(string bucket)
    {
        var garage = await GarageS3.Value;
        using var admin = ClientAdmin(garage);
        var reponse = await admin.PostAsJsonAsync("/v2/CreateBucket", new { globalAlias = bucket });
        reponse.EnsureSuccessStatusCode();
        var id = (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        (await admin.PostAsJsonAsync("/v2/AllowBucketKey", new
        {
            bucketId = id,
            accessKeyId = garage.CleAcces,
            permissions = new { read = true, write = true, owner = true }
        })).EnsureSuccessStatusCode();
        return new Dictionary<string, string?>
        {
            ["Stockage:Mode"] = "S3",
            ["Stockage:S3:Url"] = garage.Url,
            ["Stockage:S3:Bucket"] = bucket,
            ["Stockage:S3:CleAcces"] = garage.CleAcces,
            ["Stockage:S3:CleSecrete"] = garage.CleSecrete,
            ["Stockage:S3:Region"] = "us-east-1",
            ["Stockage:S3:CheminStyle"] = "true"
        };
    }

    /// <summary>Client S3 sur le serveur de test (contrôles directs dans le bucket).</summary>
    public static async Task<AmazonS3Client> ClientS3Async()
    {
        var garage = await GarageS3.Value;
        return new OptionsStockageS3
        {
            Url = garage.Url,
            Bucket = "-",
            CleAcces = garage.CleAcces,
            CleSecrete = garage.CleSecrete
        }.CreerClient();
    }

    /// <summary>Vide puis supprime le bucket de test.</summary>
    public static async Task SupprimerBucketAsync(string bucket)
    {
        if (!bucket.StartsWith("immoplus-test-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refus de supprimer le bucket « {bucket} » : seuls les buckets de test sont supprimés");
        }
        using (var s3 = await ClientS3Async())
        {
            foreach (var cle in await ListerAsync(s3, bucket))
            {
                await s3.DeleteObjectAsync(bucket, cle);
            }
        }
        var garage = await GarageS3.Value;
        using var admin = ClientAdmin(garage);
        var infos = await admin.GetFromJsonAsync<JsonElement>($"/v2/GetBucketInfo?globalAlias={bucket}");
        (await admin.PostAsync($"/v2/DeleteBucket?id={infos.GetProperty("id").GetString()}", null)).EnsureSuccessStatusCode();
    }

    public static async Task<List<string>> ListerAsync(IAmazonS3 s3, string bucket)
    {
        var cles = new List<string>();
        string? suite = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, ContinuationToken = suite });
            cles.AddRange((page.S3Objects ?? []).Select(o => o.Key));
            suite = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (suite is not null);
        return cles;
    }

    private static HttpClient ClientAdmin(Garage garage)
    {
        var client = new HttpClient { BaseAddress = new Uri(garage.UrlAdmin) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", garage.JetonAdmin);
        return client;
    }

    private static async Task<Garage> DemarrerGarageAsync()
    {
        var jetonAdmin = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        const string configuration = """
            metadata_dir = "/var/lib/garage/meta"
            data_dir = "/var/lib/garage/data"
            db_engine = "sqlite"
            replication_factor = 1
            rpc_bind_addr = "[::]:3901"
            rpc_public_addr = "127.0.0.1:3901"

            [s3_api]
            s3_region = "us-east-1"
            api_bind_addr = "[::]:9000"

            [admin]
            api_bind_addr = "[::]:3903"
            """;
        IContainer conteneur = new ContainerBuilder(ImageGarage)
            .WithResourceMapping(Encoding.UTF8.GetBytes(configuration), "/etc/garage.toml")
            .WithEnvironment("GARAGE_RPC_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant())
            .WithEnvironment("GARAGE_ADMIN_TOKEN", jetonAdmin)
            .WithPortBinding(9000, true)
            .WithPortBinding(3903, true)
            // Image sans shell : attente par HTTP depuis l'hôte (toute réponse de l'API d'administration suffit).
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3903).ForPath("/health").ForStatusCodeMatching(_ => true)))
            .Build();
        await conteneur.StartAsync();

        var garage = new Garage(
            $"http://{conteneur.Hostname}:{conteneur.GetMappedPublicPort(9000)}",
            $"http://{conteneur.Hostname}:{conteneur.GetMappedPublicPort(3903)}",
            jetonAdmin,
            "GK" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

        // Même initialisation que infra/garage/init-garage.sh : rôle du nœud unique, puis clé importée.
        using var admin = ClientAdmin(garage);
        JsonElement statut = default;
        for (var essai = 0; essai < 60; essai++)
        {
            try
            {
                statut = await admin.GetFromJsonAsync<JsonElement>("/v2/GetClusterStatus");
                break;
            }
            catch (HttpRequestException) when (essai < 59)
            {
                await Task.Delay(500);
            }
        }
        var noeud = statut.GetProperty("nodes")[0].GetProperty("id").GetString();
        (await admin.PostAsJsonAsync("/v2/UpdateClusterLayout", new
        {
            roles = new[] { new { id = noeud, zone = "dc1", capacity = 1_000_000_000L, tags = new[] { "tests" } } }
        })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/v2/ApplyClusterLayout", new { version = 1 })).EnsureSuccessStatusCode();
        for (var essai = 0; (await admin.GetAsync("/health")).StatusCode != System.Net.HttpStatusCode.OK; essai++)
        {
            if (essai > 60)
            {
                throw new InvalidOperationException("Garage de test : stockage jamais prêt");
            }
            await Task.Delay(500);
        }
        (await admin.PostAsJsonAsync("/v2/ImportKey", new
        {
            name = "tests",
            accessKeyId = garage.CleAcces,
            secretAccessKey = garage.CleSecrete
        })).EnsureSuccessStatusCode();
        return garage;
    }
}
