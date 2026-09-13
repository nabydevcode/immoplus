namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class ReservationServices
{
    private readonly AppDbContext _context;

    public ReservationServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ReservationResponseDto> CreerAsync(int locataireId, ReservationDto dto)
    {
        var appartement = await _context.Appartements.FindAsync(dto.AppartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (appartement.TypeOffre != TypeOffre.SejourCourt)
        {
            throw new ConflictException("Cette annonce est une location longue durée : passez par une candidature");
        }
        if (!appartement.Disponible)
        {
            throw new ConflictException("Ce logement n'est plus disponible");
        }
        if (dto.DateArrivee.Date < DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date d'arrivée ne peut pas être dans le passé");
        }
        if (dto.DateDepart.Date <= dto.DateArrivee.Date)
        {
            throw new InvalidOperationException("La date de départ doit être après la date d'arrivée");
        }

        var dureeMaxMois = appartement.DureeMaxMois ?? 1;
        if (dto.DateDepart.Date > dto.DateArrivee.Date.AddMonths(dureeMaxMois))
        {
            throw new ConflictException($"Ce séjour est limité à {dureeMaxMois} mois maximum");
        }

        var chevauchement = await _context.Reservations.AnyAsync(r =>
            r.AppartementId == dto.AppartementId &&
            r.Statut != StatutReservation.Annulee &&
            dto.DateArrivee.Date < r.DateDepart.Date && r.DateArrivee.Date < dto.DateDepart.Date);
        if (chevauchement)
        {
            throw new ConflictException("Ce logement est déjà réservé sur une partie de cette période");
        }

        var nombreNuits = (dto.DateDepart.Date - dto.DateArrivee.Date).Days;
        var montantTotal = appartement.PrixParNuit!.Value * nombreNuits;

        var reservation = new Reservation
        {
            LocataireId = locataireId,
            AppartementId = dto.AppartementId,
            DateArrivee = dto.DateArrivee.Date,
            DateDepart = dto.DateDepart.Date,
            MontantTotal = montantTotal,
            Statut = StatutReservation.EnAttente,
            DateCreation = DateTime.UtcNow
        };

        await _context.Reservations.AddAsync(reservation);
        await _context.SaveChangesAsync();

        return await RechercherAsync(reservation.Id, locataireId, RoleUtilisateur.Locataire);
    }

    public async Task<List<ReservationResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role)
    {
        var requete = _context.Reservations.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(r => r.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(r => r.Appartement!.BailleurId == utilisateurId);
        }

        var reservations = await requete
            .Include(r => r.Locataire)
            .Include(r => r.Appartement)
            .ToListAsync();

        var montantsPayes = await _context.Paiements
            .Where(p => p.ReservationId != null && reservations.Select(r => r.Id).Contains(p.ReservationId!.Value))
            .GroupBy(p => p.ReservationId)
            .Select(g => new { ReservationId = g.Key, Total = g.Sum(p => p.Montant) })
            .ToListAsync();

        return reservations.Select(r => VersDto(r, montantsPayes.FirstOrDefault(m => m.ReservationId == r.Id)?.Total ?? 0)).ToList();
    }

    public async Task<ReservationResponseDto> RechercherAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement)
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

        var montantPaye = await _context.Paiements
            .Where(p => p.ReservationId == id)
            .SumAsync(p => (decimal?)p.Montant) ?? 0;

        return VersDto(reservation, montantPaye);
    }

    public async Task AnnulerAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations.FindAsync(id);
        if (reservation is null)
        {
            throw new NotFoundException("Cette réservation n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && reservation.LocataireId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette réservation");
        }
        if (reservation.Statut == StatutReservation.Annulee)
        {
            throw new ConflictException("Cette réservation est déjà annulée");
        }

        reservation.Statut = StatutReservation.Annulee;
        await _context.SaveChangesAsync();
    }

    private static ReservationResponseDto VersDto(Reservation r, decimal montantPaye) => new()
    {
        Id = r.Id,
        LocataireId = r.LocataireId,
        LocataireNom = r.Locataire?.Nom ?? "",
        AppartementId = r.AppartementId,
        AppartementAdresse = r.Appartement?.Adresse ?? "",
        DateArrivee = r.DateArrivee,
        DateDepart = r.DateDepart,
        MontantTotal = r.MontantTotal,
        MontantPaye = montantPaye,
        Statut = r.Statut.ToString(),
        DateCreation = r.DateCreation
    };
}
