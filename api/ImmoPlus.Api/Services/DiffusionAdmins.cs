namespace ImmoPlus.Api.Services;

using Microsoft.AspNetCore.SignalR;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Hubs;
using ImmoPlus.Api.Models;

/// <summary>
/// Pousse aux admins connectés (groupe « admins » du hub, toutes instances via le backplane Redis) les changements d'état
/// des comptes, pour que la page « Utilisateurs » se rafraîchisse seule. À appeler après validation de la transaction ;
/// un échec est journalisé sans jamais faire échouer l'action (le temps réel n'est qu'un confort).
/// </summary>
public class DiffusionAdmins
{
    public const string EvenementUtilisateurChange = "utilisateurChange";

    private readonly IHubContext<MessagesHub> _hub;
    private readonly ILogger<DiffusionAdmins> _logger;

    public DiffusionAdmins(IHubContext<MessagesHub> hub, ILogger<DiffusionAdmins> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task UtilisateurChangeAsync(Utilisateur utilisateur, string type)
    {
        var evenement = new UtilisateurChangeDto
        {
            UtilisateurId = utilisateur.Id,
            Type = type,
            Statut = utilisateur.Statut.ToString(),
            EmailConfirme = utilisateur.EmailConfirme
        };
        try
        {
            // Borné : un backplane injoignable ne doit pas faire attendre la réponse HTTP.
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _hub.Clients.Group(MessagesHub.GroupeAdmins).SendAsync(EvenementUtilisateurChange, evenement, delai.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Événement temps réel {Evenement} ({Type}) non diffusé aux admins pour l'utilisateur {UtilisateurId}",
                EvenementUtilisateurChange, type, utilisateur.Id);
        }
    }
}
