namespace ImmoPlus.Api.Services;

using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Models.Enums;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

/// <summary>
/// Identifiant de corrélation et journal d'accès : chaque requête reprend l'en-tête X-Correlation-Id (s'il est sûr) ou en
/// génère un, le renvoie dans la réponse et l'ajoute à toutes les lignes de journal de la requête (portée « CorrelationId »).
/// Une ligne par requête : méthode, route (modèle, sans les valeurs ni la chaîne de requête, qui peut porter un jeton),
/// statut, durée et n° d'utilisateur. Jamais de mot de passe, de jeton ni de contenu de message.
/// </summary>
public partial class MiddlewareCorrelation(RequestDelegate suivant, ILogger<MiddlewareCorrelation> journal)
{
    public const string Entete = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext contexte)
    {
        var recu = contexte.Request.Headers[Entete].ToString();
        var id = IdentifiantSur().IsMatch(recu) ? recu : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        contexte.TraceIdentifier = id;
        // Posé au tout dernier moment (OnStarting) : le cache de sortie a déjà copié les en-têtes à mettre en cache, et une
        // réponse resservie depuis le cache (qui rejoue ses en-têtes) reçoit quand même l'identifiant de CETTE requête.
        contexte.Response.OnStarting(() =>
        {
            contexte.Response.Headers[Entete] = id;
            return Task.CompletedTask;
        });

        var debut = Stopwatch.GetTimestamp();
        using (journal.BeginScope("CorrelationId:{CorrelationId}", id))
        {
            try
            {
                await suivant(contexte);
            }
            finally
            {
                var duree = Stopwatch.GetElapsedTime(debut).TotalMilliseconds;
                var route = (contexte.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(aucune route)";
                var statut = contexte.Response.StatusCode;
                // Santé et métriques interrogées toutes les quelques secondes : seulement en Debug, sauf en cas d'erreur.
                var technique = contexte.Request.Path.StartsWithSegments("/sante") || contexte.Request.Path.StartsWithSegments("/metriques");
                var niveau = statut >= 500 ? LogLevel.Error : technique ? LogLevel.Debug : LogLevel.Information;
                journal.Log(niveau, "Requête {Methode} {Route} → {Statut} en {DureeMs} ms (utilisateur {UtilisateurId})",
                    contexte.Request.Method, route, statut, Math.Round(duree, 1),
                    contexte.User.Identity?.IsAuthenticated == true ? contexte.User.ObtenirId() : null);
            }
        }
    }

    /// <summary>Identifiant fourni par un client ou un répartiteur : 1 à 64 caractères sans espace ni caractère de contrôle.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,64}$")]
    private static partial Regex IdentifiantSur();
}

/// <summary>Requêtes SQL plus lentes que Journaux:SeuilRequeteLenteMs (200 ms par défaut) : un avertissement avec le SQL, sans les valeurs.</summary>
public class RequetesLentesInterceptor(IConfiguration configuration, ILogger<RequetesLentesInterceptor> journal) : DbCommandInterceptor
{
    private readonly TimeSpan _seuil = TimeSpan.FromMilliseconds(configuration.GetValue("Journaux:SeuilRequeteLenteMs", 200));

