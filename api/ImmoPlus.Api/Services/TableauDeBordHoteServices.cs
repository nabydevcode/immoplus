namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Tableau de bord de l'hôte : revenus encaissés de l'année (séjours courts et loyers), occupation de ses séjours courts,
/// arrivées des 14 prochains jours, demandes et messages en attente, badges de confiance. Tout est recalculé à la lecture.
/// </summary>
public class TableauDeBordHoteServices
{
    private const int JoursProchainesArrivees = 14;

    private readonly AppDbContext _context;
    private readonly AvisServices _avisServices;
    private readonly MessageServices _messageServices;

    public TableauDeBordHoteServices(AppDbContext context, AvisServices avisServices, MessageServices messageServices)
    {
        _context = context;
        _avisServices = avisServices;
        _messageServices = messageServices;
    }

    /// <param name="bailleurIdDemande">Admin uniquement : l'hôte à consulter (obligatoire pour l'admin, ignoré pour un bailleur).</param>
    public async Task<TableauDeBordHoteDto> ObtenirAsync(int? annee, int? bailleurIdDemande, int utilisateurId, RoleUtilisateur role)
    {
        int bailleurId;
        if (role == RoleUtilisateur.Admin)
        {
            if (bailleurIdDemande is null)
            {
                throw new InvalidOperationException("Indiquez le bailleur à consulter (bailleurId)");
            }
            var bailleur = await _context.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == bailleurIdDemande)
                ?? throw new NotFoundException("Ce bailleur n'existe pas");
            if (bailleur.Role != RoleUtilisateur.Bailleur)
            {
                throw new InvalidOperationException("Cet utilisateur n'a pas le rôle Bailleur");
            }
            bailleurId = bailleur.Id;
        }
        else
        {
            bailleurId = utilisateurId;
        }

        var aujourdhui = DateTime.UtcNow.Date;
        var an = annee ?? aujourdhui.Year;
        if (an < 2000 || an > aujourdhui.Year + 1)
        {
            throw new InvalidOperationException($"Année invalide (entre 2000 et {aujourdhui.Year + 1})");
        }
        var debutAnnee = new DateTime(an, 1, 1);
        var finAnnee = debutAnnee.AddYears(1);

        var dto = new TableauDeBordHoteDto { Annee = an };

        // --- Revenus : paiements encaissés dans l'année (montants additionnés en mémoire : decimal sous SQLite) ---
        var sejours = await _context.Paiements.AsNoTracking()
            .Where(p => p.ReservationId != null && p.Reservation!.Appartement!.BailleurId == bailleurId
                && p.DatePaiement >= debutAnnee && p.DatePaiement < finAnnee)
            .Select(p => new { p.DatePaiement.Month, p.Montant })
            .ToListAsync();
        var loyers = await _context.Paiements.AsNoTracking()
            .Where(p => p.LoyerId != null && p.Loyer!.Location!.Appartement!.BailleurId == bailleurId
                && p.DatePaiement >= debutAnnee && p.DatePaiement < finAnnee)
            .Select(p => new { p.DatePaiement.Month, p.Montant })
            .ToListAsync();
        // Remboursements versés aux voyageurs (séjours annulés), au mois du dernier remboursement : déduits des revenus.
        var remboursements = await _context.Reservations.AsNoTracking()
            .Where(r => r.Appartement!.BailleurId == bailleurId && r.MontantRembourse != null && r.DateRemboursement != null
                && r.DateRemboursement >= debutAnnee && r.DateRemboursement < finAnnee)
            .Select(r => new { r.DateRemboursement!.Value.Month, Montant = r.MontantRembourse!.Value })
            .ToListAsync();
        dto.RevenusParMois = Enumerable.Range(1, 12).Select(mois => new RevenusMoisDto
        {
            Mois = mois,
            Sejours = sejours.Where(p => p.Month == mois).Sum(p => p.Montant),
            Loyers = loyers.Where(p => p.Month == mois).Sum(p => p.Montant),
            Remboursements = remboursements.Where(r => r.Month == mois).Sum(r => r.Montant)
        }).ToList();
        dto.RemboursementsTotal = dto.RevenusParMois.Sum(m => m.Remboursements);
        dto.RevenusTotal = dto.RevenusParMois.Sum(m => m.Sejours + m.Loyers) - dto.RemboursementsTotal;

