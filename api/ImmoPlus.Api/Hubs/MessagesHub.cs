namespace ImmoPlus.Api.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Services;

/// <summary>
/// Messagerie en temps réel : le serveur pousse les nouveaux messages, les accusés de lecture et l'indicateur « écrit… ».
/// Chaque utilisateur est joint par son id (claim NameIdentifier) via Clients.User ; l'envoi des messages reste en HTTP (validation, emails).
/// Les admins rejoignent le groupe « admins » : nouveaux messages et lectures leur sont aussi poussés (fil en lecture seule).
/// </summary>
[Authorize]
public class MessagesHub : Hub
{
    public const string Chemin = "/hubs/messages";
    /// <summary>Groupe des administrateurs : ils suivent en direct toutes les conversations (lecture seule).</summary>
    public const string GroupeAdmins = "admins";

    private readonly AppDbContext _context;

    public MessagesHub(AppDbContext context)
    {
        _context = context;
    }

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupeAdmins);
        }
        await base.OnConnectedAsync();
    }

    /// <summary>L'utilisateur est en train d'écrire dans la conversation : l'autre participant en est prévenu.</summary>
    public async Task Ecrit(int reservationId)
    {
        var utilisateurId = Context.User!.ObtenirId();
        var participants = await _context.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new { r.LocataireId, r.Appartement!.BailleurId })
            .FirstOrDefaultAsync();
        if (participants is null || (participants.LocataireId != utilisateurId && participants.BailleurId != utilisateurId))
        {
            return;
        }
        var destinataire = participants.LocataireId == utilisateurId ? participants.BailleurId : participants.LocataireId;
        await Clients.User(destinataire.ToString()).SendAsync("ecrit", new { reservationId, auteurId = utilisateurId });
    }
}
