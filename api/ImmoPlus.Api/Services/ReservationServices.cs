namespace ImmoPlus.Api.Services;

using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class ReservationServices
{
    private readonly AppDbContext _context;
    private readonly NotificationServices _notificationServices;

    public ReservationServices(AppDbContext context, NotificationServices notificationServices)
    {
        _context = context;
        _notificationServices = notificationServices;
    }

    public const string MotifSansReponse = "Sans réponse de l'hôte sous 48 h";
    private static readonly TimeSpan DelaiReponseHote = TimeSpan.FromHours(48);

    public const string MotifPaiementNonRecu = "Paiement non reçu sous 24 h";
    /// <summary>Délai de paiement d'une réservation acceptée (ou instantanée) avant annulation automatique : les dates ne restent pas bloquées.</summary>
    public static readonly TimeSpan DelaiPaiement = TimeSpan.FromHours(24);
    public const string MotifHoteDesactive = "Compte de l'hôte désactivé";

    /// <summary>Prix détaillé et disponibilité d'un séjour, sans rien réserver (public).</summary>
    public async Task<DevisReservationDto> DevisAsync(int appartementId, DateTime? arrivee, DateTime? depart, int? voyageurs)
    {
        var appartement = await _context.Appartements.AsNoTracking().Include(a => a.Bailleur).FirstOrDefaultAsync(a => a.Id == appartementId)
            ?? throw new NotFoundException("Cet appartement n'existe pas");
        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            throw new InvalidOperationException("Cette annonce est une location longue durée : pas de réservation à la nuit");
        }
        await ExpirerImpayeesAsync(r => r.AppartementId == appartementId);
        return (await CalculerAsync(appartement, arrivee, depart, voyageurs ?? 1)).Devis;
    }

    /// <summary>
    /// Calcul unique du prix et des contrôles de disponibilité, partagé par le devis et la création :
    /// le prix annoncé est exactement celui réservé. Erreur de saisie (dates, voyageurs) → 400 à la création, conflit → 409.
    /// </summary>
    private async Task<(DevisReservationDto Devis, bool ErreurSaisie)> CalculerAsync(Appartement appartement, DateTime? arrivee, DateTime? depart, int voyageurs)
    {
        var devis = new DevisReservationDto
        {
            PrixParNuit = appartement.PrixParNuit ?? 0,
            FraisMenage = appartement.FraisMenage ?? 0,
            Caution = appartement.Caution ?? 0,
            ReservationInstantanee = appartement.ReservationInstantanee
        };
        (DevisReservationDto, bool) Refus(string raison, bool saisie)
        {
            devis.Disponible = false;
            devis.Raison = raison;
            return (devis, saisie);
        }

        if (arrivee is null || depart is null)
        {
            return Refus("Choisissez vos dates d'arrivée et de départ", true);
        }
        var debut = arrivee.Value.Date;
        var fin = depart.Value.Date;
        if (fin <= debut)
        {
            return Refus("La date de départ doit être après la date d'arrivée", true);
        }

        // Le prix est calculé dès que les dates sont cohérentes, même si le séjour est refusé ensuite : le front l'affiche quand même.
        var nuits = (fin - debut).Days;
        var sousTotal = devis.PrixParNuit * nuits;
        var reductionPct = nuits >= 28 && appartement.ReductionMoisPct > 0 ? appartement.ReductionMoisPct!.Value
            : nuits >= 7 && appartement.ReductionSemainePct > 0 ? appartement.ReductionSemainePct!.Value
            : 0;
        var reductionMontant = Math.Round(sousTotal * reductionPct / 100m, 0, MidpointRounding.AwayFromZero);
        devis.NombreNuits = nuits;
        devis.SousTotal = sousTotal;
        devis.ReductionPct = reductionPct;
        devis.ReductionMontant = reductionMontant;
        devis.MontantTotal = Math.Round(sousTotal - reductionMontant + devis.FraisMenage, 0, MidpointRounding.AwayFromZero);

        if (!appartement.Disponible || appartement.EstDesactive || appartement.Bailleur?.Statut == StatutUtilisateur.Desactive)
        {
            return Refus("Ce logement n'est plus disponible à la réservation", false);
        }
        if (debut < DateTime.UtcNow.Date)
        {
            return Refus("La date d'arrivée ne peut pas être dans le passé", true);
        }
        var nuitsMin = appartement.NuitsMin ?? 1;
        if (nuits < nuitsMin)
        {
            return Refus($"Ce logement se réserve pour {nuitsMin} nuits minimum", true);
        }
        var dureeMaxMois = appartement.DureeMaxMois ?? 1;
        if (fin > debut.AddMonths(dureeMaxMois))
        {
            return Refus($"Ce séjour est limité à {dureeMaxMois} mois maximum", false);
        }
        if (voyageurs < 1)
        {
            return Refus("Le nombre de voyageurs doit être d'au moins 1", true);
        }
        if (appartement.Capacite is not null && voyageurs > appartement.Capacite)
        {
            return Refus($"Ce logement accueille {appartement.Capacite} voyageur{(appartement.Capacite > 1 ? "s" : "")} maximum", false);
        }
        var reserve = await _context.Reservations.AnyAsync(r =>
            r.AppartementId == appartement.Id &&
            r.Statut != StatutReservation.Annulee && r.Statut != StatutReservation.Refusee &&
            debut < r.DateDepart && r.DateArrivee < fin);
        if (reserve)
        {
            return Refus("Ce logement est déjà réservé sur une partie de cette période", false);
        }
        var bloque = await _context.Indisponibilites.AnyAsync(i => i.AppartementId == appartement.Id && debut < i.DateFin && i.DateDebut < fin);
        if (bloque)
        {
            return Refus("L'hôte n'accepte pas de réservation sur une partie de cette période", false);
        }

        devis.Disponible = true;
        return (devis, false);
    }

    public async Task<ReservationResponseDto> CreerAsync(int locataireId, ReservationDto dto)
    {
        var appartement = await _context.Appartements.Include(a => a.Bailleur).FirstOrDefaultAsync(a => a.Id == dto.AppartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            throw new ConflictException("Cette annonce est une location longue durée : passez par une candidature");
        }

        // Réservations impayées depuis plus de 24 h sur ce logement : annulées d'abord, leurs dates redeviennent réservables.
        await ExpirerImpayeesAsync(r => r.AppartementId == appartement.Id);

        // Contrôle des disponibilités et création d'un seul tenant (SQLite : verrou d'écriture dès le début de la transaction) :
        // deux demandes simultanées sur les mêmes dates (double clic, deux voyageurs) ne peuvent pas aboutir toutes les deux.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var (devis, erreurSaisie) = await CalculerAsync(appartement, dto.DateArrivee, dto.DateDepart, dto.NombreVoyageurs);
        if (!devis.Disponible)
        {
            throw erreurSaisie ? new InvalidOperationException(devis.Raison) : new ConflictException(devis.Raison!);
        }

        // Sans réservation instantanée, l'hôte valide d'abord la demande ; le paiement n'est possible qu'ensuite.
        var reservation = new Reservation
        {
            LocataireId = locataireId,
            AppartementId = dto.AppartementId,
            DateArrivee = dto.DateArrivee.Date,
            DateDepart = dto.DateDepart.Date,
            NombreVoyageurs = dto.NombreVoyageurs,
            NombreNuits = devis.NombreNuits,
            PrixParNuit = devis.PrixParNuit,
            ReductionMontant = devis.ReductionMontant,
            FraisMenage = devis.FraisMenage,
            Caution = devis.Caution,
            MontantTotal = devis.MontantTotal,
            PolitiqueAnnulation = appartement.PolitiqueAnnulation,
            Statut = appartement.ReservationInstantanee ? StatutReservation.EnAttente : StatutReservation.DemandeEnvoyee,
            DateCreation = DateTime.UtcNow
        };

        await _context.Reservations.AddAsync(reservation);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var locataire = await _context.Utilisateurs.FindAsync(locataireId);
        if (reservation.Statut == StatutReservation.DemandeEnvoyee)
        {
            await _notificationServices.DemandeReservationAsync(locataire!, reservation, appartement);
            await _notificationServices.DemandeReservationBailleurAsync(appartement.Bailleur!, reservation, appartement, locataire!);
        }
        else
        {
            await _notificationServices.ReservationCreeeAsync(locataire!, reservation, appartement);
            await _notificationServices.ReservationCreeeBailleurAsync(appartement.Bailleur!, reservation, appartement, locataire!);
        }

        return await RechercherAsync(reservation.Id, locataireId, RoleUtilisateur.Locataire);
    }

    /// <summary>L'hôte (ou l'admin) accepte la demande : le voyageur peut alors payer pour confirmer.</summary>
    public async Task<ReservationResponseDto> AccepterAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await ChargerDemandeAsync(id, utilisateurId, role);
        // Accepter un séjour déjà commencé ouvrirait un paiement pour des nuits passées.
        if (reservation.DateArrivee.Date < DateTime.UtcNow.Date)
        {
            throw new ConflictException("La date d'arrivée de cette demande est passée : elle ne peut plus être acceptée, refusez-la");
        }
        reservation.Statut = StatutReservation.EnAttente;
        reservation.DateReponseHote = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _notificationServices.DemandeAccepteeAsync(reservation.Locataire!, reservation, reservation.Appartement!);
        return await RechercherAsync(id, utilisateurId, role);
    }

    public async Task<ReservationResponseDto> RefuserAsync(int id, string? motif, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await ChargerDemandeAsync(id, utilisateurId, role);
        reservation.Statut = StatutReservation.Refusee;
        reservation.MotifRefus = LireMotif(motif);
        reservation.DateReponseHote = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _notificationServices.DemandeRefuseeAsync(reservation.Locataire!, reservation, reservation.Appartement!);
        return await RechercherAsync(id, utilisateurId, role);
    }

    /// <summary>
    /// Demandes restées sans réponse de l'hôte depuis 48 h : refusées automatiquement pour libérer les dates
    /// et prévenir le voyageur. Appelé par les rappels planifiés.
    /// </summary>
    public async Task<int> ExpirerDemandesAsync(CancellationToken annulation = default)
    {
        var limite = DateTime.UtcNow - DelaiReponseHote;
        var demandes = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement)
            .Where(r => r.Statut == StatutReservation.DemandeEnvoyee && r.DateCreation <= limite)
            .ToListAsync(annulation);
        foreach (var r in demandes)
        {
            r.Statut = StatutReservation.Refusee;
            r.MotifRefus = MotifSansReponse;
        }
        await _context.SaveChangesAsync(annulation);

        foreach (var r in demandes)
        {
            await _notificationServices.DemandeRefuseeAsync(r.Locataire!, r, r.Appartement!);
        }
        return demandes.Count;
    }

    /// <summary>Échéance de paiement d'une réservation en attente sans aucun paiement (null sinon), en UTC.</summary>
    public static DateTime? PayerAvant(Reservation r, decimal montantPaye) =>
        r.Statut == StatutReservation.EnAttente && montantPaye <= 0
            ? DateTime.SpecifyKind((r.DateReponseHote ?? r.DateCreation) + DelaiPaiement, DateTimeKind.Utc)
            : null;

    /// <summary>
    /// Réservations en attente de paiement depuis 24 h (après l'acceptation de l'hôte, ou la création si elle est instantanée),
    /// sans aucun paiement ni paiement mobile en cours de validation : annulées par le système pour libérer les dates,
    /// avec un email au voyageur et à l'hôte. Appelé à la lecture des réservations et par les rappels quotidiens.
    /// </summary>
    /// <param name="perimetre">Limite le traitement aux réservations concernées par la lecture en cours (toutes si null).</param>
    public async Task<int> ExpirerImpayeesAsync(Expression<Func<Reservation, bool>>? perimetre = null, CancellationToken annulation = default)
    {
        var maintenant = DateTime.UtcNow;
        var limite = maintenant - DelaiPaiement;
        var limiteTransaction = maintenant - PaiementMobileServices.DureeValidite;
        var requete = _context.Reservations.Where(r => r.Statut == StatutReservation.EnAttente
            && (r.DateReponseHote ?? r.DateCreation) <= limite
            && !r.Paiements.Any()
            && !r.TransactionsMobiles.Any(t => t.Statut == StatutTransactionMobile.EnAttente && t.DateCreation > limiteTransaction));
        if (perimetre is not null)
        {
            requete = requete.Where(perimetre);
        }
        var ids = await requete.Select(r => r.Id).ToListAsync(annulation);

        var annulees = 0;
        foreach (var id in ids)
        {
            // Mise à jour conditionnelle : deux lectures simultanées ou un paiement arrivé entre-temps → une seule annulation, emails envoyés une fois.
            var modifiees = await _context.Reservations
                .Where(r => r.Id == id && r.Statut == StatutReservation.EnAttente && !r.Paiements.Any())
                .ExecuteUpdateAsync(maj => maj
                    .SetProperty(r => r.Statut, StatutReservation.Annulee)
                    .SetProperty(r => r.AnnuleePar, (AuteurAnnulation?)AuteurAnnulation.Systeme)
                    .SetProperty(r => r.MotifAnnulation, (string?)MotifPaiementNonRecu)
                    .SetProperty(r => r.DateAnnulation, (DateTime?)maintenant)
                    .SetProperty(r => r.MontantARembourser, (decimal?)0m), annulation);
            if (modifiees == 0)
            {
                continue;
            }
            annulees++;
            var reservation = await _context.Reservations.AsNoTracking()
                .Include(r => r.Locataire)
                .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
                .FirstAsync(r => r.Id == id, annulation);
            const string parSysteme = "ImmoPlus, faute de paiement reçu dans les 24 h";
            await _notificationServices.ReservationAnnuleeAsync(reservation.Locataire!, reservation, reservation.Appartement!,
                parSysteme, 0, 0, pourBailleur: false);
            await _notificationServices.ReservationAnnuleeAsync(reservation.Appartement!.Bailleur!, reservation, reservation.Appartement!,
                parSysteme, 0, 0, pourBailleur: true);
        }
        return annulees;
    }

    /// <summary>
    /// Désactivation d'un hôte : ses demandes et réservations non confirmées sont annulées par l'admin (remboursement intégral de ce qui a été payé),
    /// les voyageurs prévenus. Les séjours confirmés ne sont pas touchés : on les compte pour que l'admin les traite.
    /// </summary>
    public async Task<(int Annulees, int ConfirmeesAVenir)> AnnulerReservationsHoteDesactiveAsync(int bailleurId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var reservations = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .Where(r => r.Appartement!.BailleurId == bailleurId
                && (r.Statut == StatutReservation.DemandeEnvoyee || r.Statut == StatutReservation.EnAttente))
            .ToListAsync();
        var ids = reservations.Select(r => r.Id).ToList();
        var payes = (await _context.Paiements
                .Where(p => p.ReservationId != null && ids.Contains(p.ReservationId!.Value))
                .Select(p => new { ReservationId = p.ReservationId!.Value, p.Montant })
                .ToListAsync())
            .GroupBy(p => p.ReservationId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Montant));
        var maintenant = DateTime.UtcNow;
        foreach (var r in reservations)
        {
            r.Statut = StatutReservation.Annulee;
            r.AnnuleePar = AuteurAnnulation.Admin;
            r.MotifAnnulation = MotifHoteDesactive;
            r.DateAnnulation = maintenant;
            r.MontantARembourser = payes.GetValueOrDefault(r.Id);
        }
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        foreach (var r in reservations)
        {
            var paye = payes.GetValueOrDefault(r.Id);
            await _notificationServices.ReservationAnnuleeAsync(r.Locataire!, r, r.Appartement!, "l'administrateur ImmoPlus", paye, paye, pourBailleur: false);
        }

        var aujourdhui = maintenant.Date;
        var confirmeesAVenir = await _context.Reservations.CountAsync(r => r.Appartement!.BailleurId == bailleurId
            && r.Statut == StatutReservation.Confirmee && r.DateDepart > aujourdhui);
        return (reservations.Count, confirmeesAVenir);
    }

    private async Task<Reservation> ChargerDemandeAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (role != RoleUtilisateur.Admin && reservation.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Seul l'hôte de ce logement peut répondre à cette demande");
        }
        if (reservation.Statut != StatutReservation.DemandeEnvoyee)
        {
            throw new ConflictException("Cette réservation n'est pas une demande en attente de réponse");
        }
        return reservation;
    }

    /// <summary>
    /// Fin de séjour : une réservation confirmée dont la date de départ est atteinte passe en « Terminee »
    /// et le voyageur comme l'hôte sont invités à laisser un avis. Appelé à la lecture des réservations et par les rappels quotidiens.
    /// </summary>
    /// <param name="perimetre">Limite le traitement aux réservations concernées par la lecture en cours (toutes si null).</param>
    public async Task<int> TerminerSejoursAsync(Expression<Func<Reservation, bool>>? perimetre = null, CancellationToken annulation = default)
    {
        var aujourdhui = DateTime.UtcNow.Date;
        var requete = _context.Reservations.Where(r => r.Statut == StatutReservation.Confirmee && r.DateDepart <= aujourdhui);
        if (perimetre is not null)
        {
            requete = requete.Where(perimetre);
        }
        var ids = await requete.Select(r => r.Id).ToListAsync(annulation);

        var terminees = 0;
        foreach (var id in ids)
        {
            // Mise à jour conditionnelle : si deux lectures simultanées traitent le même séjour, une seule envoie les emails.
            var modifiees = await _context.Reservations
                .Where(r => r.Id == id && r.Statut == StatutReservation.Confirmee)
                .ExecuteUpdateAsync(maj => maj.SetProperty(r => r.Statut, StatutReservation.Terminee), annulation);
            if (modifiees == 0)
            {
                continue;
            }
            terminees++;
            var reservation = await _context.Reservations.AsNoTracking()
                .Include(r => r.Locataire)
                .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
                .FirstAsync(r => r.Id == id, annulation);
            await _notificationServices.SejourTermineAsync(reservation.Locataire!, reservation, reservation.Appartement!, pourHote: false);
            await _notificationServices.SejourTermineAsync(reservation.Appartement!.Bailleur!, reservation, reservation.Appartement!, pourHote: true);
        }
        return terminees;
    }

    public async Task<List<ReservationResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role)
    {
        var requete = _context.Reservations.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(r => r.LocataireId == utilisateurId);
            await ExpirerImpayeesAsync(r => r.LocataireId == utilisateurId);
            await TerminerSejoursAsync(r => r.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(r => r.Appartement!.BailleurId == utilisateurId);
            await ExpirerImpayeesAsync(r => r.Appartement!.BailleurId == utilisateurId);
            await TerminerSejoursAsync(r => r.Appartement!.BailleurId == utilisateurId);
        }
        else
        {
            await ExpirerImpayeesAsync();
            await TerminerSejoursAsync();
        }

        var reservations = await requete
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .OrderByDescending(r => r.DateCreation)
            .ToListAsync();

        return await VersDtosAsync(reservations, utilisateurId, role);
    }

    public async Task<ReservationResponseDto> RechercherAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        await ExpirerImpayeesAsync(r => r.Id == id);
        await TerminerSejoursAsync(r => r.Id == id);
        var reservation = await _context.Reservations
            .AsNoTracking()
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation is null)
        {
            throw new NotFoundException("Cette réservation n'existe pas");
        }

        var autorise = role == RoleUtilisateur.Admin
            || reservation.LocataireId == utilisateurId
            || reservation.Appartement!.BailleurId == utilisateurId;
        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à cette réservation");
        }

        return (await VersDtosAsync([reservation], utilisateurId, role))[0];
    }

    /// <summary>Historique des paiements d'une réservation (voyageur, hôte, admin), du plus ancien au plus récent.</summary>
    public async Task<List<PaiementReservationDto>> ListerPaiementsAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations.AsNoTracking()
            .Include(r => r.Appartement)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (role != RoleUtilisateur.Admin && reservation.LocataireId != utilisateurId && reservation.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette réservation");
        }
        return await _context.Paiements.AsNoTracking()
            .Where(p => p.ReservationId == id)
            .OrderBy(p => p.DatePaiement).ThenBy(p => p.Id)
            .Select(p => new PaiementReservationDto
            {
                Id = p.Id,
                Montant = p.Montant,
                DatePaiement = p.DatePaiement,
                ModePaiement = p.ModePaiement,
                Reference = p.Reference
            })
            .ToListAsync();
    }

    /// <summary>Remboursement que l'appelant déclencherait en annulant maintenant (rien n'est modifié).</summary>
    public async Task<ApercuAnnulationDto> ApercuAnnulationAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        await ExpirerImpayeesAsync(r => r.Id == id);
        await TerminerSejoursAsync(r => r.Id == id);
        var reservation = await ChargerPourAnnulationAsync(id);
        var auteur = AuteurAnnulationPour(reservation, utilisateurId, role);
        VerifierAnnulable(reservation, auteur);

        var montantPaye = await MontantPayeAsync(id);
        var calcul = CalculerRemboursement(reservation, montantPaye, auteur);
        return new ApercuAnnulationDto
        {
            Politique = reservation.PolitiqueAnnulation.ToString(),
            MontantPaye = montantPaye,
            MontantRembourse = calcul.MontantRembourse,
            PourcentageNuitees = calcul.PourcentageNuitees,
            Explication = calcul.Explication
        };
    }

    public async Task AnnulerAsync(int id, string? motif, int utilisateurId, RoleUtilisateur role)
    {
        await ExpirerImpayeesAsync(r => r.Id == id);
        await TerminerSejoursAsync(r => r.Id == id);
        var motifLu = LireMotif(motif);
        // Lecture des paiements et annulation d'un seul tenant : un paiement enregistré au même instant est compté ou refusé, jamais perdu.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var reservation = await ChargerPourAnnulationAsync(id);
        var auteur = AuteurAnnulationPour(reservation, utilisateurId, role);
        VerifierAnnulable(reservation, auteur);

        var totalPaye = await MontantPayeAsync(id);
        var calcul = CalculerRemboursement(reservation, totalPaye, auteur);

        reservation.Statut = StatutReservation.Annulee;
        reservation.AnnuleePar = auteur;
        reservation.MotifAnnulation = motifLu;
        reservation.DateAnnulation = DateTime.UtcNow;
        reservation.MontantARembourser = calcul.MontantRembourse;
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var appartement = reservation.Appartement!;
        var (pourVoyageur, pourHote) = auteur switch
        {
            AuteurAnnulation.Voyageur => ("vous-même", $"le voyageur ({reservation.Locataire!.Nom})"),
            AuteurAnnulation.Hote => ("l'hôte", "vous-même"),
            _ => ("l'administrateur ImmoPlus", "l'administrateur ImmoPlus")
        };
        await _notificationServices.ReservationAnnuleeAsync(reservation.Locataire!, reservation, appartement,
            pourVoyageur, totalPaye, calcul.MontantRembourse, pourBailleur: false);
        await _notificationServices.ReservationAnnuleeAsync(appartement.Bailleur!, reservation, appartement,
            pourHote, totalPaye, calcul.MontantRembourse, pourBailleur: true);
    }

    /// <summary>L'admin enregistre un remboursement versé au voyageur (cumulable jusqu'au montant dû).</summary>
    public async Task<ReservationResponseDto> EnregistrerRemboursementAsync(int id, RemboursementDto dto, int utilisateurId, RoleUtilisateur role)
    {
        // Double clic : le second enregistrement voit le premier et ne peut pas dépasser le montant dû.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var reservation = await ChargerPourAnnulationAsync(id);
        // Remboursement : réservation annulée, ou trop-perçu (paiement mobile validé après un règlement complet).
        if (reservation.Statut != StatutReservation.Annulee && (reservation.MontantARembourser ?? 0) <= 0)
        {
            throw new ConflictException("Seule une réservation annulée ou payée en trop peut faire l'objet d'un remboursement");
        }
        var du = reservation.MontantARembourser ?? 0;
        var dejaRembourse = reservation.MontantRembourse ?? 0;
        if (du <= 0)
        {
            throw new ConflictException("Aucun remboursement n'est dû pour cette réservation");
        }
        if (dejaRembourse >= du)
        {
            throw new ConflictException("Cette réservation est déjà entièrement remboursée");
        }
        if (dto.Montant <= 0)
        {
            throw new InvalidOperationException("Le montant doit être supérieur à 0");
        }
        if (dto.Montant > du - dejaRembourse)
        {
            throw new InvalidOperationException($"Le montant dépasse le reste à rembourser ({(du - dejaRembourse).ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} GNF)");
        }
        if (string.IsNullOrWhiteSpace(dto.ModePaiement))
        {
            throw new InvalidOperationException("Le mode de remboursement est obligatoire");
        }
        // Date absente du JSON : DateTime par défaut (01/01/0001), qui passerait le contrôle « pas dans le futur ».
        if (dto.Date.Year < 2000)
        {
            throw new InvalidOperationException("Indiquez la date du remboursement");
        }
        if (dto.Date.Date > DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date du remboursement ne peut pas être dans le futur");
        }
        if (reservation.DateAnnulation is { } annulation && dto.Date.Date < annulation.Date)
        {
            throw new InvalidOperationException("La date du remboursement ne peut pas précéder l'annulation de la réservation");
        }

        reservation.MontantRembourse = dejaRembourse + dto.Montant;
        reservation.DateRemboursement = dto.Date.Date;
        reservation.ModeRemboursement = dto.ModePaiement.Trim();
        reservation.ReferenceRemboursement = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim();
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        await _notificationServices.RemboursementEffectueAsync(reservation.Locataire!, reservation, reservation.Appartement!,
            dto.Montant, reservation.ModeRemboursement, reservation.ReferenceRemboursement, dto.Date.Date);
        return await RechercherAsync(id, utilisateurId, role);
    }

    private async Task<Reservation> ChargerPourAnnulationAsync(int id) =>
        await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(r => r.Id == id)
        ?? throw new NotFoundException("Cette réservation n'existe pas");

    private const int LongueurMaxMotif = 500;

    private static string? LireMotif(string? motif)
    {
        var texte = string.IsNullOrWhiteSpace(motif) ? null : motif.Trim();
        if (texte?.Length > LongueurMaxMotif)
        {
            throw new InvalidOperationException($"Le motif ne doit pas dépasser {LongueurMaxMotif} caractères");
        }
        return texte;
    }

    private async Task<decimal> MontantPayeAsync(int reservationId) =>
        await _context.Paiements.Where(p => p.ReservationId == reservationId).SumAsync(p => (decimal?)p.Montant) ?? 0;

    private static AuteurAnnulation AuteurAnnulationPour(Reservation reservation, int utilisateurId, RoleUtilisateur role)
    {
        if (role == RoleUtilisateur.Admin)
        {
            return AuteurAnnulation.Admin;
        }
        if (reservation.LocataireId == utilisateurId)
        {
            return AuteurAnnulation.Voyageur;
        }
        if (reservation.Appartement!.BailleurId == utilisateurId)
        {
            return AuteurAnnulation.Hote;
        }
        throw new ConflictException("Vous n'avez pas accès à cette réservation");
    }

    private static void VerifierAnnulable(Reservation reservation, AuteurAnnulation auteur)
    {
        if (reservation.Statut == StatutReservation.Annulee)
        {
            throw new ConflictException("Cette réservation est déjà annulée");
        }
        if (reservation.Statut == StatutReservation.Refusee)
        {
            throw new ConflictException("Cette demande a été refusée : il n'y a rien à annuler");
        }
        if (reservation.Statut == StatutReservation.Terminee)
        {
            throw new ConflictException("Ce séjour est terminé : la réservation ne peut plus être annulée");
        }
        if (auteur == AuteurAnnulation.Hote && reservation.Statut == StatutReservation.DemandeEnvoyee)
        {
            throw new ConflictException("Cette demande n'est pas encore acceptée : refusez-la plutôt que de l'annuler");
        }
        // Voyageur et hôte ne peuvent plus annuler un séjour commencé ; l'admin le peut encore pour régulariser un séjour non effectué.
        if (auteur != AuteurAnnulation.Admin && reservation.DateArrivee.Date < DateTime.UtcNow.Date)
        {
            throw new ConflictException("La date d'arrivée est passée : cette réservation ne peut plus être annulée. Contactez ImmoPlus si le séjour n'a pas eu lieu");
        }
    }

    private static PolitiqueRemboursement.Resultat CalculerRemboursement(Reservation reservation, decimal montantPaye, AuteurAnnulation auteur) =>
        PolitiqueRemboursement.Calculer(reservation.PolitiqueAnnulation, montantPaye, reservation.MontantTotal, reservation.FraisMenage,
            reservation.DateArrivee, reservation.DateCreation, DateTime.UtcNow, parHote: auteur != AuteurAnnulation.Voyageur);

    /// <summary>Convertit les réservations en ajoutant ce qui dépend de l'appelant (avis donnés, messages non lus) en quelques requêtes groupées.</summary>
    private async Task<List<ReservationResponseDto>> VersDtosAsync(List<Reservation> reservations, int utilisateurId, RoleUtilisateur role)
    {
        var ids = reservations.Select(r => r.Id).ToList();
        var montantsPayes = (await _context.Paiements
                .Where(p => p.ReservationId != null && ids.Contains(p.ReservationId!.Value))
                .GroupBy(p => p.ReservationId!.Value)
                .Select(g => new { ReservationId = g.Key, Total = g.Sum(p => p.Montant) })
                .ToListAsync())
            .ToDictionary(m => m.ReservationId, m => m.Total);
        var avis = (await _context.Avis
                .Where(a => a.ReservationId != null && ids.Contains(a.ReservationId!.Value))
                .Select(a => new { ReservationId = a.ReservationId!.Value, a.Sens })
                .ToListAsync())
            .Select(a => (a.ReservationId, a.Sens))
            .ToHashSet();
        var nonLus = (await _context.Messages
                .Where(m => ids.Contains(m.ReservationId) && m.AuteurId != utilisateurId && m.DateLecture == null)
                .GroupBy(m => m.ReservationId)
                .Select(g => new { ReservationId = g.Key, Nombre = g.Count() })
                .ToListAsync())
            .ToDictionary(m => m.ReservationId, m => m.Nombre);

        return reservations.Select(r =>
        {
            var dto = VersDto(r, montantsPayes.GetValueOrDefault(r.Id));
            // Séjour court non confirmé : le voyageur ne voit que le quartier et la ville (l'hôte et l'admin, l'adresse exacte).
            var voitAdresseExacte = role == RoleUtilisateur.Admin || r.Appartement?.BailleurId == utilisateurId
                || r.Statut is StatutReservation.Confirmee or StatutReservation.Terminee;
            if (!voitAdresseExacte && r.Appartement is { TypeOffre: TypeOffre.SejourCourt } logement)
            {
                dto.AppartementAdresse = AppartementServices.AdresseApproximative(logement);
            }
            dto.AvisVoyageurDonne = avis.Contains((r.Id, SensAvis.VoyageurVersLogement));
            dto.AvisHoteDonne = avis.Contains((r.Id, SensAvis.HoteVersVoyageur));
            // Seuls les participants ont des messages « reçus » : l'admin lit sans destinataire.
            var participant = r.LocataireId == utilisateurId || r.Appartement?.BailleurId == utilisateurId;
            dto.MessagesNonLus = participant ? nonLus.GetValueOrDefault(r.Id) : 0;
            return dto;
        }).ToList();
    }

    /// <summary>Dernier jour pour laisser un avis : 14 jours après le départ.</summary>
    public static DateTime FinPeriodeAvis(Reservation r) => r.DateDepart.Date.AddDays(JoursPourAvis);
    public const int JoursPourAvis = 14;

    /// <summary>
    /// Adresse exacte, instructions, position et téléphone de l'hôte : seulement une fois le séjour confirmé (ou terminé).
    /// Les réservations ne sont lues que par le voyageur, l'hôte et l'admin (contrôlé par l'appelant).
    /// </summary>
    private static ReservationResponseDto VersDto(Reservation r, decimal montantPaye)
    {
        var dto = VersDtoDeBase(r, montantPaye);
        var appartement = r.Appartement;
        if (appartement is not null)
        {
            dto.HeureArrivee = AppartementServices.HeureArriveeDe(appartement);
            dto.HeureDepart = AppartementServices.HeureDepartDe(appartement);
            if (r.Statut is StatutReservation.Confirmee or StatutReservation.Terminee)
            {
                dto.InstructionsArrivee = appartement.InstructionsArrivee;
                dto.AdresseComplete = AdresseComplete(appartement);
                dto.TelephoneHote = string.IsNullOrWhiteSpace(appartement.Bailleur?.Telephone) ? null : appartement.Bailleur.Telephone;
                dto.LatitudeExacte = appartement.Latitude;
                dto.LongitudeExacte = appartement.Longitude;
            }
        }
        return dto;
    }

    /// <summary>« Adresse, Quartier, Ville » (sans répéter un élément déjà présent dans l'adresse).</summary>
    public static string AdresseComplete(Appartement a)
    {
        var parties = new List<string> { a.Adresse.Trim() };
        foreach (var partie in new[] { a.Quartier, a.Ville })
        {
            if (!string.IsNullOrWhiteSpace(partie) && !a.Adresse.Contains(partie.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                parties.Add(partie.Trim());
            }
        }
        return string.Join(", ", parties);
    }

    private static ReservationResponseDto VersDtoDeBase(Reservation r, decimal montantPaye) => new()
    {
        Id = r.Id,
        LocataireId = r.LocataireId,
        LocataireNom = r.Locataire?.Nom ?? "",
        AppartementId = r.AppartementId,
        AppartementAdresse = r.Appartement?.Adresse ?? "",
        AppartementVille = r.Appartement?.Ville,
        AppartementQuartier = r.Appartement?.Quartier,
        BailleurNom = r.Appartement?.Bailleur?.Nom ?? "",
        DateArrivee = r.DateArrivee,
        DateDepart = r.DateDepart,
        NombreVoyageurs = r.NombreVoyageurs,
        NombreNuits = r.NombreNuits,
        PrixParNuit = r.PrixParNuit,
        ReductionMontant = r.ReductionMontant,
        FraisMenage = r.FraisMenage,
        Caution = r.Caution,
        MontantTotal = r.MontantTotal,
        MontantPaye = montantPaye,
        Statut = r.Statut.ToString(),
        MotifRefus = r.MotifRefus,
        DateReponseHote = r.DateReponseHote,
        DateCreation = r.DateCreation,
        AvisPossibleJusquau = r.Statut == StatutReservation.Terminee ? DateOnly.FromDateTime(FinPeriodeAvis(r)) : null,
        PolitiqueAnnulation = r.PolitiqueAnnulation.ToString(),
        MontantARembourser = r.MontantARembourser,
        MontantRembourse = r.MontantRembourse,
        DateRemboursement = r.DateRemboursement is { } d ? DateOnly.FromDateTime(d) : null,
        AnnuleePar = r.AnnuleePar?.ToString(),
        MotifAnnulation = r.MotifAnnulation,
        DateAnnulation = r.DateAnnulation,
        PayerAvant = PayerAvant(r, montantPaye)
    };
}
