namespace ImmoPlus.Api.Services;

using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using ImmoPlus.Api.Dtos;

/// <summary>
/// Badges des hôtes (superhôte, taux et délai de réponse, note, ancienneté) affichés sur les annonces : ~6 agrégats sur
/// leurs réservations et leurs avis, gardés <see cref="CacheHotes.Duree"/> pour ne pas les recalculer à chaque liste ou
/// fiche. Avec Redis, partagés par toutes les instances (une seule lecture groupée par page) ; sinon en mémoire.
/// </summary>
public interface ICacheHotes
{
    /// <summary>Badges trouvés en cache (les hôtes absents ne figurent pas dans le résultat).</summary>
    Task<Dictionary<int, HoteDto>> LireAsync(IReadOnlyCollection<int> ids);

    Task EcrireAsync(IReadOnlyDictionary<int, HoteDto> hotes);

    /// <summary>Changement à montrer tout de suite (ex. identité vérifiée) : l'entrée de l'hôte est effacée.</summary>
    Task InvaliderAsync(int id);
}

public static class CacheHotes
{
    public static readonly TimeSpan Duree = TimeSpan.FromMinutes(2);
}

public class CacheHotesMemoire(IMemoryCache cache) : ICacheHotes
{
    private static (string, int) Cle(int id) => ("hote", id);

    public Task<Dictionary<int, HoteDto>> LireAsync(IReadOnlyCollection<int> ids)
    {
        var resultat = new Dictionary<int, HoteDto>();
        foreach (var id in ids)
        {
            if (cache.TryGetValue(Cle(id), out HoteDto? hote) && hote is not null)
            {
                resultat[id] = hote;
            }
        }
        return Task.FromResult(resultat);
    }

    public Task EcrireAsync(IReadOnlyDictionary<int, HoteDto> hotes)
    {
        foreach (var (id, hote) in hotes)
        {
            cache.Set(Cle(id), hote, CacheHotes.Duree);
        }
        return Task.CompletedTask;
    }

    public Task InvaliderAsync(int id)
    {
        cache.Remove(Cle(id));
        return Task.CompletedTask;
    }
}

/// <summary>Redis : un MGET pour toute une page d'annonces ; Redis indisponible → badges recalculés (jamais d'erreur visible).</summary>
public class CacheHotesRedis : ICacheHotes
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefixe;
    private readonly ILogger<CacheHotesRedis> _logger;

    public CacheHotesRedis(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<CacheHotesRedis> logger)
    {
        _redis = redis;
        _prefixe = PrefixeRedis.Lire(configuration) + ":hote:";
        _logger = logger;
    }

    public async Task<Dictionary<int, HoteDto>> LireAsync(IReadOnlyCollection<int> ids)
    {
        var resultat = new Dictionary<int, HoteDto>();
        if (ids.Count == 0)
        {
            return resultat;
        }
        var liste = ids.ToList();
        try
        {
            var valeurs = await _redis.GetDatabase().StringGetAsync(liste.Select(id => (RedisKey)(_prefixe + id)).ToArray());
            for (var i = 0; i < liste.Count; i++)
            {
                if (valeurs[i].HasValue && JsonSerializer.Deserialize<HoteDto>(valeurs[i].ToString(), Json) is { } hote)
                {
                    resultat[liste[i]] = hote;
                }
            }
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            _logger.LogWarning(ex, "Redis indisponible : badges des hôtes recalculés");
        }
        return resultat;
    }

    public async Task EcrireAsync(IReadOnlyDictionary<int, HoteDto> hotes)
    {
        if (hotes.Count == 0)
        {
            return;
        }
        try
        {
            var db = _redis.GetDatabase();
            await Task.WhenAll(hotes.Select(h => db.StringSetAsync(_prefixe + h.Key, JsonSerializer.Serialize(h.Value, Json), CacheHotes.Duree)));
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            _logger.LogWarning(ex, "Redis indisponible : badges des hôtes non mis en cache");
        }
    }

    public async Task InvaliderAsync(int id)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(_prefixe + id);
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            // Au pire, l'ancien badge reste affiché jusqu'à l'expiration de l'entrée (2 min).
            _logger.LogWarning(ex, "Redis indisponible : badges de l'hôte {Id} non effacés du cache", id);
        }
    }
}
