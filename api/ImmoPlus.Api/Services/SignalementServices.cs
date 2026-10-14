namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Signalements d'annonces, d'utilisateurs, d'avis et de messages, traités par les admins.
/// Anti-abus : 5 signalements par heure (par utilisateur connecté, sinon par adresse IP), et pas deux signalements ouverts
/// du même auteur sur la même cible.
/// </summary>
public class SignalementServices
{
    public const int MaxParHeure = 5;
    private const int LongueurMaxTexte = 1000;
    public static readonly string[] Cibles = ["Appartement", "Utilisateur", "Avis", "Message"];
    public static readonly string[] Actions = ["aucune", "desactiverAnnonce", "desactiverUtilisateur", "masquerAvis"];

    private readonly AppDbContext _context;
    private readonly NotificationServices _notificationServices;
    private readonly AppartementServices _appartementServices;
    private readonly UtilisateurServices _utilisateurServices;
    private readonly JournalServices _journal;

    public SignalementServices(AppDbContext context, NotificationServices notificationServices, AppartementServices appartementServices,
        UtilisateurServices utilisateurServices, JournalServices journal)
    {
        _context = context;
        _notificationServices = notificationServices;
        _appartementServices = appartementServices;
        _utilisateurServices = utilisateurServices;
        _journal = journal;
    }

    /// <summary>Description et liens de la cible (pour l'admin et l'email).</summary>
    private sealed record InfosCible(string Resume, int? AppartementId, int? ReservationId, int? LocationId, int? UtilisateurId);

