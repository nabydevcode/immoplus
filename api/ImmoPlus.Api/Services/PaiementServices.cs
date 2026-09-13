namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class PaiementServices
{
    private readonly AppDbContext _context;

    public PaiementServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PaiementResponseDto> EnregistrerPaiementAsync(PaiementDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var unSeul = (dto.MensualiteId is not null) ^ (dto.ReservationId is not null);
        if (!unSeul)
        {
            throw new InvalidOperationException("Renseignez exactement l'un des deux : MensualiteId ou ReservationId");
        }

        var paiement = new Paiement
        {
            MensualiteId = dto.MensualiteId,
            ReservationId = dto.ReservationId,
            Montant = dto.Montant,
            DatePaiement = dto.DatePaiement,
            ModePaiement = dto.ModePaiement,
            Reference = dto.Reference
        };

        if (dto.MensualiteId is not null)
        {
            var mensualite = await _context.Mensualites
                .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Appartement)
                .FirstOrDefaultAsync(m => m.Id == dto.MensualiteId);
            if (mensualite is null)
            {
                throw new NotFoundException("Cette mensualité n'existe pas");
            }
            var location = mensualite.FinancementLocation!.Location!;
            VerifierAutorisation(role, utilisateurId, location.LocataireId, location.Appartement!.BailleurId);

            if (mensualite.Statut == StatutMensualite.Payee)
            {
                throw new ConflictException("Cette mensualité est déjà payée");
            }

            mensualite.MontantPaye += dto.Montant;
            mensualite.Statut = mensualite.MontantPaye >= mensualite.MontantDu
                ? StatutMensualite.Payee
                : StatutMensualite.Partiellement;
        }
        else
        {
            var reservation = await _context.Reservations
                .Include(r => r.Appartement)
                .FirstOrDefaultAsync(r => r.Id == dto.ReservationId);
            if (reservation is null)
            {
                throw new NotFoundException("Cette réservation n'existe pas");
            }
            VerifierAutorisation(role, utilisateurId, reservation.LocataireId, reservation.Appartement!.BailleurId);

            if (reservation.Statut == StatutReservation.Annulee)
            {
                throw new ConflictException("Cette réservation est annulée");
            }

            var totalDejaPaye = await _context.Paiements
                .Where(p => p.ReservationId == reservation.Id)
                .SumAsync(p => (decimal?)p.Montant) ?? 0;

            if (totalDejaPaye + dto.Montant >= reservation.MontantTotal)
            {
                reservation.Statut = StatutReservation.Confirmee;
            }
        }

        await _context.Paiements.AddAsync(paiement);
        await _context.SaveChangesAsync();

        return VersDto(paiement);
    }

    public async Task<List<PaiementResponseDto>> ListerParMensualiteAsync(int mensualiteId, int utilisateurId, RoleUtilisateur role)
    {
        var mensualite = await _context.Mensualites
            .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Appartement)
            .FirstOrDefaultAsync(m => m.Id == mensualiteId);
        if (mensualite is null)
        {
            throw new NotFoundException("Cette mensualité n'existe pas");
        }
        var location = mensualite.FinancementLocation!.Location!;
        VerifierAutorisation(role, utilisateurId, location.LocataireId, location.Appartement!.BailleurId);

        return await _context.Paiements
            .AsNoTracking()
            .Where(p => p.MensualiteId == mensualiteId)
            .OrderBy(p => p.DatePaiement)
            .Select(p => new PaiementResponseDto
            {
                Id = p.Id,
                MensualiteId = p.MensualiteId,
                ReservationId = p.ReservationId,
                Montant = p.Montant,
                DatePaiement = p.DatePaiement,
                ModePaiement = p.ModePaiement,
                Reference = p.Reference
            })
            .ToListAsync();
    }

    private static void VerifierAutorisation(RoleUtilisateur role, int utilisateurId, int locataireId, int bailleurId)
    {
        var autorise = role == RoleUtilisateur.Admin || utilisateurId == locataireId || utilisateurId == bailleurId;
        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à cette ressource");
        }
    }

    private static PaiementResponseDto VersDto(Paiement p) => new()
    {
        Id = p.Id,
        MensualiteId = p.MensualiteId,
        ReservationId = p.ReservationId,
        Montant = p.Montant,
        DatePaiement = p.DatePaiement,
        ModePaiement = p.ModePaiement,
        Reference = p.Reference
    };
}
