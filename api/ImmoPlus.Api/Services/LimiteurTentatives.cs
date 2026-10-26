namespace ImmoPlus.Api.Services;

using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using ImmoPlus.Api.Exceptions;

/// <summary>
/// Freine la recherche de mots de passe par essais successifs. Connexion : 5 échecs par email ou 20 par adresse IP en 15 min
/// bloquent 15 min (429). Mot de passe oublié : 5 demandes par email en 15 min. Renvoi de l'email de confirmation :
/// 3 par email et par heure. Compteurs dans Redis quand Redis:Connexion
/// est renseigné (partagés par toutes les instances de l'API), sinon en mémoire (une seule instance).
/// </summary>
public class LimiteurTentatives
{
    public static readonly TimeSpan Fenetre = TimeSpan.FromMinutes(15);
    public const int MaxEchecsParEmail = 5;
    public const int MaxEchecsParIp = 20;
    public const int MaxDemandesReinitialisation = 5;
    public const int MaxRenvoisConfirmation = 3;
    public static readonly TimeSpan FenetreRenvoiConfirmation = TimeSpan.FromHours(1);

    private readonly ICompteursTentatives _compteurs;

    public LimiteurTentatives(ICompteursTentatives compteurs)
    {
        _compteurs = compteurs;
    }

    private static string CleEmail(string? email) => $"connexion:email:{(email ?? "").Trim().ToLowerInvariant()}";
    private static string CleIp(string? ip) => $"connexion:ip:{ip ?? "inconnue"}";

    /// <summary>429 si l'email ou l'adresse IP est bloqué.</summary>
    public async Task VerifierConnexionAsync(string? email, string? ip)
    {
        var restant = new[] { await _compteurs.BlocageAsync(CleEmail(email)), await _compteurs.BlocageAsync(CleIp(ip)) }.Max();
        if (restant is { } duree)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(duree.TotalMinutes));
            throw new TropDeTentativesException($"Trop de tentatives de connexion. Réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.");
        }
    }

    public async Task EnregistrerEchecConnexionAsync(string? email, string? ip)
    {
        await _compteurs.EnregistrerEchecAsync(CleEmail(email), MaxEchecsParEmail, Fenetre);
        await _compteurs.EnregistrerEchecAsync(CleIp(ip), MaxEchecsParIp, Fenetre);
    }

    /// <summary>Connexion réussie : le compteur de cet email repart de zéro (celui de l'IP, partagée, n'est pas effacé).</summary>
    public Task ReinitialiserConnexionAsync(string? email) => _compteurs.EffacerAsync(CleEmail(email));

    /// <summary>true si un email de réinitialisation peut encore partir pour cet email (la demande est alors comptée).</summary>
    public Task<bool> AutoriserReinitialisationAsync(string? email) =>
        _compteurs.CompterSiPossibleAsync($"reinitialisation:email:{(email ?? "").Trim().ToLowerInvariant()}", MaxDemandesReinitialisation, Fenetre);

    /// <summary>true si l'email de confirmation peut encore être renvoyé à cette adresse (le renvoi est alors compté).</summary>
    public Task<bool> AutoriserRenvoiConfirmationAsync(string? email) =>
        _compteurs.CompterSiPossibleAsync($"confirmation:email:{(email ?? "").Trim().ToLowerInvariant()}", MaxRenvoisConfirmation,
            FenetreRenvoiConfirmation);
}

/// <summary>Compteurs à fenêtre glissante du limiteur (mémoire locale ou Redis).</summary>
public interface ICompteursTentatives
{
    /// <summary>Temps restant si la clé est bloquée, sinon null.</summary>
    Task<TimeSpan?> BlocageAsync(string cle);

    /// <summary>Compte un échec ; au maximum atteint dans la fenêtre, la clé est bloquée pendant une fenêtre.</summary>
    Task EnregistrerEchecAsync(string cle, int maximum, TimeSpan fenetre);

    Task EffacerAsync(string cle);

    /// <summary>true (et l'essai est compté) s'il reste moins de <paramref name="maximum"/> essais dans la fenêtre.</summary>
    Task<bool> CompterSiPossibleAsync(string cle, int maximum, TimeSpan fenetre);
}

/// <summary>Compteurs en mémoire : perdus au redémarrage, propres à une instance (mode sans Redis, et secours si Redis tombe).</summary>
public class CompteursTentativesMemoire : ICompteursTentatives
{
    private readonly IMemoryCache _cache;
    private readonly object _verrou = new();

    public CompteursTentativesMemoire(IMemoryCache cache)
    {
        _cache = cache;
    }

    private sealed class Compteur
    {
        public List<DateTime> Dates { get; } = new();
        public DateTime? BloqueJusqua { get; set; }
    }

    public Task<TimeSpan?> BlocageAsync(string cle)
    {
        lock (_verrou)
        {
            if (_cache.TryGetValue(cle, out Compteur? compteur) && compteur!.BloqueJusqua is { } fin && fin > DateTime.UtcNow)
            {
                return Task.FromResult<TimeSpan?>(fin - DateTime.UtcNow);
            }
            return Task.FromResult<TimeSpan?>(null);
        }
    }

