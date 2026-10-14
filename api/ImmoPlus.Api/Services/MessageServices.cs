namespace ImmoPlus.Api.Services;

using Microsoft.AspNetCore.SignalR;
using ImmoPlus.Api.Hubs;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Messagerie voyageur ↔ hôte, une conversation par réservation. L'admin lit sans écrire ni marquer comme lu.
/// </summary>
public partial class MessageServices
{
    private const int LongueurMax = 2000;
    private static readonly TimeSpan DureeOuvertureApresFin = TimeSpan.FromDays(30);
    private static readonly TimeSpan IntervalleEmails = TimeSpan.FromHours(1);
    public const string MessageCoordonnees = "Les coordonnées sont partagées après confirmation de la réservation.";

    private readonly AppDbContext _context;
    private readonly NotificationServices _notificationServices;
    private readonly ReservationServices _reservationServices;
    private readonly IHubContext<MessagesHub> _hub;

    public MessageServices(AppDbContext context, NotificationServices notificationServices, ReservationServices reservationServices, IHubContext<MessagesHub> hub)
    {
        _context = context;
        _notificationServices = notificationServices;
        _reservationServices = reservationServices;
        _hub = hub;
    }

    /// <summary>Fil de la conversation (ordre chronologique) ; les messages reçus par l'appelant passent en lus.</summary>
    public async Task<List<MessageDto>> ListerAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await ChargerAsync(reservationId);
        var participant = EstParticipant(reservation, utilisateurId);
        if (!participant && role != RoleUtilisateur.Admin)
        {
            throw new ConflictException("Vous n'avez pas accès à cette conversation");
        }

        var messages = await _context.Messages
            .Include(m => m.Auteur)
            .Where(m => m.ReservationId == reservationId)
            .OrderBy(m => m.DateEnvoi).ThenBy(m => m.Id)
            .ToListAsync();

