namespace ImmoPlus.Api.Services;

using Microsoft.Extensions.Caching.Memory;
using ImmoPlus.Api.Exceptions;

/// <summary>
/// Freine la recherche de mots de passe par essais successifs : compteurs en mémoire (perdus au redémarrage, suffisant pour une seule instance).
/// Connexion : 5 échecs par email ou 20 par adresse IP en 15 min bloquent 15 min (429). Mot de passe oublié : 5 demandes par email en 15 min.
/// </summary>
public class LimiteurTentatives
{
    public static readonly TimeSpan Fenetre = TimeSpan.FromMinutes(15);
    public const int MaxEchecsParEmail = 5;
    public const int MaxEchecsParIp = 20;
    public const int MaxDemandesReinitialisation = 5;

    private readonly IMemoryCache _cache;
    private readonly object _verrou = new();

    public LimiteurTentatives(IMemoryCache cache)
    {
        _cache = cache;
    }

    private sealed class Compteur
    {
        public List<DateTime> Dates { get; } = new();
        public DateTime? BloqueJusqua { get; set; }
    }

    private static string CleEmail(string? email) => $"connexion:email:{(email ?? "").Trim().ToLowerInvariant()}";
    private static string CleIp(string? ip) => $"connexion:ip:{ip ?? "inconnue"}";

    /// <summary>429 si l'email ou l'adresse IP est bloqué.</summary>
    public void VerifierConnexion(string? email, string? ip)
    {
        var restant = new[] { Blocage(CleEmail(email)), Blocage(CleIp(ip)) }.Max();
        if (restant is { } duree)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(duree.TotalMinutes));
            throw new TropDeTentativesException($"Trop de tentatives de connexion. Réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.");
        }
    }

    public void EnregistrerEchecConnexion(string? email, string? ip)
    {
        EnregistrerEchec(CleEmail(email), MaxEchecsParEmail);
        EnregistrerEchec(CleIp(ip), MaxEchecsParIp);
    }

    /// <summary>Connexion réussie : le compteur de cet email repart de zéro (celui de l'IP, partagée, n'est pas effacé).</summary>
    public void ReinitialiserConnexion(string? email) => _cache.Remove(CleEmail(email));

    /// <summary>true si un email de réinitialisation peut encore partir pour cet email (la demande est alors comptée).</summary>
    public bool AutoriserReinitialisation(string? email)
    {
        var cle = $"reinitialisation:email:{(email ?? "").Trim().ToLowerInvariant()}";
        lock (_verrou)
        {
            var compteur = Obtenir(cle);
            Purger(compteur);
            if (compteur.Dates.Count >= MaxDemandesReinitialisation)
            {
                return false;
            }
            compteur.Dates.Add(DateTime.UtcNow);
            return true;
        }
    }

    private TimeSpan? Blocage(string cle)
    {
        lock (_verrou)
        {
            if (_cache.TryGetValue(cle, out Compteur? compteur) && compteur!.BloqueJusqua is { } fin && fin > DateTime.UtcNow)
            {
                return fin - DateTime.UtcNow;
            }
            return null;
        }
    }

    private void EnregistrerEchec(string cle, int maximum)
    {
        lock (_verrou)
        {
            var compteur = Obtenir(cle);
            Purger(compteur);
            compteur.Dates.Add(DateTime.UtcNow);
            if (compteur.Dates.Count >= maximum)
            {
                compteur.BloqueJusqua = DateTime.UtcNow + Fenetre;
                compteur.Dates.Clear();
            }
        }
    }

    private Compteur Obtenir(string cle) =>
        _cache.GetOrCreate(cle, entree =>
        {
            entree.SlidingExpiration = Fenetre * 2;
            return new Compteur();
        })!;

    private static void Purger(Compteur compteur)
    {
        var limite = DateTime.UtcNow - Fenetre;
        compteur.Dates.RemoveAll(d => d < limite);
    }
}
