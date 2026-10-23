namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Tableau de bord de l'admin : ce qui attend une action (dossiers, clés, échéanciers, retards, préavis, réservations).
/// Les retards suivent la règle de SuiviRetards (échéance passée que les paiements n'ont pas encore soldée), évaluée en SQL.
/// Les loyers du mois sont créés par la maintenance planifiée (MaintenanceServices), plus à l'ouverture du tableau de bord.
/// </summary>
public class TableauDeBordServices
{
    private const int TailleListeRetards = 10;

    private readonly AppDbContext _context;

    public TableauDeBordServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TableauDeBordDto> ObtenirAsync()
    {
        var maintenant = DateTime.UtcNow;
        var aujourdhui = maintenant.Date;

        var dto = new TableauDeBordDto
        {
            UtilisateursAValider = await _context.Utilisateurs.CountAsync(u => u.Statut == StatutUtilisateur.EnAttente),
            CandidaturesAEtudier = await _context.Candidatures.CountAsync(c => c.Statut == StatutCandidature.EnAttente),
            VisitesAVenir = await _context.Visites.CountAsync(v =>
                (v.Statut == StatutVisite.Proposee || v.Statut == StatutVisite.Confirmee || v.Statut == StatutVisite.Indisponible)
                && v.DateHeure > maintenant),
            Logements = await _context.Appartements.CountAsync(),
            LocationsEnCours = await _context.Locations.CountAsync(l => l.Statut == StatutLocation.EnCours),
            ReservationsEnAttentePaiement = await _context.Reservations.CountAsync(r => r.Statut == StatutReservation.EnAttente),
            ReservationsAVenir7Jours = await _context.Reservations.CountAsync(r =>
                (r.Statut == StatutReservation.EnAttente || r.Statut == StatutReservation.Confirmee || r.Statut == StatutReservation.DemandeEnvoyee)
                && r.DateArrivee >= aujourdhui && r.DateArrivee < aujourdhui.AddDays(8))
        };

        var locationsEnCours = _context.Locations.AsNoTracking().Where(l => l.Statut == StatutLocation.EnCours);

        dto.DossiersAPrendreEnCharge = await locationsEnCours
            .Where(l => l.CandidatureId != null && l.Candidature!.Statut == StatutCandidature.Acceptee && l.DateApportPaye == null)
            .OrderBy(l => l.DateDebut).ThenBy(l => l.Id)
            .Select(l => new DossierAPrendreEnChargeDto
            {
                CandidatureId = l.CandidatureId!.Value,
                LocationId = l.Id,
                LocataireNom = l.Locataire!.Nom,
                AppartementAdresse = l.Appartement!.Adresse,
                ApportInitial = l.Candidature!.ApportInitial,
                DateAttribution = l.Candidature.DateTraitement ?? l.DateDebut
            })
            .ToListAsync();

        dto.ClesARemettre = await locationsEnCours
            .Where(l => l.DateApportPaye != null && l.DateRemiseCles == null)
            .OrderBy(l => l.DateApportPaye).ThenBy(l => l.Id)
            .Select(l => new CleARemettreDto
            {
                CandidatureId = l.CandidatureId,
                LocationId = l.Id,
                LocataireNom = l.Locataire!.Nom,
                AppartementAdresse = l.Appartement!.Adresse,
                DateApportPaye = l.DateApportPaye!.Value
            })
            .ToListAsync();

        // L'échéancier se génère une fois l'apport reçu.
        dto.EcheanciersAGenerer = await _context.FinancementLocations.AsNoTracking()
            .Where(f => f.Statut == StatutFinancement.EnAttenteValidation
                && f.Location!.Statut == StatutLocation.EnCours && f.Location.DateApportPaye != null)
            .OrderBy(f => f.Id)
            .Select(f => new EcheancierAGenererDto
            {
                FinancementId = f.Id,
                CandidatureId = f.Location!.CandidatureId,
                LocationId = f.LocationId,
                LocataireNom = f.Location.Locataire!.Nom,
                MontantARembourser = f.MontantARembourser
            })
            .ToListAsync();

        dto.Retards = await RetardsAsync(aujourdhui);

        var preavis = await locationsEnCours
            .Where(l => l.DateFinPrevue != null)
            .OrderBy(l => l.DateFinPrevue).ThenBy(l => l.Id)
            .Select(l => new PreavisEnCoursDto
            {
                LocationId = l.Id,
                CandidatureId = l.CandidatureId,
                LocataireNom = l.Locataire!.Nom,
                AppartementAdresse = l.Appartement!.Adresse,
                DateFinPrevue = l.DateFinPrevue!.Value
            })
            .ToListAsync();
        preavis.ForEach(p => p.FinAtteinte = p.DateFinPrevue.Date <= aujourdhui);
        dto.PreavisEnCours = preavis;

        // Réservations avec un remboursement dû (annulées ou payées en trop) et pas encore entièrement remboursé.
        dto.RemboursementsAEffectuer = await _context.Reservations.AsNoTracking()
            .Where(r => r.MontantARembourser != null && r.MontantARembourser > (r.MontantRembourse ?? 0))
            .OrderBy(r => r.DateAnnulation).ThenBy(r => r.Id)
            .Select(r => new RemboursementAEffectuerDto
            {
                ReservationId = r.Id,
                VoyageurNom = r.Locataire!.Nom,
                MontantARembourser = r.MontantARembourser!.Value,
                MontantRembourse = r.MontantRembourse ?? 0,
                DateAnnulation = r.DateAnnulation
            })
            .ToListAsync();

        return dto;
    }

