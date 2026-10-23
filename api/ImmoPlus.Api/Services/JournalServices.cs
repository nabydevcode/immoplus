namespace ImmoPlus.Api.Services;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;

/// <summary>
/// Journal des actions importantes (audit). L'auteur et son adresse IP viennent de la requête en cours.
/// Écrit par un contexte à part sur la même connexion (comme les SMS) : n'enregistre jamais par mégarde des modifications
/// en attente du service appelant, sans ouvrir une seconde connexion (ni sortir de sa transaction). Un échec d'écriture est journalisé sans jamais
/// faire échouer l'action métier.
/// </summary>
public class JournalServices
{
    /// <summary>Durée de conservation : les entrées plus anciennes sont purgées par le service des rappels.</summary>
    public const int ConservationAnnees = 2;
    private const int LongueurMaxDetails = 1000;

    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _context;
    private readonly ILogger<JournalServices> _logger;

    public JournalServices(IHttpContextAccessor http, AppDbContext context, ILogger<JournalServices> logger)
    {
        _http = http;
        _context = context;
        _logger = logger;
    }

    /// <summary>Action de l'utilisateur connecté (ou « Systeme » hors requête HTTP, « Anonyme » sans jeton).</summary>
    public Task EnregistrerAsync(string action, string cible, int? cibleId, string details)
    {
        var utilisateur = _http.HttpContext?.User;
        if (utilisateur?.Identity?.IsAuthenticated == true)
        {
            return EnregistrerPourAsync(utilisateur.ObtenirId(), utilisateur.FindFirstValue(ClaimTypes.Name) ?? "",
                utilisateur.FindFirstValue(ClaimTypes.Role) ?? "", action, cible, cibleId, details);
        }
        return EnregistrerPourAsync(null, _http.HttpContext is null ? "Système" : "Visiteur",
            _http.HttpContext is null ? "Systeme" : "Anonyme", action, cible, cibleId, details);
    }

    /// <summary>Auteur explicite (ex. connexion : l'utilisateur n'est pas encore authentifié par un jeton).</summary>
    public async Task EnregistrerPourAsync(int? utilisateurId, string utilisateurNom, string role, string action, string cible, int? cibleId, string details)
    {
        try
        {
            await using var contexte = await AppDbContext.AnnexeAsync(_context);
            contexte.JournalActions.Add(new JournalAction
            {
                Date = DateTime.UtcNow,
                UtilisateurId = utilisateurId,
                UtilisateurNom = Tronquer(utilisateurNom, 100),
                Role = Tronquer(role, 20),
                Action = Tronquer(action, 60),
                Cible = Tronquer(cible, 30),
                CibleId = cibleId,
                Details = Tronquer(details.Trim(), LongueurMaxDetails),
                AdresseIp = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
            });
            await contexte.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'écriture du journal pour l'action {Action}", action);
        }
    }

    /// <param name="action">Code exact, ou préfixe (« utilisateur » ou « utilisateur. » trouve toutes les actions sur les utilisateurs).</param>
    /// <param name="recherche">Texte cherché dans les détails, le nom de l'auteur et le code de l'action.</param>
    public async Task<PagedResultDto<JournalActionDto>> ListerAsync(int page, int taille, int? utilisateurId, string? action, string? cible,
        int? cibleId, DateTime? du, DateTime? au, string? recherche)
    {
        if (page < 1) page = 1;
        taille = taille < 1 ? 50 : Math.Min(taille, 200);
        if (du is not null && au is not null && au < du)
        {
            throw new InvalidOperationException("La date de fin doit être postérieure à la date de début");
        }

        var requete = _context.JournalActions.AsNoTracking().AsQueryable();
        if (utilisateurId is not null)
        {
            requete = requete.Where(j => j.UtilisateurId == utilisateurId);
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            var code = action.Trim().ToLower();
            var prefixe = code.EndsWith('.') ? code : code + ".";
            requete = requete.Where(j => j.Action.ToLower() == code || j.Action.ToLower().StartsWith(prefixe));
        }
        if (!string.IsNullOrWhiteSpace(cible))
        {
            var type = cible.Trim().ToLower();
            requete = requete.Where(j => j.Cible.ToLower() == type);
        }
        if (cibleId is not null)
        {
            requete = requete.Where(j => j.CibleId == cibleId);
        }
        if (du is not null)
        {
            requete = requete.Where(j => j.Date >= du.Value.Date);
        }
        if (au is not null)
        {
            // Borne de fin incluse : toute la journée « au ».
            var fin = au.Value.Date.AddDays(1);
            requete = requete.Where(j => j.Date < fin);
        }
        if (!string.IsNullOrWhiteSpace(recherche))
        {
            // Sans casse ni accents.
            var motif = RechercheTexte.Contient(recherche);
            requete = requete.Where(j => EF.Functions.ILike(EF.Functions.Unaccent(j.Details), EF.Functions.Unaccent(motif))
                || EF.Functions.ILike(EF.Functions.Unaccent(j.UtilisateurNom), EF.Functions.Unaccent(motif))
                || EF.Functions.ILike(j.Action, motif));
        }

        var total = await requete.CountAsync();
        var donnees = await requete
            .OrderByDescending(j => j.Date).ThenByDescending(j => j.Id)
            .Skip((page - 1) * taille).Take(taille)
            .Select(j => new JournalActionDto
            {
                Id = j.Id,
                Date = j.Date,
                UtilisateurId = j.UtilisateurId,
                UtilisateurNom = j.UtilisateurNom,
                Role = j.Role,
                Action = j.Action,
                Cible = j.Cible,
                CibleId = j.CibleId,
                Details = j.Details,
                AdresseIp = j.AdresseIp
            })
            .ToListAsync();
        return new PagedResultDto<JournalActionDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = total,
            TotalPages = (int)Math.Ceiling(total / (double)taille)
        };
    }

    /// <summary>Supprime les entrées de plus de 2 ans ; renvoie le nombre d'entrées supprimées.</summary>
    public async Task<int> PurgerAsync(CancellationToken annulation = default)
    {
        var limite = DateTime.UtcNow.AddYears(-ConservationAnnees);
        return await _context.JournalActions.Where(j => j.Date < limite).ExecuteDeleteAsync(annulation);
    }

    private static string Tronquer(string valeur, int longueur) => valeur.Length > longueur ? valeur[..(longueur - 1)] + "…" : valeur;

    /// <summary>Montant lisible pour les détails du journal : « 1 500 000 GNF ».</summary>
    public static string Montant(decimal montant) =>
        $"{montant.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ').Replace(' ', ' ')} GNF";
}
