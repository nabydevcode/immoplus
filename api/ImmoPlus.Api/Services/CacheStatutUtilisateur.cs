namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Models.Enums;

/// <summary>Erreurs d'accès à Redis (connexion perdue, délai dépassé) : l'API se replie alors sur la base ou la mémoire.</summary>
public static class PanneRedis
{
    public static bool Est(Exception ex) => ex is RedisException or RedisTimeoutException;
}

/// <summary>
/// Préfixe de toutes les clés et canaux Redis de l'API (Redis:Prefixe, « immoplus » par défaut) : deux environnements
/// (dev et tests de charge, préproduction et production…) peuvent partager un même Redis sans mélanger cache, compteurs
/// et temps réel. Clés obtenues : {prefixe}:statut-utilisateur:…, {prefixe}:tentatives:…, {prefixe}:hote:…,
/// {prefixe}-cache:… (cache de sortie) et canaux {prefixe}-signalr… (backplane SignalR) — identiques à avant par défaut.
/// </summary>
public static partial class PrefixeRedis
{
    public const string Defaut = "immoplus";

    public static string Lire(IConfiguration configuration)
    {
        var prefixe = configuration["Redis:Prefixe"]?.Trim();
        if (string.IsNullOrEmpty(prefixe))
        {
            return Defaut;
        }
        return Autorise().IsMatch(prefixe)
            ? prefixe
            : throw new InvalidOperationException("Redis:Prefixe : 1 à 50 caractères parmi lettres, chiffres, « . », « _ » et « - »");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9._\-]{1,50}$")]
    private static partial System.Text.RegularExpressions.Regex Autorise();
}

/// <summary>
/// Statut du compte vérifié à chaque requête authentifiée (un compte désactivé est coupé sans attendre l'expiration du jeton).
/// </summary>
public interface ICacheStatutUtilisateur
{
    /// <summary>Statut actuel (null si le compte n'existe plus).</summary>
    Task<StatutUtilisateur?> LireAsync(int utilisateurId, AppDbContext context);

    /// <summary>À appeler après tout changement de statut enregistré (validation, refus, désactivation, réactivation).</summary>
    Task InvaliderAsync(int utilisateurId);
}

/// <summary>Sans Redis : lecture en base à chaque requête, comme avant (une seule instance, aucun risque de cache périmé).</summary>
public class CacheStatutUtilisateurBase : ICacheStatutUtilisateur
{
    public Task<StatutUtilisateur?> LireAsync(int utilisateurId, AppDbContext context) => LireEnBaseAsync(utilisateurId, context);

    public Task InvaliderAsync(int utilisateurId) => Task.CompletedTask;

    public static Task<StatutUtilisateur?> LireEnBaseAsync(int utilisateurId, AppDbContext context) =>
        context.Utilisateurs.AsNoTracking()
            .Where(u => u.Id == utilisateurId).Select(u => (StatutUtilisateur?)u.Statut).FirstOrDefaultAsync();
}

/// <summary>
/// Avec Redis : statut gardé 30 s (Redis:DureeCacheStatutSecondes, 30 à 60) et partagé par les instances, pour ne pas
/// interroger la base à chaque appel. Toute modification du statut efface l'entrée : la désactivation reste immédiate
/// sur toutes les instances. Redis indisponible : lecture en base.
/// </summary>
public class CacheStatutUtilisateurRedis : ICacheStatutUtilisateur
{
    private readonly string _prefixe;
    /// <summary>Compte inexistant (supprimé) : mis en cache aussi, pour ne pas interroger la base à chaque appel.</summary>
    private const int Inexistant = -1;

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _duree;
    private readonly ILogger<CacheStatutUtilisateurRedis> _logger;

    public CacheStatutUtilisateurRedis(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<CacheStatutUtilisateurRedis> logger)
    {
        _redis = redis;
        _prefixe = PrefixeRedis.Lire(configuration) + ":statut-utilisateur:";
        _duree = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Redis:DureeCacheStatutSecondes", 30), 30, 60));
        _logger = logger;
    }

    public async Task<StatutUtilisateur?> LireAsync(int utilisateurId, AppDbContext context)
    {
        try
        {
            var valeur = await _redis.GetDatabase().StringGetAsync(_prefixe + utilisateurId);
            if (valeur.HasValue && int.TryParse(valeur.ToString(), out var code))
            {
                return code == Inexistant ? null : (StatutUtilisateur)code;
            }
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            _logger.LogWarning(ex, "Redis indisponible : statut du compte lu en base");
            return await CacheStatutUtilisateurBase.LireEnBaseAsync(utilisateurId, context);
        }

        var statut = await CacheStatutUtilisateurBase.LireEnBaseAsync(utilisateurId, context);
        try
        {
            await _redis.GetDatabase().StringSetAsync(_prefixe + utilisateurId, statut is { } s ? (int)s : Inexistant, _duree);
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            _logger.LogWarning(ex, "Redis indisponible : statut du compte non mis en cache");
        }
        return statut;
    }

    public async Task InvaliderAsync(int utilisateurId)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(_prefixe + utilisateurId);
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            // Au pire, l'ancien statut reste servi jusqu'à l'expiration de l'entrée (30 s par défaut).
            _logger.LogError(ex, "Redis indisponible : cache du statut de l'utilisateur {Id} non effacé", utilisateurId);
        }
    }
}