        if (participant)
        {
            var maintenant = DateTime.UtcNow;
            var nonLus = messages.Where(m => m.AuteurId != utilisateurId && m.DateLecture is null).ToList();
            nonLus.ForEach(m => m.DateLecture = maintenant);
            if (nonLus.Count > 0)
            {
                await _context.SaveChangesAsync();
                // Accusé de lecture instantané chez l'auteur, et pastille des non lus à jour chez le lecteur (autres onglets).
                var lecture = new { reservationId, lecteurId = utilisateurId, dateLecture = maintenant };
                var auteurs = nonLus.Select(m => m.AuteurId.ToString()).Distinct().Append(utilisateurId.ToString()).ToList();
                await _hub.Clients.Users(auteurs).SendAsync("messagesLus", lecture);
                await _hub.Clients.Group(MessagesHub.GroupeAdmins).SendAsync("messagesLus", lecture);
            }
        }
        return messages.Select(m => VersDto(m, utilisateurId)).ToList();
    }

    public async Task<MessageDto> EnvoyerAsync(int reservationId, EnvoyerMessageDto dto, int utilisateurId, RoleUtilisateur role)
    {
        if (role == RoleUtilisateur.Admin)
        {
            throw new ConflictException("L'administrateur peut lire les conversations mais pas y écrire");
        }
        // Un séjour dont le départ est passé doit être vu comme terminé pour le délai de clôture.
        await _reservationServices.TerminerSejoursAsync(r => r.Id == reservationId);
        var reservation = await ChargerAsync(reservationId);
        if (!EstParticipant(reservation, utilisateurId))
        {
            throw new ConflictException("Vous n'avez pas accès à cette conversation");
        }

        var contenu = (dto.Contenu ?? "").Trim();
        if (contenu.Length == 0)
        {
            throw new InvalidOperationException("Le message ne peut pas être vide");
        }
        if (contenu.Length > LongueurMax)
        {
            throw new InvalidOperationException($"Le message ne peut pas dépasser {LongueurMax} caractères");
        }

        var maintenant = DateTime.UtcNow;
        var fin = DateFinConversation(reservation);
        if (fin is not null && maintenant - fin.Value > DureeOuvertureApresFin)
        {
            throw new ConflictException("Cette conversation est close : la réservation est terminée, annulée ou refusée depuis plus de 30 jours");
        }
        // Tant que le séjour n'est pas confirmé (payé), les échanges restent sur la plateforme.
        var confirmee = reservation.Statut is StatutReservation.Confirmee or StatutReservation.Terminee;
        if (!confirmee && ContientCoordonnees(contenu))
        {
            throw new InvalidOperationException(MessageCoordonnees);
        }

        var message = new Message
        {
            ReservationId = reservationId,
            AuteurId = utilisateurId,
            Contenu = contenu,
            DateEnvoi = maintenant
        };
        await _context.Messages.AddAsync(message);
        await _context.SaveChangesAsync();

        // Livraison instantanée : au destinataire, et à l'auteur pour ses autres onglets ou appareils.
        var destinataireId = reservation.LocataireId == utilisateurId ? reservation.Appartement!.BailleurId : reservation.LocataireId;
        message.Auteur = reservation.LocataireId == utilisateurId ? reservation.Locataire : reservation.Appartement!.Bailleur;
        await _hub.Clients.User(destinataireId.ToString()).SendAsync("nouveauMessage", VersDto(message, destinataireId));
        await _hub.Clients.User(utilisateurId.ToString()).SendAsync("nouveauMessage", VersDto(message, utilisateurId));
        // Les admins (jamais participants) voient le message arriver dans le fil qu'ils consultent : deMoi = false.
        await _hub.Clients.Group(MessagesHub.GroupeAdmins).SendAsync("nouveauMessage", VersDto(message, 0));

        // Au plus un email par heure et par destinataire pour une conversation : les messages suivants restent visibles sur le site.
        var deVoyageur = reservation.LocataireId == utilisateurId;
        var dernierEmail = deVoyageur ? reservation.DateDernierEmailMessageHote : reservation.DateDernierEmailMessageVoyageur;
        if (dernierEmail is null || maintenant - dernierEmail.Value >= IntervalleEmails)
        {
            var auteur = deVoyageur ? reservation.Locataire! : reservation.Appartement!.Bailleur!;
            var destinataire = deVoyageur ? reservation.Appartement!.Bailleur! : reservation.Locataire!;
            if (await _notificationServices.NouveauMessageAsync(destinataire, reservation, reservation.Appartement!, auteur.Nom, contenu, pourHote: deVoyageur))
            {
                if (deVoyageur)
                {
                    reservation.DateDernierEmailMessageHote = maintenant;
                }
                else
                {
                    reservation.DateDernierEmailMessageVoyageur = maintenant;
                }
                await _context.SaveChangesAsync();
            }
        }

        message.Auteur = deVoyageur ? reservation.Locataire : reservation.Appartement!.Bailleur;
        return VersDto(message, utilisateurId);
    }

    /// <summary>Messages reçus non lus par l'utilisateur, toutes conversations confondues.</summary>
    public async Task<MessagesNonLusDto> NonLusAsync(int utilisateurId)
    {
        var parReservation = await _context.Messages.AsNoTracking()
            .Where(m => m.AuteurId != utilisateurId && m.DateLecture == null
                && (m.Reservation!.LocataireId == utilisateurId || m.Reservation.Appartement!.BailleurId == utilisateurId))
            .GroupBy(m => m.ReservationId)
            .Select(g => new NonLusReservationDto { ReservationId = g.Key, Nombre = g.Count() })
            .ToListAsync();
        return new MessagesNonLusDto
        {
            Total = parReservation.Sum(p => p.Nombre),
            ParReservation = parReservation.OrderBy(p => p.ReservationId).ToList()
        };
    }

    /// <summary>
    /// Téléphone (au moins 8 chiffres consécutifs, espaces, points et tirets ignorés) ou adresse email :
    /// filtre léger pour éviter que le paiement ne se fasse hors de la plateforme.
    /// </summary>
    public static bool ContientCoordonnees(string texte) =>
        Telephone().IsMatch(Separateurs().Replace(texte, "")) || Email().IsMatch(texte);

    [GeneratedRegex(@"[\s.\-]")]
    private static partial Regex Separateurs();

    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex Telephone();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    /// <summary>Date à partir de laquelle court le délai de 30 jours avant clôture de la conversation (null : conversation ouverte).</summary>
    private static DateTime? DateFinConversation(Reservation r) => r.Statut switch
    {
        StatutReservation.Annulee => r.DateAnnulation ?? r.DateCreation,
        StatutReservation.Refusee => r.DateReponseHote ?? r.DateCreation,
        StatutReservation.Terminee => r.DateDepart,
        _ => null
    };

    private async Task<Reservation> ChargerAsync(int reservationId) =>
        await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(r => r.Id == reservationId)
        ?? throw new NotFoundException("Cette réservation n'existe pas");

    private static bool EstParticipant(Reservation r, int utilisateurId) =>
        r.LocataireId == utilisateurId || r.Appartement!.BailleurId == utilisateurId;

    private static MessageDto VersDto(Message m, int utilisateurId) => new()
    {
        Id = m.Id,
        ReservationId = m.ReservationId,
        AuteurId = m.AuteurId,
        AuteurNom = m.Auteur?.Nom ?? "",
        Contenu = m.Contenu,
        DateEnvoi = m.DateEnvoi,
        DateLecture = m.DateLecture,
        DeMoi = m.AuteurId == utilisateurId
    };
}
