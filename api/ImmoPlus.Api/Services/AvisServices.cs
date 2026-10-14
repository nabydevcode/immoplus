namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Avis après séjour (voyageur → logement, hôte → voyageur), après une location longue durée (locataire → logement,
/// bailleur → locataire) et badges de confiance des hôtes.
/// Les badges sont recalculés à chaque lecture à partir des réservations et des avis : rien n'est stocké.
/// Les avis masqués par un admin (signalement) sont exclus de l'affichage public et des moyennes.
/// </summary>
public class AvisServices
{
    /// <summary>Location terminée : avis possible pendant 30 jours après la date de fin.</summary>
    public const int JoursAvisApresLocation = 30;
    /// <summary>Location en cours : avis possible (un seul) à partir de 6 mois après la remise des clés.</summary>
    public const int MoisAvantAvisLocation = 6;

    /// <summary>Avis portant sur un logement (et son hôte / bailleur).</summary>
    public static readonly SensAvis[] SensVersLogement = [SensAvis.VoyageurVersLogement, SensAvis.LocataireVersLogement];
    /// <summary>Avis portant sur un voyageur / locataire.</summary>
    public static readonly SensAvis[] SensVersOccupant = [SensAvis.HoteVersVoyageur, SensAvis.BailleurVersLocataire];

    private const decimal NoteMinSuperhote = 4.7m;
    private const int SejoursMinSuperhote = 3;
    private const decimal TauxReponseMinSuperhote = 90m;

    private readonly AppDbContext _context;
    private readonly NotificationServices _notificationServices;
    private readonly ReservationServices _reservationServices;
    private readonly JournalServices _journal;

    public AvisServices(AppDbContext context, NotificationServices notificationServices, ReservationServices reservationServices,
        JournalServices journal)
    {
        _journal = journal;
        _context = context;
        _notificationServices = notificationServices;
        _reservationServices = reservationServices;
    }

    public async Task<AvisDto> CreerAsync(int reservationId, CreerAvisDto dto, int utilisateurId, RoleUtilisateur role)
    {
        // Un séjour confirmé dont le départ est passé doit pouvoir être évalué même si personne n'a encore listé les réservations.
        await _reservationServices.TerminerSejoursAsync(r => r.Id == reservationId);
        var reservation = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(r => r.Id == reservationId)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (role == RoleUtilisateur.Admin)
        {
            throw new ConflictException("Seuls le voyageur et l'hôte du séjour peuvent laisser un avis");
        }
        var appartement = reservation.Appartement!;
        SensAvis sens;
        if (reservation.LocataireId == utilisateurId)
        {
            sens = SensAvis.VoyageurVersLogement;
        }
        else if (appartement.BailleurId == utilisateurId)
        {
            sens = SensAvis.HoteVersVoyageur;
        }
        else
        {
            throw new ConflictException("Vous n'avez pas accès à cette réservation");
        }

        if (reservation.Statut != StatutReservation.Terminee)
        {
            throw new ConflictException("Vous pourrez laisser un avis une fois le séjour terminé");
        }
        var limite = ReservationServices.FinPeriodeAvis(reservation);
        if (DateTime.UtcNow.Date > limite)
        {
            throw new ConflictException($"Le délai pour laisser un avis ({ReservationServices.JoursPourAvis} jours après le départ) "
                + $"a expiré le {limite:dd/MM/yyyy}");
        }
        if (await _context.Avis.AnyAsync(a => a.ReservationId == reservationId && a.Sens == sens))
        {
            throw new ConflictException("Vous avez déjà laissé un avis pour ce séjour");
        }

        var voyageur = sens == SensAvis.VoyageurVersLogement;
        var commentaire = VerifierContenu(dto, voyageur);

        var avis = new Avis
        {
            ReservationId = reservationId,
            AuteurId = utilisateurId,
            CibleId = voyageur ? appartement.BailleurId : reservation.LocataireId,
            AppartementId = appartement.Id,
            Sens = sens,
            Note = dto.Note,
            // Les sous-notes ne concernent que le logement : ignorées pour l'avis de l'hôte sur le voyageur.
            Proprete = voyageur ? dto.Proprete : null,
            Communication = voyageur ? dto.Communication : null,
            Emplacement = voyageur ? dto.Emplacement : null,
            RapportQualitePrix = voyageur ? dto.RapportQualitePrix : null,
            Commentaire = commentaire,
            DateCreation = DateTime.UtcNow
        };
        await _context.Avis.AddAsync(avis);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Index unique (ReservationId, Sens) : deux envois simultanés.
            throw new ConflictException("Vous avez déjà laissé un avis pour ce séjour");
        }

        var auteur = voyageur ? reservation.Locataire! : appartement.Bailleur!;
        var cible = voyageur ? appartement.Bailleur! : reservation.Locataire!;
        await _notificationServices.AvisRecuAsync(cible, avis, reservation, appartement, Prenom(auteur.Nom), pourHote: voyageur);

        avis.Auteur = auteur;
        return VersDto(avis);
    }

    /// <summary>
    /// Fenêtre d'avis d'une location longue durée. Terminée : jusqu'à 30 jours après la fin. En cours : à partir de 6 mois
    /// après la remise des clés (un seul avis par sens pour toute la location : pas de second avis à la fin).
    /// </summary>
    /// <returns>Possible aujourd'hui ; début et fin de la fenêtre quand elles sont connues.</returns>
    public static (bool Possible, DateTime? Depuis, DateTime? Jusquau) FenetreAvisLocation(string statut, DateTime? dateFin,
        DateTime? dateRemiseCles, DateTime? dateFinPrevue)
    {
        var aujourdhui = DateTime.UtcNow.Date;
        if (statut == nameof(StatutLocation.Terminee) && dateFin is { } fin)
        {
            var jusquau = fin.Date.AddDays(JoursAvisApresLocation);
            return (aujourdhui <= jusquau, fin.Date, jusquau);
        }
        if (statut == nameof(StatutLocation.EnCours) && dateRemiseCles is { } remise)
        {
            var depuis = remise.Date.AddMonths(MoisAvantAvisLocation);
            return (aujourdhui >= depuis, depuis, dateFinPrevue?.Date.AddDays(JoursAvisApresLocation));
        }
        return (false, null, null);
    }

    /// <summary>Avis du locataire sur le logement (et son bailleur), ou du bailleur sur son locataire, pour une location longue durée.</summary>
    public async Task<AvisDto> CreerPourLocationAsync(int locationId, CreerAvisDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var location = await _context.Locations
            .Include(l => l.Locataire)
            .Include(l => l.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(l => l.Id == locationId)
            ?? throw new NotFoundException("Cette location n'existe pas");
        if (role == RoleUtilisateur.Admin)
        {
            throw new ConflictException("Seuls le locataire et le bailleur de la location peuvent laisser un avis");
        }
        var appartement = location.Appartement!;
        SensAvis sens;
        if (location.LocataireId == utilisateurId)
        {
            sens = SensAvis.LocataireVersLogement;
        }
        else if (appartement.BailleurId == utilisateurId)
        {
            sens = SensAvis.BailleurVersLocataire;
        }
        else
        {
            throw new ConflictException("Vous n'avez pas accès à cette location");
        }

        var (possible, depuis, jusquau) = FenetreAvisLocation(location.Statut.ToString(), location.DateFin, location.DateRemiseCles, location.DateFinPrevue);
        if (!possible)
        {
            throw new ConflictException(location.Statut switch
            {
                StatutLocation.Terminee => $"Le délai pour laisser un avis ({JoursAvisApresLocation} jours après la fin de la location) a expiré le {jusquau:dd/MM/yyyy}",
                StatutLocation.EnCours when depuis is not null => $"Vous pourrez laisser un avis à partir du {depuis:dd/MM/yyyy} "
                    + $"({MoisAvantAvisLocation} mois après la remise des clés) ou à la fin de la location",
                StatutLocation.EnCours => "Vous pourrez laisser un avis après la remise des clés (au bout de 6 mois) ou à la fin de la location",
                _ => "Cette location a été annulée : aucun avis n'est possible"
            });
        }
        if (await _context.Avis.AnyAsync(a => a.LocationId == locationId && a.Sens == sens))
        {
            throw new ConflictException("Vous avez déjà laissé un avis pour cette location");
        }

        var locataire = sens == SensAvis.LocataireVersLogement;
        var commentaire = VerifierContenu(dto, locataire);
        var avis = new Avis
        {
            LocationId = locationId,
            AuteurId = utilisateurId,
            CibleId = locataire ? appartement.BailleurId : location.LocataireId,
            AppartementId = appartement.Id,
            Sens = sens,
            Note = dto.Note,
            Proprete = locataire ? dto.Proprete : null,
            Communication = locataire ? dto.Communication : null,
            Emplacement = locataire ? dto.Emplacement : null,
            RapportQualitePrix = locataire ? dto.RapportQualitePrix : null,
            Commentaire = commentaire,
            DateCreation = DateTime.UtcNow
        };
        await _context.Avis.AddAsync(avis);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Index unique (LocationId, Sens) : deux envois simultanés.
            throw new ConflictException("Vous avez déjà laissé un avis pour cette location");
        }

        var auteur = locataire ? location.Locataire! : appartement.Bailleur!;
        var cible = locataire ? appartement.Bailleur! : location.Locataire!;
        await _notificationServices.AvisLocationRecuAsync(cible, avis, location, appartement, Prenom(auteur.Nom), pourBailleur: locataire);
        await _journal.EnregistrerAsync("avis.creer", "Location", locationId,
            $"Avis {(locataire ? "du locataire sur le logement" : "du bailleur sur le locataire")} (note {avis.Note}/5) pour la location n° {locationId:D6}");

        avis.Auteur = auteur;
        return VersDto(avis);
    }

    /// <summary>Note globale et sous-notes entre 1 et 5 (sous-notes : avis sur le logement uniquement), commentaire de 10 à 1000 caractères.</summary>
    private static string VerifierContenu(CreerAvisDto dto, bool surLogement)
    {
        VerifierNote(dto.Note, "La note");
        if (surLogement)
        {
            VerifierNote(dto.Proprete, "La note de propreté");
            VerifierNote(dto.Communication, "La note de communication");
            VerifierNote(dto.Emplacement, "La note d'emplacement");
            VerifierNote(dto.RapportQualitePrix, "La note du rapport qualité-prix");
        }
        var commentaire = (dto.Commentaire ?? "").Trim();
        if (commentaire.Length is < 10 or > 1000)
        {
            throw new InvalidOperationException("Le commentaire doit contenir entre 10 et 1000 caractères");
        }
        return commentaire;
    }

    /// <summary>L'hôte répond publiquement, une seule fois, à l'avis d'un voyageur sur son logement.</summary>
    public async Task<AvisDto> RepondreAsync(int avisId, RepondreAvisDto dto, int utilisateurId)
    {
        var avis = await _context.Avis
            .Include(a => a.Appartement)
            .Include(a => a.Auteur)
            .FirstOrDefaultAsync(a => a.Id == avisId)
            ?? throw new NotFoundException("Cet avis n'existe pas");
        if (avis.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Seul l'hôte du logement peut répondre à cet avis");
        }
        if (!SensVersLogement.Contains(avis.Sens))
        {
            throw new ConflictException("Seuls les avis des voyageurs et des locataires sur le logement appellent une réponse");
        }
        if (avis.ReponseHote is not null)
        {
            throw new ConflictException("Vous avez déjà répondu à cet avis");
        }
        var reponse = (dto.Reponse ?? "").Trim();
        if (reponse.Length is < 1 or > 500)
        {
            throw new InvalidOperationException("La réponse doit contenir entre 1 et 500 caractères");
        }

        avis.ReponseHote = reponse;
        avis.DateReponse = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return VersDto(avis);
    }

    /// <summary>Avis publics (séjours et locations) sur un logement, du plus récent au plus ancien, avec les moyennes par critère.</summary>
    public async Task<ListeAvisDto> ListerParAppartementAsync(int appartementId)
    {
        if (!await _context.Appartements.AnyAsync(a => a.Id == appartementId))
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        var avis = await _context.Avis.AsNoTracking()
            .Include(a => a.Auteur)
            .Where(a => a.AppartementId == appartementId && SensVersLogement.Contains(a.Sens) && !a.Masque)
            .OrderByDescending(a => a.DateCreation).ThenByDescending(a => a.Id)
            .ToListAsync();

        return new ListeAvisDto
        {
            Moyenne = Moyenne(avis.Select(a => (int?)a.Note)),
            Nombre = avis.Count,
            Moyennes = avis.Count == 0 ? null : new MoyennesAvisDto
            {
                Proprete = Moyenne(avis.Select(a => a.Proprete)),
                Communication = Moyenne(avis.Select(a => a.Communication)),
                Emplacement = Moyenne(avis.Select(a => a.Emplacement)),
                RapportQualitePrix = Moyenne(avis.Select(a => a.RapportQualitePrix))
            },
            Avis = avis.Select(VersDto).ToList()
        };
    }

    /// <summary>Avis des hôtes et bailleurs sur un voyageur / locataire : visibles des hôtes, des admins et de l'intéressé.</summary>
    public async Task<ListeAvisDto> ListerParUtilisateurAsync(int cibleId, int utilisateurId, RoleUtilisateur role)
    {
        if (role == RoleUtilisateur.Locataire && cibleId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez consulter que les avis vous concernant");
        }
        if (!await _context.Utilisateurs.AnyAsync(u => u.Id == cibleId))
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        var avis = await _context.Avis.AsNoTracking()
            .Include(a => a.Auteur)
            .Where(a => a.CibleId == cibleId && SensVersOccupant.Contains(a.Sens) && !a.Masque)
            .OrderByDescending(a => a.DateCreation).ThenByDescending(a => a.Id)
            .ToListAsync();

        return new ListeAvisDto
        {
            Moyenne = Moyenne(avis.Select(a => (int?)a.Note)),
            Nombre = avis.Count,
            Moyennes = null,
            Avis = avis.Select(VersDto).ToList()
        };
    }

    /// <summary>Note moyenne (1 décimale) et nombre d'avis des voyageurs, par logement.</summary>
    public async Task<Dictionary<int, (decimal? Moyenne, int Nombre)>> NotesAppartementsAsync(ICollection<int> appartementIds)
    {
        var notes = await _context.Avis.AsNoTracking()
            .Where(a => appartementIds.Contains(a.AppartementId) && SensVersLogement.Contains(a.Sens) && !a.Masque)
            .Select(a => new { a.AppartementId, a.Note })
            .ToListAsync();
        return notes.GroupBy(n => n.AppartementId)
            .ToDictionary(g => g.Key, g => (Moyenne(g.Select(n => (int?)n.Note)), g.Count()));
    }

    /// <summary>
    /// Badges de confiance de plusieurs hôtes en quelques requêtes groupées (liste des annonces).
    /// Superhôte : ≥ 3 séjours terminés, note ≥ 4,7, taux de réponse ≥ 90 % (ou aucune demande à traiter)
    /// et aucune annulation par l'hôte sur les 12 derniers mois.
    /// </summary>
    public async Task<Dictionary<int, HoteDto>> StatistiquesHotesAsync(IEnumerable<int> hoteIds)
    {
        var ids = hoteIds.Distinct().ToList();
        var maintenant = DateTime.UtcNow;
        var depuis = maintenant.AddMonths(-12);

        var hotes = await _context.Utilisateurs.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nom, u.Statut, u.DateCreation, u.IdentiteVerifiee })
            .ToListAsync();
        var datesAnnonces = await _context.Appartements.AsNoTracking()
            .Where(a => ids.Contains(a.BailleurId) && a.DateCreation != null)
            .Select(a => new { a.BailleurId, a.DateCreation })
            .ToListAsync();
        var notes = await _context.Avis.AsNoTracking()
            .Where(a => ids.Contains(a.CibleId) && SensVersLogement.Contains(a.Sens) && !a.Masque)
            .Select(a => new { a.CibleId, a.Note })
            .ToListAsync();
        var sejoursTermines = await _context.Reservations.AsNoTracking()
            .Where(r => r.Statut == StatutReservation.Terminee && ids.Contains(r.Appartement!.BailleurId))
            .GroupBy(r => r.Appartement!.BailleurId)
            .Select(g => new { BailleurId = g.Key, Nombre = g.Count() })
            .ToListAsync();
        // Demandes à traiter sur 12 mois : celles auxquelles l'hôte a répondu, ou refusées faute de réponse sous 48 h.
        var demandes = await _context.Reservations.AsNoTracking()
            .Where(r => ids.Contains(r.Appartement!.BailleurId) && r.DateCreation >= depuis
                && (r.DateReponseHote != null || r.MotifRefus == ReservationServices.MotifSansReponse))
            .Select(r => new { r.Appartement!.BailleurId, r.DateCreation, r.DateReponseHote })
            .ToListAsync();
        var annulationsHote = (await _context.Reservations.AsNoTracking()
                .Where(r => r.AnnuleePar == AuteurAnnulation.Hote && r.DateAnnulation >= depuis && ids.Contains(r.Appartement!.BailleurId))
                .Select(r => r.Appartement!.BailleurId)
                .ToListAsync())
            .ToHashSet();

        return hotes.ToDictionary(h => h.Id, h =>
        {
            var notesHote = notes.Where(n => n.CibleId == h.Id).Select(n => (int?)n.Note).ToList();
            var noteMoyenne = Moyenne(notesHote);
            var demandesHote = demandes.Where(d => d.BailleurId == h.Id).ToList();
            var repondues = demandesHote.Where(d => d.DateReponseHote is not null).ToList();
            decimal? taux = demandesHote.Count == 0 ? null : Math.Round(100m * repondues.Count / demandesHote.Count, 0, MidpointRounding.AwayFromZero);
            int? delai = repondues.Count == 0 ? null
                : (int)Math.Round(repondues.Average(d => (d.DateReponseHote!.Value - d.DateCreation).TotalHours), MidpointRounding.AwayFromZero);
            var moyenneBrute = notesHote.Count == 0 ? (decimal?)null : (decimal)notesHote.Average(n => n!.Value);
            var superhote = (sejoursTermines.FirstOrDefault(s => s.BailleurId == h.Id)?.Nombre ?? 0) >= SejoursMinSuperhote
                && moyenneBrute >= NoteMinSuperhote
                && (taux is null || taux >= TauxReponseMinSuperhote)
                && !annulationsHote.Contains(h.Id);
            var premiereDate = datesAnnonces.Where(d => d.BailleurId == h.Id).Select(d => d.DateCreation)
                .Append(h.DateCreation).Where(d => d is not null).Min();

            return new HoteDto
            {
                Id = h.Id,
                Nom = h.Nom,
                // Pièce d'identité contrôlée par un admin (et non la simple validation du compte).
                IdentiteVerifiee = h.IdentiteVerifiee,
                Superhote = superhote,
                TauxReponse = taux,
                DelaiReponseHeures = delai,
                MembreDepuis = premiereDate?.ToString("yyyy-MM"),
                NombreAvis = notesHote.Count,
                NoteMoyenne = noteMoyenne
            };
        });
    }

    /// <summary>Profil visible de tous : jamais d'email ni de téléphone.</summary>
    public async Task<ProfilPublicDto> ProfilPublicAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        // Les comptes administrateurs et les comptes désactivés n'ont pas de profil public.
        if (utilisateur is null || utilisateur.Role == RoleUtilisateur.Admin || utilisateur.Statut == StatutUtilisateur.Desactive)
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        var profil = new ProfilPublicDto
        {
            Id = utilisateur.Id,
            Prenom = Prenom(utilisateur.Nom),
            Role = utilisateur.Role.ToString(),
            IdentiteVerifiee = utilisateur.IdentiteVerifiee,
            MembreDepuis = utilisateur.DateCreation?.ToString("yyyy-MM"),
            Logements = await _context.Appartements.AsNoTracking()
                .Where(a => a.BailleurId == id && a.Disponible && !a.EstDesactive)
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .ToListAsync()
        };

        if (utilisateur.Role == RoleUtilisateur.Bailleur)
        {
            var hote = (await StatistiquesHotesAsync([id]))[id];
            profil.Superhote = hote.Superhote;
            profil.MembreDepuis = hote.MembreDepuis;
            profil.NoteMoyenne = hote.NoteMoyenne;
            profil.NombreAvis = hote.NombreAvis;
            profil.TauxReponse = hote.TauxReponse;
            profil.DelaiReponseHeures = hote.DelaiReponseHeures;
        }
        else
        {
            // Voyageur : sa réputation vient des avis laissés par ses hôtes.
            var notes = await _context.Avis.AsNoTracking()
                .Where(a => a.CibleId == id && SensVersOccupant.Contains(a.Sens) && !a.Masque)
                .Select(a => (int?)a.Note)
                .ToListAsync();
            profil.NoteMoyenne = Moyenne(notes);
            profil.NombreAvis = notes.Count;
        }
        return profil;
    }

    private static void VerifierNote(int? note, string libelle)
    {
        if (note is not null && note is < 1 or > 5)
        {
            throw new InvalidOperationException($"{libelle} doit être comprise entre 1 et 5");
        }
    }

    private static decimal? Moyenne(IEnumerable<int?> notes)
    {
        var valeurs = notes.Where(n => n is not null).Select(n => n!.Value).ToList();
        return valeurs.Count == 0 ? null : Math.Round((decimal)valeurs.Average(), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Premier mot du nom : les avis publics n'affichent que le prénom.</summary>
    public static string Prenom(string nom) =>
        nom.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static AvisDto VersDto(Avis a) => new()
    {
        Id = a.Id,
        ReservationId = a.ReservationId,
        LocationId = a.LocationId,
        AuteurNom = Prenom(a.Auteur?.Nom ?? ""),
        Note = a.Note,
        Proprete = a.Proprete,
        Communication = a.Communication,
        Emplacement = a.Emplacement,
        RapportQualitePrix = a.RapportQualitePrix,
        Commentaire = a.Commentaire,
        DateCreation = a.DateCreation,
        ReponseHote = a.ReponseHote,
        DateReponse = a.DateReponse,
        Sens = a.Sens.ToString()
    };
}