    public Task EnregistrerEchecAsync(string cle, int maximum, TimeSpan fenetre)
    {
        lock (_verrou)
        {
            var compteur = Obtenir(cle, fenetre);
            Purger(compteur, fenetre);
            compteur.Dates.Add(DateTime.UtcNow);
            if (compteur.Dates.Count >= maximum)
            {
                compteur.BloqueJusqua = DateTime.UtcNow + fenetre;
                compteur.Dates.Clear();
            }
        }
        return Task.CompletedTask;
    }

    public Task EffacerAsync(string cle)
    {
        _cache.Remove(cle);
        return Task.CompletedTask;
    }

    public Task<bool> CompterSiPossibleAsync(string cle, int maximum, TimeSpan fenetre)
    {
        lock (_verrou)
        {
            var compteur = Obtenir(cle, fenetre);
            Purger(compteur, fenetre);
            if (compteur.Dates.Count >= maximum)
            {
                return Task.FromResult(false);
            }
            compteur.Dates.Add(DateTime.UtcNow);
            return Task.FromResult(true);
        }
    }

    private Compteur Obtenir(string cle, TimeSpan fenetre) =>
        _cache.GetOrCreate(cle, entree =>
        {
            entree.SlidingExpiration = fenetre * 2;
            return new Compteur();
        })!;

    private static void Purger(Compteur compteur, TimeSpan fenetre)
    {
        var limite = DateTime.UtcNow - fenetre;
        compteur.Dates.RemoveAll(d => d < limite);
    }
}

/// <summary>
/// Compteurs partagés dans Redis : un ensemble trié par clé (dates des essais, fenêtre glissante) et une clé « bloque »
/// qui expire seule. Chaque opération est un script Lua, donc atomique même avec des requêtes simultanées sur plusieurs
/// instances. Redis indisponible : repli sur les compteurs en mémoire de l'instance (le site reste utilisable).
/// </summary>
public class CompteursTentativesRedis : ICompteursTentatives
{
    private readonly string _prefixe;

    // KEYS[1] essais, KEYS[2] blocage ; ARGV maintenant (ms), fenêtre (ms), maximum, identifiant unique de l'essai.
    private const string ScriptEchec = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', tonumber(ARGV[1]) - tonumber(ARGV[2]))
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[2]) * 2)
        if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
          redis.call('SET', KEYS[2], '1', 'PX', ARGV[2])
          redis.call('DEL', KEYS[1])
          return 1
        end
        return 0
        """;

    private const string ScriptCompter = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', tonumber(ARGV[1]) - tonumber(ARGV[2]))
        if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
          return 0
        end
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[2]) * 2)
        return 1
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly CompteursTentativesMemoire _secours;
    private readonly ILogger<CompteursTentativesRedis> _logger;

    public CompteursTentativesRedis(IConnectionMultiplexer redis, CompteursTentativesMemoire secours, IConfiguration configuration,
        ILogger<CompteursTentativesRedis> logger)
    {
        _prefixe = PrefixeRedis.Lire(configuration) + ":tentatives:";
        _redis = redis;
        _secours = secours;
        _logger = logger;
    }

    private RedisKey[] Cles(string cle) => [_prefixe + cle, _prefixe + cle + ":bloque"];
    private static long Maintenant => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async Task<TimeSpan?> BlocageAsync(string cle)
    {
        try
        {
            var restant = await _redis.GetDatabase().KeyTimeToLiveAsync(Cles(cle)[1]);
            return restant is { } duree && duree > TimeSpan.Zero ? duree : null;
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            Avertir(ex);
            return await _secours.BlocageAsync(cle);
        }
    }

    public async Task EnregistrerEchecAsync(string cle, int maximum, TimeSpan fenetre)
    {
        try
        {
            await _redis.GetDatabase().ScriptEvaluateAsync(ScriptEchec, Cles(cle),
                [Maintenant, (long)fenetre.TotalMilliseconds, maximum, Guid.NewGuid().ToString("N")]);
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            Avertir(ex);
            await _secours.EnregistrerEchecAsync(cle, maximum, fenetre);
        }
    }

    public async Task EffacerAsync(string cle)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(Cles(cle));
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            Avertir(ex);
        }
        await _secours.EffacerAsync(cle);
    }

    public async Task<bool> CompterSiPossibleAsync(string cle, int maximum, TimeSpan fenetre)
    {
        try
        {
            var resultat = await _redis.GetDatabase().ScriptEvaluateAsync(ScriptCompter, [Cles(cle)[0]],
                [Maintenant, (long)fenetre.TotalMilliseconds, maximum, Guid.NewGuid().ToString("N")]);
            return (int)resultat == 1;
        }
        catch (Exception ex) when (PanneRedis.Est(ex))
        {
            Avertir(ex);
            return await _secours.CompterSiPossibleAsync(cle, maximum, fenetre);
        }
    }

    private void Avertir(Exception ex) =>
        _logger.LogWarning(ex, "Redis indisponible : limitation des tentatives en mémoire locale (propre à cette instance)");
}