    private void Verifier(DbCommand commande, CommandExecutedEventData donnees)
    {
        if (donnees.Duration >= _seuil)
        {
            var sql = commande.CommandText.Length > 4000 ? commande.CommandText[..4000] + "…" : commande.CommandText;
            journal.LogWarning("Requête SQL lente ({DureeMs} ms) : {Sql}", Math.Round(donnees.Duration.TotalMilliseconds), sql);
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Verifier(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Verifier(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Verifier(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Verifier(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Verifier(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Verifier(command, eventData);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// Métriques propres à ImmoPlus (compteur « ImmoPlus.Api ») : taille de la boîte d'envoi (messages en attente ou à
/// réessayer), emails dont la copie « Envoyés » a échoué. Lue en base au plus toutes les 15 s, quel que soit le rythme de lecture de /metriques.
/// Les autres métriques viennent des compteurs intégrés : requêtes HTTP (Microsoft.AspNetCore.Hosting), connexions
/// SignalR (Microsoft.AspNetCore.Http.Connections), requêtes SQL (Npgsql), processus et GC (System.Runtime).
/// </summary>
public sealed class MetriquesImmoPlus : IDisposable
{
    public const string NomCompteur = "ImmoPlus.Api";
    private static readonly TimeSpan DureeCache = TimeSpan.FromSeconds(15);

    private readonly Meter _compteur = new(NomCompteur);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MetriquesImmoPlus> _journal;
    private readonly Lock _verrou = new();
    private (long EnAttente, long Abandonnes, long CopiesEnEchec, DateTime LuLe) _boiteEnvoi = (0, 0, 0, DateTime.MinValue);

    public MetriquesImmoPlus(IServiceScopeFactory scopes, ILogger<MetriquesImmoPlus> journal)
    {
        _scopes = scopes;
        _journal = journal;
        _compteur.CreateObservableGauge("immoplus.boite_envoi.en_attente", () => LireBoiteEnvoi().EnAttente, "{message}",
            "Emails et SMS de la boîte d'envoi pas encore partis (en attente ou à réessayer)");
        _compteur.CreateObservableGauge("immoplus.boite_envoi.abandonnes", () => LireBoiteEnvoi().Abandonnes, "{message}",
            "Emails et SMS abandonnés après 5 tentatives");
        _compteur.CreateObservableGauge("immoplus.boite_envoi.copie_echecs", () => LireBoiteEnvoi().CopiesEnEchec, "{message}",
            "Emails partis dont la copie IMAP dans « Envoyés » a échoué (à réessayer ou abandonnée)");
    }

    private (long EnAttente, long Abandonnes, long CopiesEnEchec) LireBoiteEnvoi()
    {
        lock (_verrou)
        {
            if (DateTime.UtcNow - _boiteEnvoi.LuLe < DureeCache)
            {
                return (_boiteEnvoi.EnAttente, _boiteEnvoi.Abandonnes, _boiteEnvoi.CopiesEnEchec);
            }
            try
            {
                using var scope = _scopes.CreateScope();
                var contexte = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parStatut = contexte.MessagesSortants.AsNoTracking()
                    .Where(m => m.Statut != StatutMessageSortant.Envoye)
                    .GroupBy(m => m.Statut)
                    .Select(g => new { Statut = g.Key, Nombre = g.LongCount() })
                    .ToList();
                var copiesEnEchec = contexte.MessagesSortants.AsNoTracking()
                    .LongCount(m => m.CopieEnvoyes == StatutCopieEnvoyes.Echec || m.CopieEnvoyes == StatutCopieEnvoyes.Abandonnee);
                _boiteEnvoi = (parStatut.Where(s => s.Statut != StatutMessageSortant.Abandonne).Sum(s => s.Nombre),
                    parStatut.Where(s => s.Statut == StatutMessageSortant.Abandonne).Sum(s => s.Nombre), copiesEnEchec, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // Base indisponible : dernière valeur connue (la santé /sante/pret signale déjà la panne).
                _journal.LogWarning(ex, "Lecture de la boîte d'envoi impossible pour les métriques");
                _boiteEnvoi = _boiteEnvoi with { LuLe = DateTime.UtcNow };
            }
            return (_boiteEnvoi.EnAttente, _boiteEnvoi.Abandonnes, _boiteEnvoi.CopiesEnEchec);
        }
    }

    public void Dispose() => _compteur.Dispose();
}

/// <summary>
/// /metriques n'est pas public : avec Metriques:Jeton renseigné, il faut « Authorization: Bearer {jeton} » (Prometheus :
/// authorization.credentials) ; sinon seuls la boucle locale et les réseaux privés (Docker, LAN) y accèdent. Derrière nginx,
/// l'adresse vue est celle du vrai client (X-Forwarded-For de confiance) : un visiteur d'Internet reçoit 404.
/// </summary>
public static class AccesMetriques
{
    public const string Chemin = "/metriques";

    public static bool Autorise(HttpContext contexte, string? jeton)
    {
        if (!string.IsNullOrEmpty(jeton))
        {
            var entete = contexte.Request.Headers.Authorization.ToString();
            return entete.StartsWith("Bearer ", StringComparison.Ordinal)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(entete["Bearer ".Length..]), System.Text.Encoding.UTF8.GetBytes(jeton));
        }
        return contexte.Connection.RemoteIpAddress is { } ip && EstInterne(ip);
    }

    public static bool EstInterne(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal;
        }
        var o = ip.GetAddressBytes();
        return o[0] == 10 || (o[0] == 172 && o[1] >= 16 && o[1] <= 31) || (o[0] == 192 && o[1] == 168);
    }
}

/// <summary>Santé : PostgreSQL (requête « SELECT 1 »).</summary>
public class SantePostgres(AppDbContext contexte) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await contexte.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL injoignable");
}

/// <summary>Santé : Redis (PING), seulement s'il est configuré.</summary>
public class SanteRedis(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
        {
            return HealthCheckResult.Unhealthy("Redis injoignable");
        }
        await redis.GetDatabase().PingAsync();
        return HealthCheckResult.Healthy();
    }
}

/// <summary>Santé : stockage S3 (lecture des métadonnées d'une clé témoin : bucket joignable, clé d'accès acceptée).</summary>
public class SanteStockage(IStockageFichiers stockage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await stockage.ExisteAsync("sante/temoin", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

public static class ReponsesSante
{
    public const string Pret = "pret";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>JSON sans secret ni message d'exception : statut global, et statut + durée de chaque vérification.</summary>
    public static Task EcrireAsync(HttpContext contexte, HealthReport rapport)
    {
        contexte.Response.ContentType = "application/json; charset=utf-8";
        contexte.Response.Headers.CacheControl = "no-store";
        var corps = new
        {
            statut = rapport.Status == HealthStatus.Healthy ? "Ok" : "Indisponible",
            dureeMs = Math.Round(rapport.TotalDuration.TotalMilliseconds, 1),
            verifications = rapport.Entries.ToDictionary(e => e.Key, e => new
            {
                statut = e.Value.Status == HealthStatus.Healthy ? "Ok" : "Indisponible",
                dureeMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                detail = e.Value.Status == HealthStatus.Healthy ? null : e.Value.Description ?? "Erreur ou délai dépassé"
            })
        };
        return contexte.Response.WriteAsync(JsonSerializer.Serialize(corps, Json));
    }
}

/// <summary>
/// Cache de sortie des listes publiques anonymes (annonces, villes, avis d'un logement) : 30 s, partagé par Redis entre
/// les instances s'il est configuré. Jamais pour une requête avec jeton (règle par défaut du cache de sortie). Vidé dès
/// qu'une requête modifie ce qui y est affiché (voir <see cref="DoitVider"/>).
/// </summary>
public static class CachePublic
{
    public const string Annonces = "annonces-publiques";
    public const string Etiquette = "annonces";
    public static readonly TimeSpan Duree = TimeSpan.FromSeconds(30);

    public static void Configurer(OutputCacheOptions options) =>
        // Varie selon tous les paramètres de la requête et l'origine (en-têtes CORS de la réponse).
        options.AddPolicy(Annonces, b => b.Expire(Duree).SetVaryByQuery("*").SetVaryByHeader("Origin").Tag(Etiquette));

    /// <summary>
    /// Modifications qui changent les annonces publiques : annonce, photos, calendrier, avis et réponses, compte d'un hôte
    /// (désactivation, nom), signalements traités (masquage), identité vérifiée (badge).
    /// </summary>
    public static bool DoitVider(HttpRequest requete)
    {
        if (HttpMethods.IsGet(requete.Method) || HttpMethods.IsHead(requete.Method) || HttpMethods.IsOptions(requete.Method))
        {
            return false;
        }
        var chemin = requete.Path.Value ?? "";
        return chemin.StartsWith("/appartement", StringComparison.OrdinalIgnoreCase)
            || chemin.EndsWith("/avis", StringComparison.OrdinalIgnoreCase)
            || chemin.StartsWith("/avis/", StringComparison.OrdinalIgnoreCase)
            || chemin.StartsWith("/admin/signalements/", StringComparison.OrdinalIgnoreCase)
            || chemin.StartsWith("/admin/identites/", StringComparison.OrdinalIgnoreCase)
            || (chemin.StartsWith("/utilisateur/", StringComparison.OrdinalIgnoreCase)
                && (chemin.EndsWith("/desactiver", StringComparison.OrdinalIgnoreCase) || chemin.EndsWith("/reactiver", StringComparison.OrdinalIgnoreCase)
                    || chemin.EndsWith("/valider", StringComparison.OrdinalIgnoreCase) || chemin.Equals("/utilisateur/moi", StringComparison.OrdinalIgnoreCase)));
    }
}