    /// <summary>
    /// Mensualités des financements en cours et loyers des locations en cours, échus et non soldés par leurs paiements
    /// (même règle que SuiviRetards.EnRetard). Nombre et montant agrégés en SQL ; seules les 10 lignes affichées sont lues
    /// (avant : toutes les échéances en retard chargées en mémoire avec leurs paiements).
    /// </summary>
    private async Task<RetardsTableauDeBordDto> RetardsAsync(DateTime aujourdhui)
    {
        var mensualites = _context.Mensualites.AsNoTracking()
            .Where(m => m.FinancementLocation!.Statut == StatutFinancement.EnCours
                && m.Statut != StatutMensualite.Payee && m.DateEcheance < aujourdhui && m.MontantPaye < m.MontantDu
                && m.Paiements.Sum(p => p.Montant) < m.MontantDu);
        var loyers = _context.Loyers.AsNoTracking()
            .Where(l => l.Location!.Statut == StatutLocation.EnCours
                && l.Statut != StatutMensualite.Payee && l.DateEcheance < aujourdhui && l.MontantPaye < l.MontantDu
                && l.Paiements.Sum(p => p.Montant) < l.MontantDu);

        var totauxMensualites = await mensualites.GroupBy(_ => 1)
            .Select(g => new { Nombre = g.Count(), Montant = g.Sum(m => m.MontantDu - m.MontantPaye) }).FirstOrDefaultAsync();
        var totauxLoyers = await loyers.GroupBy(_ => 1)
            .Select(g => new { Nombre = g.Count(), Montant = g.Sum(l => l.MontantDu - l.MontantPaye) }).FirstOrDefaultAsync();

        // Les plus en retard d'abord (échéance la plus ancienne), puis par location : 10 de chaque type suffisent pour les 10 premières.
        var lignes = (await mensualites
                .OrderBy(m => m.DateEcheance).ThenBy(m => m.FinancementLocation!.LocationId).ThenBy(m => m.Id)
                .Take(TailleListeRetards)
                .Select(m => new RetardTableauDeBordDto
                {
                    Type = "Mensualite",
                    CandidatureId = m.FinancementLocation!.Location!.CandidatureId,
                    LocationId = m.FinancementLocation.LocationId,
                    LocataireNom = m.FinancementLocation.Location.Locataire!.Nom,
                    DateEcheance = m.DateEcheance,
                    ResteDu = m.MontantDu - m.MontantPaye
                })
                .ToListAsync())
            .Concat(await loyers
                .OrderBy(l => l.DateEcheance).ThenBy(l => l.LocationId).ThenBy(l => l.Id)
                .Take(TailleListeRetards)
                .Select(l => new RetardTableauDeBordDto
                {
                    Type = "Loyer",
                    CandidatureId = l.Location!.CandidatureId,
                    LocationId = l.LocationId,
                    LocataireNom = l.Location.Locataire!.Nom,
                    DateEcheance = l.DateEcheance,
                    ResteDu = l.MontantDu - l.MontantPaye
                })
                .ToListAsync())
            .ToList();
        lignes.ForEach(l => l.JoursRetard = (aujourdhui.Date - l.DateEcheance.Date).Days);

        return new RetardsTableauDeBordDto
        {
            Nombre = (totauxMensualites?.Nombre ?? 0) + (totauxLoyers?.Nombre ?? 0),
            Montant = (totauxMensualites?.Montant ?? 0) + (totauxLoyers?.Montant ?? 0),
            Liste = lignes.OrderByDescending(r => r.JoursRetard).ThenBy(r => r.LocationId).Take(TailleListeRetards).ToList()
        };
    }
}
