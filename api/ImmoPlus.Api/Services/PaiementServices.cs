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
    private readonly NotificationServices _notificationServices;

    public PaiementServices(AppDbContext context, NotificationServices notificationServices)
    {
        _context = context;
        _notificationServices = notificationServices;
    }

    public async Task<PaiementResponseDto> EnregistrerPaiementAsync(PaiementDto dto, int utilisateurId, RoleUtilisateur role) =>
        (await EnregistrerAsync(dto, utilisateurId, role, argentDejaRecu: false)).Paiement;

    /// <summary>
    /// Paiement mobile validé par l'opérateur : l'argent est déjà débité, il est donc toujours tracé. Arrivé après l'annulation
    /// de la réservation, il s'ajoute au montant à rembourser (visible de l'admin dans « Remboursements à effectuer »).
    /// </summary>
    public async Task<(PaiementResponseDto Paiement, bool ApresAnnulation)> EnregistrerPaiementMobileAsync(PaiementDto dto, int utilisateurId) =>
        await EnregistrerAsync(dto, utilisateurId, RoleUtilisateur.Locataire, argentDejaRecu: true);

    private async Task<(PaiementResponseDto Paiement, bool ApresAnnulation)> EnregistrerAsync(PaiementDto dto, int utilisateurId, RoleUtilisateur role,
        bool argentDejaRecu)
    {
        var cibles = new[] { dto.MensualiteId, dto.ReservationId, dto.LoyerId }.Count(c => c is not null);
        if (cibles != 1)
        {
            throw new InvalidOperationException("Renseignez exactement une cible : MensualiteId, ReservationId ou LoyerId");
        }
        if (dto.Montant <= 0)
        {
            throw new InvalidOperationException("Le montant doit être supérieur à 0");
        }
        // Date absente du JSON : DateTime par défaut (01/01/0001), qui passerait le contrôle « pas dans le futur ».
        if (dto.DatePaiement.Year < 2000)
        {
            throw new InvalidOperationException("Indiquez la date du paiement");
        }
        if (dto.DatePaiement.Date > DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date de paiement ne peut pas être dans le futur");
        }
        if (string.IsNullOrWhiteSpace(dto.ModePaiement))
        {
            throw new InvalidOperationException("Indiquez le mode de paiement");
        }
        if ((dto.MensualiteId is not null || dto.LoyerId is not null) && role != RoleUtilisateur.Admin)
        {
            // Mensualités et loyers sont suivis par la plateforme : seul l'admin enregistre leurs paiements.
            throw new ConflictException("Seul l'administrateur enregistre les paiements des mensualités et des loyers");
        }

        var paiement = new Paiement
        {
            MensualiteId = dto.MensualiteId,
            ReservationId = dto.ReservationId,
            Montant = dto.Montant,
            DatePaiement = dto.DatePaiement,
            ModePaiement = dto.ModePaiement.Trim(),
            Reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference.Trim(),
            LoyerId = dto.LoyerId
        };

        Mensualite? mensualitePayee = null;
        Reservation? reservationPayee = null;
        Loyer? loyerPaye = null;
        decimal totalReservationPaye = 0;
        var financementSolde = false;
        var apresAnnulation = false;

        // Lecture du reste dû et écriture du paiement d'un seul tenant (SQLite : verrou d'écriture dès le début) :
        // deux enregistrements simultanés (double clic, admin + paiement mobile) ne peuvent pas dépasser le montant dû.
        await using var transaction = _context.Database.CurrentTransaction is null ? await _context.Database.BeginTransactionAsync() : null;

        if (dto.LoyerId is not null)
        {
            var loyer = await _context.Loyers
                .Include(l => l.Location).ThenInclude(l => l!.Locataire)
                .Include(l => l.Location).ThenInclude(l => l!.Appartement)
                .FirstOrDefaultAsync(l => l.Id == dto.LoyerId)
                ?? throw new NotFoundException("Ce loyer n'existe pas");
            if (loyer.Statut == StatutMensualite.Payee || loyer.MontantPaye >= loyer.MontantDu)
            {
                throw new ConflictException("Ce loyer est déjà payé");
            }
            VerifierResteDu(dto.Montant, loyer.MontantDu - loyer.MontantPaye, "ce loyer");
            loyer.MontantPaye += dto.Montant;
            loyer.Statut = loyer.MontantPaye >= loyer.MontantDu ? StatutMensualite.Payee : StatutMensualite.Partiellement;
            loyerPaye = loyer;
        }
        else if (dto.MensualiteId is not null)
        {
            var mensualite = await _context.Mensualites
                .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Appartement)
                .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Locataire)
                .FirstOrDefaultAsync(m => m.Id == dto.MensualiteId)
                ?? throw new NotFoundException("Cette mensualité n'existe pas");
            var location = mensualite.FinancementLocation!.Location!;
            VerifierAutorisation(role, utilisateurId, location.LocataireId, location.Appartement!.BailleurId);

            if (mensualite.Statut == StatutMensualite.Payee || mensualite.MontantPaye >= mensualite.MontantDu)
            {
                throw new ConflictException("Cette mensualité est déjà payée");
            }
            VerifierResteDu(dto.Montant, mensualite.MontantDu - mensualite.MontantPaye, "cette mensualité");

            mensualite.MontantPaye += dto.Montant;
            mensualite.Statut = mensualite.MontantPaye >= mensualite.MontantDu
                ? StatutMensualite.Payee
                : StatutMensualite.Partiellement;

            // Dernière échéance réglée : le financement est soldé.
            var resteDesImpayees = await _context.Mensualites.AnyAsync(m =>
                m.FinancementLocationId == mensualite.FinancementLocationId && m.Id != mensualite.Id && m.Statut != StatutMensualite.Payee);
            if (mensualite.Statut == StatutMensualite.Payee && !resteDesImpayees)
            {
                mensualite.FinancementLocation!.Statut = StatutFinancement.Solde;
                financementSolde = true;
            }
            mensualitePayee = mensualite;
        }
        else
        {
            var reservation = await _context.Reservations
                .Include(r => r.Appartement)
                .Include(r => r.Locataire)
                .FirstOrDefaultAsync(r => r.Id == dto.ReservationId)
                ?? throw new NotFoundException("Cette réservation n'existe pas");
            VerifierAutorisation(role, utilisateurId, reservation.LocataireId, reservation.Appartement!.BailleurId);

            // Sans réservation instantanée, le paiement n'est ouvert qu'après l'acceptation de l'hôte.
            if (reservation.Statut == StatutReservation.DemandeEnvoyee)
            {
                throw new InvalidOperationException("L'hôte n'a pas encore accepté cette demande : le paiement n'est pas encore possible");
            }
            if (reservation.Statut == StatutReservation.Refusee)
            {
                throw new InvalidOperationException("Cette demande a été refusée : aucun paiement n'est possible");
            }

            var totalDejaPaye = await _context.Paiements
                .Where(p => p.ReservationId == reservation.Id)
                .SumAsync(p => (decimal?)p.Montant) ?? 0;

            if (reservation.Statut == StatutReservation.Annulee)
            {
                if (!argentDejaRecu)
                {
                    throw new ConflictException("Cette réservation est annulée");
                }
                // Validé par l'opérateur après l'annulation : rien n'est dû, tout est à rendre.
                reservation.MontantARembourser = (reservation.MontantARembourser ?? 0) + dto.Montant;
                apresAnnulation = true;
            }
            else if (!argentDejaRecu)
            {
                var resteDu = reservation.MontantTotal - totalDejaPaye;
                if (resteDu <= 0)
                {
                    throw new ConflictException("Cette réservation est déjà entièrement payée");
                }
                VerifierResteDu(dto.Montant, resteDu, "cette réservation");
            }
            else
            {
                // Paiement mobile validé alors que la réservation a déjà été réglée autrement (espèces, autre paiement) :
                // l'argent est encaissé, le trop-perçu est à rendre au voyageur (« Remboursements à effectuer »).
                var tropPercu = Math.Min(dto.Montant, Math.Max(0, totalDejaPaye + dto.Montant - reservation.MontantTotal));
                if (tropPercu > 0)
                {
                    reservation.MontantARembourser = (reservation.MontantARembourser ?? 0) + tropPercu;
                }
            }

            if (reservation.Statut == StatutReservation.EnAttente && totalDejaPaye + dto.Montant >= reservation.MontantTotal)
            {
                reservation.Statut = StatutReservation.Confirmee;
            }
            reservationPayee = reservation;
            totalReservationPaye = totalDejaPaye + dto.Montant;
        }

        await _context.Paiements.AddAsync(paiement);
        await _context.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        if (reservationPayee is not null && !apresAnnulation)
        {
            await _notificationServices.ReservationPaiementAsync(reservationPayee.Locataire!, reservationPayee, reservationPayee.Appartement!,
                paiement, totalReservationPaye);
        }
        else if (reservationPayee is not null)
        {
            await _notificationServices.PaiementRecuApresAnnulationAsync(reservationPayee.Locataire!, reservationPayee, reservationPayee.Appartement!,
                paiement.ModePaiement, paiement.Montant);
        }

        if (loyerPaye is not null)
        {
            await _notificationServices.LoyerPayeAsync(loyerPaye.Location!.Locataire!, paiement, loyerPaye, loyerPaye.Location.Appartement!);
        }

        if (mensualitePayee is not null)
        {
            var financement = mensualitePayee.FinancementLocation!;
            var locataire = financement.Location!.Locataire!;
            var prochaine = await _context.Mensualites
                .Where(m => m.FinancementLocationId == financement.Id && m.Statut != StatutMensualite.Payee)
                .OrderBy(m => m.NumeroEcheance)
                .FirstOrDefaultAsync();

            await _notificationServices.PaiementMensualiteAsync(locataire, paiement, mensualitePayee, prochaine);
            if (financementSolde)
            {
                await _notificationServices.FinancementSoldeAsync(locataire, financement);
            }
        }

        return (VersDto(paiement), apresAnnulation);
    }

    /// <summary>Un paiement ne dépasse jamais le reste dû : un trop-perçu devrait ensuite être remboursé à la main.</summary>
    private static void VerifierResteDu(decimal montant, decimal resteDu, string cible)
    {
        if (montant > resteDu)
        {
            throw new InvalidOperationException(
                $"Le montant dépasse le reste dû pour {cible} ({resteDu.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} GNF)");
        }
    }

    /// <summary>
    /// Remboursement anticipé (admin) : enregistre le reste dû de chaque mensualité non soldée, avec une trace par
    /// mensualité, puis clôture le financement. Les loyers démarrent toujours à la fin de la période couverte par l'avance.
    /// </summary>
    public async Task<int> SolderFinancementAsync(int financementId, SolderFinancementDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ModePaiement))
        {
            throw new InvalidOperationException("Indiquez le mode de paiement");
        }
        // Date absente du JSON : DateTime par défaut (01/01/0001), qui passerait le contrôle « pas dans le futur ».
        if (dto.DatePaiement.Year < 2000)
        {
            throw new InvalidOperationException("Indiquez la date du paiement");
        }
        if (dto.DatePaiement.Date > DateTime.UtcNow.Date)
        {
            throw new InvalidOperationException("La date de paiement ne peut pas être dans le futur");
        }

        var financement = await _context.FinancementLocations
            .Include(f => f.Mensualites)
            .Include(f => f.Location).ThenInclude(l => l!.Locataire)
            .FirstOrDefaultAsync(f => f.Id == financementId)
            ?? throw new NotFoundException("Ce financement n'existe pas");
        var restantes = financement.Mensualites.Where(m => m.Statut != StatutMensualite.Payee).OrderBy(m => m.NumeroEcheance).ToList();
        if (financement.Mensualites.Count == 0)
        {
            throw new ConflictException("L'échéancier n'a pas encore été généré");
        }
        if (restantes.Count == 0)
        {
            throw new ConflictException("Ce financement est déjà soldé");
        }

        foreach (var m in restantes)
        {
            await _context.Paiements.AddAsync(new Paiement
            {
                MensualiteId = m.Id,
                Montant = m.MontantDu - m.MontantPaye,
                DatePaiement = dto.DatePaiement,
                ModePaiement = dto.ModePaiement.Trim(),
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? "Remboursement anticipé" : $"{dto.Reference.Trim()} (remboursement anticipé)"
            });
            m.MontantPaye = m.MontantDu;
            m.Statut = StatutMensualite.Payee;
        }
        financement.Statut = StatutFinancement.Solde;
        await _context.SaveChangesAsync();

        await _notificationServices.FinancementSoldeAsync(financement.Location!.Locataire!, financement);
        return restantes.Count;
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
        // Lecture : le locataire et le bailleur concernés ; l'enregistrement est de toute façon réservé
        // au bailleur et à l'admin par la route.
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