    public async Task<SignalementDto> CreerAsync(CreerSignalementDto dto, int? utilisateurId, string? adresseIp)
    {
        var cible = Cibles.FirstOrDefault(c => string.Equals(c, dto.Cible?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Cible invalide. Utilisez Appartement, Utilisateur, Avis ou Message");
        if (string.IsNullOrWhiteSpace(dto.Motif) || int.TryParse(dto.Motif, out _)
            || !Enum.TryParse<MotifSignalement>(dto.Motif.Trim(), true, out var motif) || !Enum.IsDefined(motif))
        {
            throw new InvalidOperationException("Motif invalide. Utilisez Arnaque, AnnonceTrompeuse, ContenuInapproprie, Doublon, "
                + "LogementIndisponible, ComportementAbusif ou Autre");
        }
        var texte = (dto.Texte ?? "").Trim();
        if (texte.Length > LongueurMaxTexte)
        {
            throw new InvalidOperationException($"Le texte ne doit pas dépasser {LongueurMaxTexte} caractères");
        }
        if (motif == MotifSignalement.Autre && texte.Length == 0)
        {
            throw new InvalidOperationException("Décrivez le problème pour le motif « Autre »");
        }

        Utilisateur? auteur = null;
        string? email = null;
        if (utilisateurId is not null)
        {
            auteur = await _context.Utilisateurs.FindAsync(utilisateurId.Value) ?? throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        else
        {
            email = dto.Email?.Trim();
            if (string.IsNullOrEmpty(email) || email.Length > 200 || !System.Net.Mail.MailAddress.TryCreate(email, out var adresse)
                || adresse.Address != email || !email.Contains('.'))
            {
                throw new InvalidOperationException("Indiquez une adresse email valide pour que nous puissions vous répondre");
            }
        }

        var infos = await InfosCibleAsync(cible, dto.CibleId, utilisateurId, pourSignalement: true);

        // Anti-abus : compté en base (survit aux redémarrages).
        var depuis = DateTime.UtcNow.AddHours(-1);
        var recents = utilisateurId is not null
            ? await _context.Signalements.CountAsync(s => s.AuteurId == utilisateurId && s.DateCreation >= depuis)
            : await _context.Signalements.CountAsync(s => s.AuteurId == null && s.AdresseIp == adresseIp && s.DateCreation >= depuis);
        if (recents >= MaxParHeure)
        {
            throw new TropDeTentativesException($"Vous avez envoyé {MaxParHeure} signalements en une heure. Réessayez plus tard.");
        }
        var cleEmail = email?.ToLower();
        var dejaOuvert = await _context.Signalements.AnyAsync(s => s.Cible == cible && s.CibleId == dto.CibleId
            && (s.Statut == StatutSignalement.Nouveau || s.Statut == StatutSignalement.EnCours)
            && (utilisateurId != null ? s.AuteurId == utilisateurId : s.AuteurId == null && s.AuteurEmail!.ToLower() == cleEmail));
        if (dejaOuvert)
        {
            throw new ConflictException("Vous avez déjà signalé ce contenu : votre signalement est en cours d'examen");
        }

        var signalement = new Signalement
        {
            AuteurId = utilisateurId,
            AuteurEmail = email,
            Cible = cible,
            CibleId = dto.CibleId,
            Motif = motif,
            Texte = texte,
            Statut = StatutSignalement.Nouveau,
            DateCreation = DateTime.UtcNow,
            AdresseIp = adresseIp
        };
        _context.Signalements.Add(signalement);
        await _context.SaveChangesAsync();

        var nomAuteur = auteur is not null ? $"{auteur.Nom} ({auteur.Email})" : $"un visiteur ({email})";
        var admins = await _context.Utilisateurs
            .Where(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide)
            .ToListAsync();
        foreach (var admin in admins)
        {
            await _notificationServices.SignalementRecuAsync(admin, signalement, infos.Resume, nomAuteur);
        }
        await _journal.EnregistrerAsync("signalement.creer", cible, dto.CibleId,
            $"Signalement n° {signalement.Id:D6} ({motif}) sur {cible} n° {dto.CibleId} par {nomAuteur}");
        return VersDto(signalement, auteur?.Nom, null, infos);
    }

    public async Task<PagedResultDto<SignalementDto>> ListerAsync(string? statut, string? cible, int page, int taille)
    {
        if (page < 1) page = 1;
        taille = taille < 1 ? 20 : Math.Min(taille, 100);
        var requete = _context.Signalements.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (int.TryParse(statut, out _) || !Enum.TryParse<StatutSignalement>(statut.Trim(), true, out var s) || !Enum.IsDefined(s))
            {
                throw new InvalidOperationException("Statut invalide. Utilisez Nouveau, EnCours, Traite ou Rejete");
            }
            requete = requete.Where(x => x.Statut == s);
        }
        if (!string.IsNullOrWhiteSpace(cible))
        {
            var type = Cibles.FirstOrDefault(c => string.Equals(c, cible.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Cible invalide. Utilisez Appartement, Utilisateur, Avis ou Message");
            requete = requete.Where(x => x.Cible == type);
        }
        var total = await requete.CountAsync();
        var lignes = await requete
            .OrderByDescending(x => x.DateCreation).ThenByDescending(x => x.Id)
            .Skip((page - 1) * taille).Take(taille)
            .Select(x => new { Signalement = x, AuteurNom = x.Auteur!.Nom, TraiteParNom = x.TraitePar!.Nom })
            .ToListAsync();
        var donnees = new List<SignalementDto>();
        foreach (var l in lignes)
        {
            InfosCible infos;
            try
            {
                infos = await InfosCibleAsync(l.Signalement.Cible, l.Signalement.CibleId, null, pourSignalement: false);
            }
            catch (NotFoundException)
            {
                infos = new InfosCible("(supprimé)", null, null, null, null);
            }
            donnees.Add(VersDto(l.Signalement, l.AuteurNom, l.TraiteParNom, infos));
        }
        return new PagedResultDto<SignalementDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = total,
            TotalPages = (int)Math.Ceiling(total / (double)taille)
        };
    }

    public Task<int> NombreNouveauxAsync() => _context.Signalements.CountAsync(s => s.Statut == StatutSignalement.Nouveau);

    /// <summary>
    /// Traitement par un admin : statut, décision et action éventuelle, appliquée avec les règles habituelles
    /// (désactivation d'annonce ou de compte : mêmes refus, mêmes emails). Si l'action est refusée, rien n'est enregistré.
    /// </summary>
    public async Task<SignalementDto> TraiterAsync(int id, TraiterSignalementDto dto, int adminId)
    {
        var signalement = await _context.Signalements.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException("Ce signalement n'existe pas");
        if (signalement.Statut is StatutSignalement.Traite or StatutSignalement.Rejete)
        {
            throw new ConflictException("Ce signalement a déjà été clôturé");
        }
        if (string.IsNullOrWhiteSpace(dto.Statut) || int.TryParse(dto.Statut, out _)
            || !Enum.TryParse<StatutSignalement>(dto.Statut.Trim(), true, out var statut)
            || statut is not (StatutSignalement.EnCours or StatutSignalement.Traite or StatutSignalement.Rejete))
        {
            throw new InvalidOperationException("Statut invalide. Utilisez EnCours, Traite ou Rejete");
        }
        var decision = string.IsNullOrWhiteSpace(dto.Decision) ? null : dto.Decision.Trim();
        if (decision is { Length: > 1000 })
        {
            throw new InvalidOperationException("La décision ne doit pas dépasser 1000 caractères");
        }
        var action = Actions.FirstOrDefault(a => string.Equals(a, (dto.Action ?? "aucune").Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Action invalide. Utilisez aucune, desactiverAnnonce, desactiverUtilisateur ou masquerAvis");
        if (action != "aucune" && statut == StatutSignalement.Rejete)
        {
            throw new InvalidOperationException("Un signalement rejeté ne peut pas déclencher d'action");
        }

        var infos = await InfosCibleAsync(signalement.Cible, signalement.CibleId, null, pourSignalement: false);
        string? detailAction = null;
        switch (action)
        {
            case "desactiverAnnonce":
                if (signalement.Cible != "Appartement")
                {
                    throw new InvalidOperationException("La désactivation d'annonce ne s'applique qu'à un signalement d'annonce");
                }
                await _appartementServices.SupprimerAsync(signalement.CibleId, adminId, RoleUtilisateur.Admin);
                detailAction = $"annonce n° {signalement.CibleId} désactivée";
                break;
            case "desactiverUtilisateur":
                var utilisateurId = infos.UtilisateurId
                    ?? throw new InvalidOperationException("Aucun utilisateur n'est rattaché à cette cible");
                await _utilisateurServices.DesactiverAsync(utilisateurId, adminId,
                    decision ?? $"Suite au signalement n° {signalement.Id:D6}");
                detailAction = $"compte n° {utilisateurId} désactivé";
                break;
            case "masquerAvis":
                if (signalement.Cible != "Avis")
                {
                    throw new InvalidOperationException("Le masquage ne s'applique qu'à un signalement d'avis");
                }
                var avis = await _context.Avis.FirstAsync(a => a.Id == signalement.CibleId);
                if (avis.Masque)
                {
                    throw new ConflictException("Cet avis est déjà masqué");
                }
                avis.Masque = true;
                detailAction = $"avis n° {avis.Id} masqué";
                break;
        }

        signalement.Statut = statut;
        signalement.DecisionAdmin = decision ?? signalement.DecisionAdmin;
        if (action != "aucune")
        {
            signalement.ActionAppliquee = action;
        }
        if (statut != StatutSignalement.EnCours)
        {
            signalement.DateTraitement = DateTime.UtcNow;
        }
        signalement.TraiteParId = adminId;
        await _context.SaveChangesAsync();

        await _journal.EnregistrerAsync("signalement.traiter", "Signalement", signalement.Id,
            $"Signalement n° {signalement.Id:D6} ({signalement.Cible} n° {signalement.CibleId}) : {statut}"
            + (detailAction is null ? "" : $", {detailAction}") + (decision is null ? "" : $" — {decision}"));

        var noms = await _context.Utilisateurs.AsNoTracking()
            .Where(u => u.Id == signalement.AuteurId || u.Id == adminId)
            .Select(u => new { u.Id, u.Nom }).ToListAsync();
        return VersDto(signalement, noms.FirstOrDefault(n => n.Id == signalement.AuteurId)?.Nom, noms.FirstOrDefault(n => n.Id == adminId)?.Nom, infos);
    }

    /// <param name="pourSignalement">Création : la cible doit être visible de l'auteur (message : participant uniquement).</param>
    private async Task<InfosCible> InfosCibleAsync(string cible, int cibleId, int? auteurId, bool pourSignalement)
    {
        switch (cible)
        {
            case "Appartement":
            {
                var a = await _context.Appartements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cibleId)
                    ?? throw new NotFoundException("Cette annonce n'existe pas");
                if (pourSignalement && auteurId == a.BailleurId)
                {
                    throw new InvalidOperationException("Vous ne pouvez pas signaler votre propre annonce");
                }
                return new InfosCible($"Annonce n° {a.Id} — {a.Adresse}", a.Id, null, null, a.BailleurId);
            }
            case "Utilisateur":
            {
                var u = await _context.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cibleId);
                if (u is null || (pourSignalement && u.Role == RoleUtilisateur.Admin))
                {
                    throw new NotFoundException("Cet utilisateur n'existe pas");
                }
                if (pourSignalement && auteurId == u.Id)
                {
                    throw new InvalidOperationException("Vous ne pouvez pas vous signaler vous-même");
                }
                return new InfosCible($"{u.Nom} ({u.Role})", null, null, null, u.Id);
            }
            case "Avis":
            {
                var a = await _context.Avis.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cibleId);
                if (a is null || (pourSignalement && a.Masque))
                {
                    throw new NotFoundException("Cet avis n'existe pas");
                }
                if (pourSignalement && auteurId == a.AuteurId)
                {
                    throw new InvalidOperationException("Vous ne pouvez pas signaler votre propre avis");
                }
                return new InfosCible($"Avis ({a.Note}/5) : « {Extrait(a.Commentaire)} »", a.AppartementId, a.ReservationId, a.LocationId, a.AuteurId);
            }
            default:
            {
                var m = await _context.Messages.AsNoTracking().Include(x => x.Reservation).ThenInclude(r => r!.Appartement)
                    .FirstOrDefaultAsync(x => x.Id == cibleId)
                    ?? throw new NotFoundException("Ce message n'existe pas");
                if (pourSignalement)
                {
                    // Un message est privé : seul son destinataire (l'autre participant de la conversation) peut le signaler.
                    var participants = new[] { m.Reservation!.LocataireId, m.Reservation.Appartement!.BailleurId };
                    if (auteurId is null || !participants.Contains(auteurId.Value))
                    {
                        throw new ConflictException("Connectez-vous avec le compte qui a reçu ce message pour le signaler");
                    }
                    if (auteurId == m.AuteurId)
                    {
                        throw new InvalidOperationException("Vous ne pouvez pas signaler votre propre message");
                    }
                }
                return new InfosCible($"Message (réservation n° {m.ReservationId:D6}) : « {Extrait(m.Contenu)} »",
                    m.Reservation!.AppartementId, m.ReservationId, null, m.AuteurId);
            }
        }
    }

    private static string Extrait(string texte) => texte.Length > 120 ? texte[..120] + "…" : texte;

    private static SignalementDto VersDto(Signalement s, string? auteurNom, string? traiteParNom, InfosCible infos) => new()
    {
        Id = s.Id,
        Reference = s.Id.ToString("D6"),
        AuteurId = s.AuteurId,
        AuteurNom = auteurNom,
        AuteurEmail = s.AuteurEmail,
        Cible = s.Cible,
        CibleId = s.CibleId,
        CibleResume = infos.Resume,
        AppartementId = infos.AppartementId,
        ReservationId = infos.ReservationId,
        LocationId = infos.LocationId,
        UtilisateurCibleId = infos.UtilisateurId,
        Motif = s.Motif.ToString(),
        Texte = s.Texte,
        Statut = s.Statut.ToString(),
        DecisionAdmin = s.DecisionAdmin,
        ActionAppliquee = s.ActionAppliquee,
        DateCreation = s.DateCreation,
        DateTraitement = s.DateTraitement,
        TraiteParId = s.TraiteParId,
        TraiteParNom = traiteParNom
    };
}
