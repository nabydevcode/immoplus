namespace ImmoPlus.Api.Services;

using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class AppartementServices
{
    private readonly AppDbContext _context;
    private readonly AvisServices _avisServices;
    private readonly ReservationServices _reservationServices;
    private readonly JournalServices _journal;

    public AppartementServices(AppDbContext context, AvisServices avisServices, ReservationServices reservationServices, JournalServices journal)
    {
        _journal = journal;
        _context = context;
        _avisServices = avisServices;
        _reservationServices = reservationServices;
    }

    private const int LongueurMaxAdresse = 300;
    private const int LongueurMaxDescription = 5000;
    private const int LongueurMaxLieu = 100;

    public async Task<AppartementResponseDto> AjouterAppartementAsync(AppartementDto dto)
    {
        var bailleur = await _context.Utilisateurs.FindAsync(dto.BailleurId);
        if (bailleur is null)
        {
            throw new NotFoundException("Ce bailleur n'existe pas, impossible d'ajouter cet appartement");
        }
        if (bailleur.Role != RoleUtilisateur.Bailleur)
        {
            throw new ConflictException("Cet utilisateur n'a pas le rôle Bailleur");
        }
        // Un compte en attente, refusé ou désactivé ne peut pas publier (ses annonces seraient de toute façon masquées).
        if (bailleur.Statut != StatutUtilisateur.Valide)
        {
            throw new ConflictException("Le compte de ce bailleur n'est pas actif : impossible de publier une annonce pour lui");
        }
        VerifierTextes(dto.Adresse, dto.Description);

        if (!Enum.TryParse<TypeOffre>(dto.TypeOffre, true, out var typeOffre))
        {
            throw new InvalidOperationException("TypeOffre invalide. Utilisez LongueDuree ou SejourCourt");
        }

        if (typeOffre == TypeOffre.LongueDuree)
        {
            if (dto.LoyerMensuel is null || dto.LoyerMensuel <= 0 || dto.DureeAvanceExigee is null || dto.DureeAvanceExigee <= 0)
            {
                throw new InvalidOperationException("LoyerMensuel et DureeAvanceExigee sont obligatoires pour une offre LongueDuree");
            }
        }
        else
        {
            if (dto.PrixParNuit is null || dto.PrixParNuit <= 0)
            {
                throw new InvalidOperationException("PrixParNuit est obligatoire pour une offre SejourCourt");
            }
            if (dto.DureeMaxMois is not null && dto.DureeMaxMois <= 0)
            {
                throw new InvalidOperationException("La durée maximale doit être d'au moins 1 mois");
            }
        }

        var appartement = new Appartement
        {
            BailleurId = dto.BailleurId,
            Adresse = dto.Adresse.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Disponible = true,
            TypeOffre = typeOffre,
            LoyerMensuel = typeOffre == TypeOffre.LongueDuree ? dto.LoyerMensuel : null,
            DureeAvanceExigee = typeOffre == TypeOffre.LongueDuree ? dto.DureeAvanceExigee : null,
            PrixParNuit = typeOffre == TypeOffre.SejourCourt ? dto.PrixParNuit : null,
            DureeMaxMois = typeOffre == TypeOffre.SejourCourt ? (dto.DureeMaxMois ?? 1) : null,
            PolitiqueAnnulation = LirePolitique(dto.PolitiqueAnnulation, PolitiqueAnnulation.Moderee),
            DateCreation = DateTime.UtcNow
        };
        AppliquerDetails(appartement, new DetailsAnnonce(dto.Ville, dto.Quartier, dto.Capacite, dto.Chambres, dto.Lits, dto.SallesDeBain,
            dto.Equipements, dto.FraisMenage, dto.Caution, dto.ReductionSemainePct, dto.ReductionMoisPct, dto.NuitsMin, dto.ReservationInstantanee));
        AppliquerArriveeEtPosition(appartement, new ArriveeEtPosition(dto.HeureArrivee, dto.HeureDepart, dto.InstructionsArrivee, dto.Latitude, dto.Longitude));

        await _context.Appartements.AddAsync(appartement);
        await _context.SaveChangesAsync();

        appartement.Bailleur = bailleur;
        await _journal.EnregistrerAsync("appartement.publier", "Appartement", appartement.Id,
            $"Annonce n° {appartement.Id} publiée ({typeOffre}) : {appartement.Adresse} — bailleur {bailleur.Nom}");
        return (await VersDtosAsync([appartement], appelant: (appartement.BailleurId, RoleUtilisateur.Bailleur)))[0];
    }

    /// <param name="utilisateurId">Appelant connecté (facultatif) : ses propres logements sont détaillés (instructions, position exacte).</param>
    public async Task<List<AppartementResponseDto>> ListerAsync(TypeOffre? typeOffre = null, bool? disponible = null,
        RechercheAppartementDto? recherche = null, int? utilisateurId = null, RoleUtilisateur? role = null)
    {
        recherche ??= new RechercheAppartementDto();
        if (typeOffre == TypeOffre.LongueDuree)
        {
            // Dates, voyageurs et prix par nuit n'ont pas de sens pour la longue durée : ignorés.
            recherche.Arrivee = null;
            recherche.Depart = null;
            recherche.Voyageurs = null;
            recherche.PrixMax = null;
        }
        if (recherche.LoyerMax is <= 0 || recherche.AvanceMax is <= 0 || recherche.ChambresMin is < 0)
        {
            throw new InvalidOperationException("Le loyer maximum et l'avance maximum doivent être positifs, le nombre de chambres ne peut pas être négatif");
        }
        if ((recherche.Arrivee is null) != (recherche.Depart is null))
        {
            throw new InvalidOperationException("Indiquez à la fois la date d'arrivée et la date de départ");
        }
        if (recherche.Arrivee is not null && recherche.Depart!.Value.Date <= recherche.Arrivee.Value.Date)
        {
            throw new InvalidOperationException("La date de départ doit être après la date d'arrivée");
        }
        if (recherche.Voyageurs is not null && recherche.Voyageurs < 1)
        {
            throw new InvalidOperationException("Le nombre de voyageurs doit être d'au moins 1");
        }
        var equipementsVoulus = NormaliserEquipements(recherche.Equipements);
        var zone = LireZone(recherche);

        var requete = _context.Appartements.AsNoTracking().Include(a => a.Bailleur).Include(a => a.Photos).AsQueryable();

        if (role != RoleUtilisateur.Admin)
        {
            // Même règle que la fiche : le public ne voit ni les annonces supprimées ni celles d'un hôte désactivé.
            requete = requete.Where(a => (utilisateurId != null && a.BailleurId == utilisateurId)
                || (!a.EstDesactive && a.Bailleur!.Statut != StatutUtilisateur.Desactive));
        }
        if (recherche.BailleurId is not null)
        {
            requete = requete.Where(a => a.BailleurId == recherche.BailleurId);
        }
        if (typeOffre is not null)
        {
            requete = requete.Where(a => a.TypeOffre == typeOffre);
        }
        if (disponible is not null)
        {
            requete = requete.Where(a => a.Disponible == disponible);
            if (disponible == true)
            {
                requete = requete.Where(a => !a.EstDesactive && a.Bailleur!.Statut != StatutUtilisateur.Desactive);
            }
        }
        if (recherche.Voyageurs is not null)
        {
            requete = requete.Where(a => a.Capacite != null && a.Capacite >= recherche.Voyageurs);
        }
        if (recherche.PrixMax is not null)
        {
            requete = requete.Where(a => a.PrixParNuit != null && a.PrixParNuit <= recherche.PrixMax);
        }
        if (recherche.LoyerMax is not null)
        {
            requete = requete.Where(a => a.LoyerMensuel != null && a.LoyerMensuel <= recherche.LoyerMax);
        }
        if (recherche.ChambresMin is not null)
        {
            requete = requete.Where(a => a.Chambres != null && a.Chambres >= recherche.ChambresMin);
        }
        if (recherche.AvanceMax is not null)
        {
            requete = requete.Where(a => a.DureeAvanceExigee != null && a.DureeAvanceExigee <= recherche.AvanceMax);
        }
        if (zone is { } z)
        {
            requete = requete.Where(a => a.Latitude != null && a.Longitude != null && a.Latitude >= z.Sud && a.Latitude <= z.Nord);
            // Une zone qui traverse l'antiméridien (ouest > est) se lit en deux morceaux.
            requete = z.Ouest <= z.Est
                ? requete.Where(a => a.Longitude >= z.Ouest && a.Longitude <= z.Est)
                : requete.Where(a => a.Longitude >= z.Ouest || a.Longitude <= z.Est);
        }

        var appartements = await requete.ToListAsync();

        // Texte et équipements filtrés en mémoire : comparaison sans accents ni casse, impossible à traduire en SQL avec SQLite.
        if (!string.IsNullOrWhiteSpace(recherche.Ville))
        {
            var ville = Simplifier(recherche.Ville);
            appartements = appartements.Where(a => a.Ville is not null && Simplifier(a.Ville).Contains(ville)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(recherche.Quartier))
        {
            var quartier = Simplifier(recherche.Quartier);
            appartements = appartements.Where(a => a.Quartier is not null && Simplifier(a.Quartier).Contains(quartier)).ToList();
        }
        if (equipementsVoulus.Count > 0)
        {
            appartements = appartements.Where(a => equipementsVoulus.All(LireEquipements(a.Equipements).Contains)).ToList();
        }

        if (recherche.Arrivee is not null)
        {
            // Les réservations impayées depuis 24 h ne doivent plus masquer un logement libre.
            await _reservationServices.ExpirerImpayeesAsync();
            var arrivee = recherche.Arrivee.Value.Date;
            var depart = recherche.Depart!.Value.Date;
            var nuits = (depart - arrivee).Days;
            var candidats = appartements.Select(a => a.Id).ToList();
            var occupes = (await _context.Reservations
                    .Where(r => candidats.Contains(r.AppartementId)
                        && r.Statut != StatutReservation.Annulee && r.Statut != StatutReservation.Refusee
                        && arrivee < r.DateDepart && r.DateArrivee < depart)
                    .Select(r => r.AppartementId)
                    .ToListAsync())
                .Concat(await _context.Indisponibilites
                    .Where(i => candidats.Contains(i.AppartementId) && arrivee < i.DateFin && i.DateDebut < depart)
                    .Select(i => i.AppartementId)
                    .ToListAsync())
                .ToHashSet();
            appartements = appartements.Where(a => !occupes.Contains(a.Id) && nuits >= (a.NuitsMin ?? 1)).ToList();
        }

        var ids = appartements.Select(a => a.Id).ToList();
        var loues = (await _context.Locations
            .Where(l => ids.Contains(l.AppartementId) && l.Statut == StatutLocation.EnCours)
            .Select(l => l.AppartementId)
            .ToListAsync()).ToHashSet();

        return await VersDtosAsync(appartements, loues, (utilisateurId, role));
    }

    /// <summary>
    /// Fiche d'un logement, avec les mêmes règles de visibilité que la liste : le public ne voit pas une annonce supprimée
    /// ni celle d'un hôte désactivé (404) ; le propriétaire et l'admin voient tout, en détail.
    /// </summary>
    public async Task<AppartementResponseDto> ObtenirAsync(int id, int? utilisateurId, RoleUtilisateur? role)
    {
        var appartement = await _context.Appartements.AsNoTracking().Include(a => a.Bailleur).Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (appartement is null || !EstVisible(appartement, utilisateurId, role))
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        var loue = await _context.Locations.AnyAsync(l => l.AppartementId == id && l.Statut == StatutLocation.EnCours);
        return (await VersDtosAsync([appartement], loue ? new HashSet<int> { id } : null, (utilisateurId, role)))[0];
    }

    /// <summary>Public : annonce non supprimée d'un hôte non désactivé. Propriétaire et admin : toujours (Bailleur chargé).</summary>
    private static bool EstVisible(Appartement a, int? utilisateurId, RoleUtilisateur? role) =>
        role == RoleUtilisateur.Admin
        || (utilisateurId is not null && a.BailleurId == utilisateurId)
        || (!a.EstDesactive && a.Bailleur?.Statut != StatutUtilisateur.Desactive);

    /// <summary>Logements donnés, dans l'ordre des ids (favoris), au même format que la liste, disponibles ou non.</summary>
    public async Task<List<AppartementResponseDto>> ListerParIdsAsync(List<int> ids, int utilisateurId, RoleUtilisateur role)
    {
        var appartements = await _context.Appartements.AsNoTracking()
            .Include(a => a.Bailleur).Include(a => a.Photos)
            .Where(a => ids.Contains(a.Id))
            .ToListAsync();
        var loues = (await _context.Locations
            .Where(l => ids.Contains(l.AppartementId) && l.Statut == StatutLocation.EnCours)
            .Select(l => l.AppartementId)
            .ToListAsync()).ToHashSet();
        var ordre = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return await VersDtosAsync(appartements.OrderBy(a => ordre[a.Id]).ToList(), loues, (utilisateurId, role));
    }

    private readonly record struct Zone(double Nord, double Sud, double Est, double Ouest);

    private static Zone? LireZone(RechercheAppartementDto r)
    {
        var renseignees = new[] { r.Nord, r.Sud, r.Est, r.Ouest }.Count(v => v is not null);
        if (renseignees == 0)
        {
            return null;
        }
        if (renseignees != 4)
        {
            throw new InvalidOperationException("Indiquez les 4 bornes de la carte : nord, sud, est et ouest");
        }
        if (r.Nord is < -90 or > 90 || r.Sud is < -90 or > 90 || r.Est is < -180 or > 180 || r.Ouest is < -180 or > 180)
        {
            throw new InvalidOperationException("Bornes de la carte invalides (latitude entre -90 et 90, longitude entre -180 et 180)");
        }
        if (r.Sud > r.Nord)
        {
            throw new InvalidOperationException("La borne sud doit être inférieure à la borne nord");
        }
        return new Zone(r.Nord!.Value, r.Sud!.Value, r.Est!.Value, r.Ouest!.Value);
    }

    /// <summary>Villes et quartiers distincts des annonces disponibles (d'un type d'offre, ou toutes), triés, pour les listes déroulantes.</summary>
    public async Task<List<VilleQuartiersDto>> ListerVillesAsync(TypeOffre? typeOffre = null)
    {
        var lieux = await _context.Appartements.AsNoTracking()
            .Where(a => a.Disponible && !a.EstDesactive && a.Bailleur!.Statut != StatutUtilisateur.Desactive && a.Ville != null && a.Ville != "")
            .Where(a => typeOffre == null || a.TypeOffre == typeOffre)
            .Select(a => new { a.Ville, a.Quartier })
            .ToListAsync();

        return lieux
            .GroupBy(l => l.Ville!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new VilleQuartiersDto
            {
                Ville = g.Key,
                Quartiers = g.Where(l => !string.IsNullOrWhiteSpace(l.Quartier))
                    .Select(l => l.Quartier!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(q => q, StringComparer.Create(Fr, true))
                    .ToList()
            })
            .OrderBy(v => v.Ville, StringComparer.Create(Fr, true))
            .ToList();
    }

    /// <summary>
    /// Nuits occupées (réservations actives et dates bloquées) entre du et au (exclu). Public : sans id ni motif ;
    /// le bailleur propriétaire et l'admin voient en plus de quoi il s'agit, pour pouvoir débloquer.
    /// </summary>
    public async Task<List<PeriodeCalendrierDto>> CalendrierAsync(int id, DateTime? du, DateTime? au, int? utilisateurId, RoleUtilisateur? role)
    {
        var appartement = await _context.Appartements.AsNoTracking().Include(a => a.Bailleur).FirstOrDefaultAsync(a => a.Id == id);
        if (appartement is null || !EstVisible(appartement, utilisateurId, role))
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        await _reservationServices.ExpirerImpayeesAsync(r => r.AppartementId == id);
        var debut = (du ?? DateTime.UtcNow.Date).Date;
        var fin = (au ?? debut.AddMonths(6)).Date;
        if (fin <= debut)
        {
            throw new InvalidOperationException("La date « au » doit être après la date « du »");
        }
        if (fin > debut.AddYears(2))
        {
            throw new InvalidOperationException("La période demandée ne peut pas dépasser 2 ans");
        }
        var detaille = role == RoleUtilisateur.Admin || (role == RoleUtilisateur.Bailleur && appartement.BailleurId == utilisateurId);

        var reservations = await _context.Reservations.AsNoTracking()
            .Include(r => r.Locataire)
            .Where(r => r.AppartementId == id
                && r.Statut != StatutReservation.Annulee && r.Statut != StatutReservation.Refusee
                && debut < r.DateDepart && r.DateArrivee < fin)
            .ToListAsync();
        var indisponibilites = await _context.Indisponibilites.AsNoTracking()
            .Where(i => i.AppartementId == id && debut < i.DateFin && i.DateDebut < fin)
            .ToListAsync();

        return reservations.Select(r => new PeriodeCalendrierDto
            {
                Debut = DateOnly.FromDateTime(r.DateArrivee),
                Fin = DateOnly.FromDateTime(r.DateDepart),
                Type = "Reservee",
                Id = detaille ? r.Id : null,
                Motif = detaille ? $"Réservation n° {r.Id:D6} – {r.Locataire?.Nom} ({LibelleStatut(r.Statut)})" : null
            })
            .Concat(indisponibilites.Select(i => VersPeriode(i, detaille)))
            .OrderBy(p => p.Debut)
            .ToList();
    }

    public async Task<PeriodeCalendrierDto> BloquerDatesAsync(int appartementId, IndisponibiliteDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await _context.Appartements.FirstOrDefaultAsync(a => a.Id == appartementId)
            ?? throw new NotFoundException("Cet appartement n'existe pas");
        if (role != RoleUtilisateur.Admin && appartement.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez gérer le calendrier que de vos propres logements");
        }
        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            throw new InvalidOperationException("Le calendrier ne concerne que les séjours courts");
        }
        var debut = dto.DateDebut.Date;
        var fin = dto.DateFin.Date;
        if (fin <= debut)
        {
            throw new InvalidOperationException("La date de fin doit être après la date de début");
        }
        if (debut < DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("Impossible de bloquer des dates passées");
        }
        var reservation = await _context.Reservations
            .Where(r => r.AppartementId == appartementId
                && r.Statut != StatutReservation.Annulee && r.Statut != StatutReservation.Refusee
                && debut < r.DateDepart && r.DateArrivee < fin)
            .OrderBy(r => r.DateArrivee)
            .FirstOrDefaultAsync();
        if (reservation is not null)
        {
            throw new ConflictException($"Ces dates chevauchent la réservation n° {reservation.Id:D6} "
                + $"(du {reservation.DateArrivee:dd/MM/yyyy} au {reservation.DateDepart:dd/MM/yyyy})");
        }

        var indisponibilite = new Indisponibilite
        {
            AppartementId = appartementId,
            DateDebut = debut,
            DateFin = fin,
            Motif = string.IsNullOrWhiteSpace(dto.Motif) ? null : dto.Motif.Trim()
        };
        await _context.Indisponibilites.AddAsync(indisponibilite);
        await _context.SaveChangesAsync();
        return VersPeriode(indisponibilite, detaille: true);
    }

    public async Task DebloquerDatesAsync(int indisponibiliteId, int utilisateurId, RoleUtilisateur role)
    {
        var indisponibilite = await _context.Indisponibilites.Include(i => i.Appartement).FirstOrDefaultAsync(i => i.Id == indisponibiliteId)
            ?? throw new NotFoundException("Ces dates bloquées n'existent pas");
        if (role != RoleUtilisateur.Admin && indisponibilite.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez gérer le calendrier que de vos propres logements");
        }
        _context.Indisponibilites.Remove(indisponibilite);
        await _context.SaveChangesAsync();
    }

    private static PeriodeCalendrierDto VersPeriode(Indisponibilite i, bool detaille) => new()
    {
        Debut = DateOnly.FromDateTime(i.DateDebut),
        Fin = DateOnly.FromDateTime(i.DateFin),
        Type = "Bloquee",
        Id = detaille ? i.Id : null,
        Motif = detaille ? (i.Motif ?? "Dates bloquées par l'hôte") : null
    };

    private static string LibelleStatut(StatutReservation statut) => statut switch
    {
        StatutReservation.EnAttente => "en attente de paiement",
        StatutReservation.Confirmee => "confirmée",
        StatutReservation.Terminee => "terminée",
        StatutReservation.DemandeEnvoyee => "demande à traiter",
        _ => statut.ToString()
    };

    public async Task<AppartementResponseDto> ModifierAsync(int id, ModifierAppartementDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await _context.Appartements.Include(a => a.Bailleur).Include(a => a.Photos).FirstOrDefaultAsync(a => a.Id == id);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && appartement.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez modifier que vos propres logements");
        }
        // Le locataire en place a signé sur ces conditions : l'annonce est figée tant que la location dure.
        if (await _context.Locations.AnyAsync(l => l.AppartementId == id && l.Statut == StatutLocation.EnCours))
        {
            throw new ConflictException("Ce logement est en cours de location : il ne peut pas être modifié tant que la location dure");
        }
        VerifierTextes(dto.Adresse, dto.Description);

        if (appartement.TypeOffre == TypeOffre.LongueDuree)
        {
            if (dto.LoyerMensuel is null || dto.LoyerMensuel <= 0 || dto.DureeAvanceExigee is null || dto.DureeAvanceExigee <= 0)
            {
                throw new InvalidOperationException("Le loyer mensuel et la durée d'avance sont obligatoires");
            }
            // Les candidats en attente ont postulé sur ces conditions, et l'acceptation les utilise pour calculer le financement.
            var conditionsModifiees = dto.LoyerMensuel != appartement.LoyerMensuel || dto.DureeAvanceExigee != appartement.DureeAvanceExigee;
            if (conditionsModifiees && await _context.Candidatures.AnyAsync(c => c.AppartementId == id
                && (c.Statut == StatutCandidature.EnAttente || c.Statut == StatutCandidature.Preselectionnee)))
            {
                throw new ConflictException("Des candidatures sont en cours d'étude : traitez-les avant de modifier le loyer ou l'avance");
            }
            appartement.LoyerMensuel = dto.LoyerMensuel;
            appartement.DureeAvanceExigee = dto.DureeAvanceExigee;
        }
        else
        {
            if (dto.PrixParNuit is null || dto.PrixParNuit <= 0)
            {
                throw new InvalidOperationException("Le prix par nuit est obligatoire");
            }
            if (dto.DureeMaxMois is not null && dto.DureeMaxMois <= 0)
            {
                throw new InvalidOperationException("La durée maximale doit être d'au moins 1 mois");
            }
            // Les réservations existantes gardent le montant calculé à leur création.
            appartement.PrixParNuit = dto.PrixParNuit;
            appartement.DureeMaxMois = dto.DureeMaxMois ?? 1;
            // Les réservations existantes gardent la politique en vigueur lors de leur création.
            appartement.PolitiqueAnnulation = LirePolitique(dto.PolitiqueAnnulation, appartement.PolitiqueAnnulation);
        }

        AppliquerDetails(appartement, new DetailsAnnonce(dto.Ville, dto.Quartier, dto.Capacite, dto.Chambres, dto.Lits, dto.SallesDeBain,
            dto.Equipements, dto.FraisMenage, dto.Caution, dto.ReductionSemainePct, dto.ReductionMoisPct, dto.NuitsMin, dto.ReservationInstantanee));
        AppliquerArriveeEtPosition(appartement, new ArriveeEtPosition(dto.HeureArrivee, dto.HeureDepart, dto.InstructionsArrivee, dto.Latitude, dto.Longitude));
        appartement.Adresse = dto.Adresse.Trim();
        appartement.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        appartement.Disponible = dto.Disponible;

        await _context.SaveChangesAsync();
        await _journal.EnregistrerAsync("appartement.modifier", "Appartement", id,
            $"Annonce n° {id} modifiée : {appartement.Adresse}{(appartement.Disponible ? "" : " (marquée indisponible)")}");
        return (await VersDtosAsync([appartement], appelant: (utilisateurId, role)))[0];
    }

    /// <summary>
    /// « Suppression » d'une annonce : elle est désactivée, jamais effacée. Elle disparaît des annonces et des recherches,
    /// mais son historique (candidatures, locations, réservations, avis) et ses photos restent consultables, et elle peut être réactivée.
    /// </summary>
    public async Task SupprimerAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await ChargerPourGestionAsync(id, utilisateurId, role);
        if (appartement.EstDesactive)
        {
            throw new ConflictException("Ce logement est déjà supprimé (désactivé)");
        }

        var aujourdhui = DateTime.UtcNow.Date;
        if (await _context.Locations.AnyAsync(l => l.AppartementId == id && l.Statut == StatutLocation.EnCours))
        {
            throw new ConflictException("Une location est en cours sur ce logement : il ne peut pas être supprimé avant la fin de la location");
        }
        var reservationsActives = await _context.Reservations.CountAsync(r => r.AppartementId == id
            && (r.Statut == StatutReservation.DemandeEnvoyee || r.Statut == StatutReservation.EnAttente || r.Statut == StatutReservation.Confirmee)
            && r.DateDepart >= aujourdhui);
        if (reservationsActives > 0)
        {
            throw new ConflictException($"{reservationsActives} réservation(s) à venir ou en cours sur ce logement : "
                + "refusez ou annulez-les d'abord (le voyageur doit être prévenu et remboursé)");
        }
        var candidaturesOuvertes = await _context.Candidatures.CountAsync(c => c.AppartementId == id
            && (c.Statut == StatutCandidature.EnAttente || c.Statut == StatutCandidature.Preselectionnee
                || c.Statut == StatutCandidature.ListeAttente || c.Statut == StatutCandidature.Acceptee));
        if (candidaturesOuvertes > 0)
        {
            throw new ConflictException($"{candidaturesOuvertes} candidature(s) en cours sur ce logement : "
                + "refusez-les ou clôturez la liste d'attente d'abord, pour que les candidats soient prévenus");
        }

        appartement.EstDesactive = true;
        appartement.DateDesactivation = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _journal.EnregistrerAsync("appartement.supprimer", "Appartement", id, $"Annonce n° {id} supprimée (désactivée) : {appartement.Adresse}");
    }

    public async Task<AppartementResponseDto> ReactiverAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await ChargerPourGestionAsync(id, utilisateurId, role);
        if (!appartement.EstDesactive)
        {
            throw new ConflictException("Ce logement n'est pas supprimé");
        }
        appartement.EstDesactive = false;
        appartement.DateDesactivation = null;
        await _context.SaveChangesAsync();
        await _journal.EnregistrerAsync("appartement.reactiver", "Appartement", id, $"Annonce n° {id} réactivée : {appartement.Adresse}");
        return await ObtenirAsync(id, utilisateurId, role);
    }

    private async Task<Appartement> ChargerPourGestionAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await _context.Appartements.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new NotFoundException("Cet appartement n'existe pas");
        if (role != RoleUtilisateur.Admin && appartement.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez gérer que vos propres logements");
        }
        return appartement;
    }

    /// <summary>Ajoute les notes des logements et les badges de leurs hôtes, calculés en quelques requêtes groupées.</summary>
    /// <param name="appelant">Le propriétaire et l'admin voient les instructions d'arrivée, la position exacte et le nombre de favoris ;
    /// le public, une position arrondie (≈ 100 m) et rien de confidentiel.</param>
    private async Task<List<AppartementResponseDto>> VersDtosAsync(List<Appartement> appartements, HashSet<int>? loues = null,
        (int? Id, RoleUtilisateur? Role) appelant = default)
    {
        var ids = appartements.Select(a => a.Id).ToList();
        var notes = await _avisServices.NotesAppartementsAsync(ids);
        var hotes = await _avisServices.StatistiquesHotesAsync(appartements.Select(a => a.BailleurId));
        bool Detaille(Appartement a) => appelant.Role == RoleUtilisateur.Admin
            || (appelant.Role == RoleUtilisateur.Bailleur && a.BailleurId == appelant.Id);
        var idsDetailles = appartements.Where(Detaille).Select(a => a.Id).ToList();
        var favoris = idsDetailles.Count == 0 ? new Dictionary<int, int>() : (await _context.Favoris
                .Where(f => idsDetailles.Contains(f.AppartementId))
                .GroupBy(f => f.AppartementId)
                .Select(g => new { AppartementId = g.Key, Nombre = g.Count() })
                .ToListAsync())
            .ToDictionary(f => f.AppartementId, f => f.Nombre);
        return appartements.Select(a =>
        {
            var dto = VersDto(a, a.Bailleur?.Nom ?? "", loues?.Contains(a.Id) ?? false);
            if (Detaille(a))
            {
                dto.InstructionsArrivee = a.TypeOffre == TypeOffre.SejourCourt ? a.InstructionsArrivee : null;
                dto.NombreFavoris = favoris.GetValueOrDefault(a.Id);
                // Photos refusées au public (404) : le front doit les charger avec le jeton.
                dto.PhotosProtegees = PhotosProtegees(a);
                dto.BailleurDesactive = a.Bailleur?.Statut == StatutUtilisateur.Desactive;
            }
            else
            {
                dto.Latitude = Arrondir(a.Latitude);
                dto.Longitude = Arrondir(a.Longitude);
                // Séjour court : la rue n'est communiquée qu'au voyageur confirmé (la longue durée garde l'adresse, utile pour la visite).
                if (a.TypeOffre == TypeOffre.SejourCourt)
                {
                    dto.Adresse = AdresseApproximative(a);
                    dto.AdresseMasquee = true;
                }
            }
            if (notes.TryGetValue(a.Id, out var note))
            {
                dto.NoteMoyenne = note.Moyenne;
                dto.NombreAvis = note.Nombre;
            }
            dto.Hote = hotes.GetValueOrDefault(a.BailleurId);
            return dto;
        }).ToList();
    }

    /// <summary>« Flexible », « Moderee » ou « Stricte » (casse ignorée) ; valeur par défaut si rien n'est fourni.</summary>
    private static PolitiqueAnnulation LirePolitique(string? valeur, PolitiqueAnnulation defaut)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return defaut;
        }
        if (!Enum.TryParse<PolitiqueAnnulation>(valeur.Trim(), true, out var politique) || !Enum.IsDefined(politique))
        {
            throw new InvalidOperationException("Politique d'annulation invalide. Utilisez Flexible, Moderee ou Stricte");
        }
        return politique;
    }

    private static AppartementResponseDto VersDto(Appartement appartement, string bailleurNom, bool enLocation = false) => new()
    {
        Id = appartement.Id,
        BailleurId = appartement.BailleurId,
        BailleurNom = bailleurNom,
        Adresse = appartement.Adresse,
        Description = appartement.Description,
        Disponible = appartement.Disponible,
        EstDesactive = appartement.EstDesactive,
        DateDesactivation = appartement.DateDesactivation,
        EnLocation = enLocation,
        Photos = appartement.Photos.OrderBy(p => p.Est360).ThenBy(p => p.Ordre).Select(PhotoServices.VersDto).ToList(),
        TypeOffre = appartement.TypeOffre.ToString(),
        Ville = appartement.Ville,
        Quartier = appartement.Quartier,
        Capacite = appartement.Capacite,
        Chambres = appartement.Chambres,
        Lits = appartement.Lits,
        SallesDeBain = appartement.SallesDeBain,
        Equipements = LireEquipements(appartement.Equipements),
        LoyerMensuel = appartement.LoyerMensuel,
        DureeAvanceExigee = appartement.DureeAvanceExigee,
        PrixParNuit = appartement.PrixParNuit,
        DureeMaxMois = appartement.DureeMaxMois,
        FraisMenage = appartement.FraisMenage,
        Caution = appartement.Caution,
        ReductionSemainePct = appartement.ReductionSemainePct,
        ReductionMoisPct = appartement.ReductionMoisPct,
        NuitsMin = appartement.TypeOffre == TypeOffre.SejourCourt ? (appartement.NuitsMin ?? 1) : null,
        ReservationInstantanee = appartement.ReservationInstantanee,
        PolitiqueAnnulation = appartement.TypeOffre == TypeOffre.SejourCourt ? appartement.PolitiqueAnnulation.ToString() : null,
        HeureArrivee = HeureArriveeDe(appartement),
        HeureDepart = HeureDepartDe(appartement),
        // Position exacte et instructions : ajoutées par VersDtosAsync selon l'appelant.
        Latitude = appartement.Latitude,
        Longitude = appartement.Longitude
    };

    /// <summary>« Quartier, Ville » (ou ce qui est connu) : situe un séjour court sans donner la rue avant la confirmation.</summary>
    public static string AdresseApproximative(Appartement a)
    {
        var parties = new[] { a.Quartier, a.Ville }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return parties.Count > 0 ? string.Join(", ", parties) : "Adresse communiquée après confirmation";
    }

    /// <summary>Logement supprimé ou hôte désactivé : ses photos ne sont plus publiques (propriétaire et admin seulement).</summary>
    public static bool PhotosProtegees(Appartement a) => a.EstDesactive || a.Bailleur?.Statut == StatutUtilisateur.Desactive;

    public const string HeureArriveeParDefaut = "14:00";
    public const string HeureDepartParDefaut = "11:00";
    private const int LongueurMaxInstructions = 2000;

    public static string? HeureArriveeDe(Appartement a) =>
        a.TypeOffre == TypeOffre.SejourCourt ? a.HeureArrivee ?? HeureArriveeParDefaut : null;
    public static string? HeureDepartDe(Appartement a) =>
        a.TypeOffre == TypeOffre.SejourCourt ? a.HeureDepart ?? HeureDepartParDefaut : null;

    /// <summary>3 décimales ≈ 100 m : assez pour situer le quartier, pas la porte.</summary>
    private static double? Arrondir(double? coordonnee) => coordonnee is { } c ? Math.Round(c, 3, MidpointRounding.AwayFromZero) : null;

    private readonly record struct ArriveeEtPosition(string? HeureArrivee, string? HeureDepart, string? Instructions, double? Latitude, double? Longitude);

    /// <summary>Heures et instructions d'arrivée (séjour court uniquement) et position sur la carte (tous types).</summary>
    private static void AppliquerArriveeEtPosition(Appartement appartement, ArriveeEtPosition d)
    {
        if ((d.Latitude is null) != (d.Longitude is null))
        {
            throw new InvalidOperationException("Indiquez à la fois la latitude et la longitude (ou aucune des deux)");
        }
        if (d.Latitude is < -90 or > 90 || d.Longitude is < -180 or > 180 || d.Latitude is double.NaN || d.Longitude is double.NaN)
        {
            throw new InvalidOperationException("Position invalide : latitude entre -90 et 90, longitude entre -180 et 180");
        }
        appartement.Latitude = d.Latitude;
        appartement.Longitude = d.Longitude;

        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            appartement.HeureArrivee = null;
            appartement.HeureDepart = null;
            appartement.InstructionsArrivee = null;
            return;
        }
        appartement.HeureArrivee = LireHeure(d.HeureArrivee, "L'heure d'arrivée");
        appartement.HeureDepart = LireHeure(d.HeureDepart, "L'heure de départ");
        var instructions = Nettoyer(d.Instructions);
        if (instructions?.Length > LongueurMaxInstructions)
        {
            throw new InvalidOperationException($"Les instructions d'arrivée ne doivent pas dépasser {LongueurMaxInstructions} caractères");
        }
        appartement.InstructionsArrivee = instructions;
    }

    /// <summary>« HH:mm » normalisé (« 9:30 » → « 09:30 ») ; null si vide (heure par défaut).</summary>
    private static string? LireHeure(string? valeur, string libelle)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return null;
        }
        if (!TimeOnly.TryParseExact(valeur.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var heure))
        {
            throw new InvalidOperationException($"{libelle} doit être au format HH:mm (ex. 14:00)");
        }
        return heure.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Équipements reconnus par le front (icônes et libellés) : toute autre clé est refusée.</summary>
    public static readonly string[] EquipementsAutorises =
    [
        "climatisation", "groupe-electrogene", "wifi", "eau-courante", "parking", "gardien",
        "cuisine", "television", "lave-linge", "piscine", "balcon", "eau-chaude"
    ];

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly record struct DetailsAnnonce(string? Ville, string? Quartier, int? Capacite, int? Chambres, int? Lits, int? SallesDeBain,
        List<string>? Equipements, decimal? FraisMenage, decimal? Caution, int? ReductionSemainePct, int? ReductionMoisPct, int? NuitsMin,
        bool ReservationInstantanee);

    /// <summary>Valide et applique les champs enrichis communs à la création et à la modification d'une annonce.</summary>
    private static void AppliquerDetails(Appartement appartement, DetailsAnnonce d)
    {
        if (d.Capacite is not null && d.Capacite < 1)
        {
            throw new InvalidOperationException("La capacité doit être d'au moins 1 voyageur");
        }
        if (d.Chambres < 0 || d.Lits < 0 || d.SallesDeBain < 0)
        {
            throw new InvalidOperationException("Le nombre de chambres, de lits et de salles de bain ne peut pas être négatif");
        }
        if (d.Ville?.Trim().Length > LongueurMaxLieu || d.Quartier?.Trim().Length > LongueurMaxLieu)
        {
            throw new InvalidOperationException($"La ville et le quartier ne doivent pas dépasser {LongueurMaxLieu} caractères");
        }
        var equipements = NormaliserEquipements(d.Equipements);

        appartement.Ville = Nettoyer(d.Ville);
        appartement.Quartier = Nettoyer(d.Quartier);
        appartement.Capacite = d.Capacite;
        appartement.Chambres = d.Chambres;
        appartement.Lits = d.Lits;
        appartement.SallesDeBain = d.SallesDeBain;
        appartement.Equipements = equipements.Count == 0 ? null : string.Join(',', equipements);

        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            // Ces conditions n'ont de sens que pour une réservation à la nuit.
            appartement.FraisMenage = null;
            appartement.Caution = null;
            appartement.ReductionSemainePct = null;
            appartement.ReductionMoisPct = null;
            appartement.NuitsMin = null;
            appartement.ReservationInstantanee = false;
            return;
        }
        if (d.FraisMenage < 0 || d.Caution < 0)
        {
            throw new InvalidOperationException("Les frais de ménage et la caution ne peuvent pas être négatifs");
        }
        if (d.ReductionSemainePct is < 0 or > 50)
        {
            throw new InvalidOperationException("La réduction à la semaine doit être comprise entre 0 et 50 %");
        }
        if (d.ReductionMoisPct is < 0 or > 70)
        {
            throw new InvalidOperationException("La réduction au mois doit être comprise entre 0 et 70 %");
        }
        if (d.NuitsMin is not null && d.NuitsMin < 1)
        {
            throw new InvalidOperationException("Le nombre minimum de nuits doit être d'au moins 1");
        }
        appartement.FraisMenage = d.FraisMenage;
        appartement.Caution = d.Caution;
        appartement.ReductionSemainePct = d.ReductionSemainePct;
        appartement.ReductionMoisPct = d.ReductionMoisPct;
        appartement.NuitsMin = d.NuitsMin ?? 1;
        appartement.ReservationInstantanee = d.ReservationInstantanee;
    }

    /// <summary>Clés en minuscules, sans doublon ; 400 si une clé est inconnue.</summary>
    public static List<string> NormaliserEquipements(IEnumerable<string>? equipements)
    {
        var cles = (equipements ?? Enumerable.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        var inconnues = cles.Where(c => !EquipementsAutorises.Contains(c)).ToList();
        if (inconnues.Count > 0)
        {
            throw new InvalidOperationException($"Équipement(s) inconnu(s) : {string.Join(", ", inconnues)}. "
                + $"Valeurs autorisées : {string.Join(", ", EquipementsAutorises)}");
        }
        return cles;
    }

    private static List<string> LireEquipements(string? valeur) =>
        string.IsNullOrEmpty(valeur) ? new List<string>() : valeur.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string? Nettoyer(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();

    /// <summary>Adresse obligatoire ; longueurs bornées (la base SQLite n'impose aucune limite).</summary>
    private static void VerifierTextes(string? adresse, string? description)
    {
        if (string.IsNullOrWhiteSpace(adresse))
        {
            throw new InvalidOperationException("L'adresse est obligatoire");
        }
        if (adresse.Trim().Length > LongueurMaxAdresse)
        {
            throw new InvalidOperationException($"L'adresse ne doit pas dépasser {LongueurMaxAdresse} caractères");
        }
        if (description?.Trim().Length > LongueurMaxDescription)
        {
            throw new InvalidOperationException($"La description ne doit pas dépasser {LongueurMaxDescription} caractères");
        }
    }

    /// <summary>Minuscules sans accents : « Kipé » et « kipe » se retrouvent.</summary>
    private static string Simplifier(string valeur)
    {
        var decompose = valeur.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decompose.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