        // --- Occupation des séjours courts sur la partie écoulée de l'année (nuits avant aujourd'hui) ---
        var logements = await _context.Appartements.AsNoTracking()
            .Where(a => a.BailleurId == bailleurId && a.TypeOffre == TypeOffre.SejourCourt)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Adresse, a.DateCreation })
            .ToListAsync();
        var finPeriode = aujourdhui < finAnnee ? aujourdhui : finAnnee;
        var ids = logements.Select(l => l.Id).ToList();
        var reservees = await _context.Reservations.AsNoTracking()
            .Where(r => ids.Contains(r.AppartementId)
                && (r.Statut == StatutReservation.Confirmee || r.Statut == StatutReservation.Terminee)
                && r.DateArrivee < finPeriode && r.DateDepart > debutAnnee)
            .Select(r => new { r.AppartementId, r.DateArrivee, r.DateDepart })
            .ToListAsync();
        var bloquees = await _context.Indisponibilites.AsNoTracking()
            .Where(i => ids.Contains(i.AppartementId) && i.DateDebut < finPeriode && i.DateFin > debutAnnee)
            .Select(i => new { i.AppartementId, i.DateDebut, i.DateFin })
            .ToListAsync();

        foreach (var logement in logements)
        {
            // Une annonce publiée en cours d'année n'est comptée qu'à partir de sa publication.
            var debut = logement.DateCreation is { } creation && creation.Date > debutAnnee ? creation.Date : debutAnnee;
            var nuits = new HashSet<DateTime>(Nuits(debut, finPeriode));
            nuits.ExceptWith(bloquees.Where(b => b.AppartementId == logement.Id).SelectMany(b => Nuits(b.DateDebut.Date, b.DateFin.Date)));
            var nuitsReservees = reservees.Where(r => r.AppartementId == logement.Id)
                .SelectMany(r => Nuits(r.DateArrivee.Date, r.DateDepart.Date))
                .Distinct()
                .Count(nuits.Contains);
            dto.OccupationParLogement.Add(new OccupationLogementDto
            {
                AppartementId = logement.Id,
                Adresse = logement.Adresse,
                NuitsReservees = nuitsReservees,
                NuitsPossibles = nuits.Count,
                Taux = Taux(nuitsReservees, nuits.Count)
            });
        }
        dto.TauxOccupation = Taux(dto.OccupationParLogement.Sum(o => o.NuitsReservees), dto.OccupationParLogement.Sum(o => o.NuitsPossibles));

        // --- Prochaines arrivées (séjours confirmés, aujourd'hui inclus, sur 14 jours) ---
        var limite = aujourdhui.AddDays(JoursProchainesArrivees);
        dto.ProchainesArrivees = (await _context.Reservations.AsNoTracking()
                .Where(r => r.Appartement!.BailleurId == bailleurId && r.Statut == StatutReservation.Confirmee
                    && r.DateArrivee >= aujourdhui && r.DateArrivee <= limite)
                .OrderBy(r => r.DateArrivee).ThenBy(r => r.Id)
                .Select(r => new { r.Id, VoyageurNom = r.Locataire!.Nom, r.Appartement!.Adresse, r.DateArrivee, r.DateDepart, r.NombreVoyageurs })
                .ToListAsync())
            .Select(r => new ProchaineArriveeDto
            {
                ReservationId = r.Id,
                VoyageurNom = r.VoyageurNom,
                AppartementAdresse = r.Adresse,
                DateArrivee = DateOnly.FromDateTime(r.DateArrivee),
                DateDepart = DateOnly.FromDateTime(r.DateDepart),
                NombreVoyageurs = r.NombreVoyageurs
            })
            .ToList();

        dto.DemandesEnAttente = await _context.Reservations.CountAsync(r =>
            r.Appartement!.BailleurId == bailleurId && r.Statut == StatutReservation.DemandeEnvoyee);
        dto.MessagesNonLus = (await _messageServices.NonLusAsync(bailleurId)).Total;

        var hote = (await _avisServices.StatistiquesHotesAsync([bailleurId])).GetValueOrDefault(bailleurId);
        if (hote is not null)
        {
            dto.NoteMoyenne = hote.NoteMoyenne;
            dto.NombreAvis = hote.NombreAvis;
            dto.Superhote = hote.Superhote;
            dto.TauxReponse = hote.TauxReponse;
        }
        return dto;
    }

    /// <summary>Nuits de debut (incluse) à fin (exclue), comme une réservation : la nuit du jour J va de J à J+1.</summary>
    private static IEnumerable<DateTime> Nuits(DateTime debut, DateTime fin)
    {
        for (var jour = debut.Date; jour < fin.Date; jour = jour.AddDays(1))
        {
            yield return jour;
        }
    }

    private static decimal? Taux(int reservees, int possibles) =>
        possibles == 0 ? null : Math.Round(100m * reservees / possibles, 1, MidpointRounding.AwayFromZero);
}
