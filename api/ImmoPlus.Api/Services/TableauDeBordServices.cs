namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Tableau de bord de l'admin : ce qui attend une action (dossiers, clés, échéanciers, retards, préavis, réservations).
/// Les retards sont évalués comme partout ailleurs, à partir de l'historique des paiements (SuiviRetards).
/// </summary>
public class TableauDeBordServices
{
    private const int TailleListeRetards = 10;

    private readonly AppDbContext _context;
    private readonly LoyerServices _loyerServices;

    public TableauDeBordServices(AppDbContext context, LoyerServices loyerServices)
    {
        _context = context;
        _loyerServices = loyerServices;
    }

    public async Task<TableauDeBordDto> ObtenirAsync()
    {
        var maintenant = DateTime.UtcNow;
        var aujourdhui = maintenant.Date;

        // Les loyers sont créés à la demande : sans cela, un loyer d'une location jamais consultée échapperait aux retards.
        await _loyerServices.GarantirLoyersLocationsEnCoursAsync();

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

        // Montants décimaux : comparés en mémoire (SQLite) sur les réservations avec un remboursement dû (annulées ou payées en trop).
        dto.RemboursementsAEffectuer = (await _context.Reservations.AsNoTracking()
                .Where(r => r.MontantARembourser != null)
                .Select(r => new RemboursementAEffectuerDto
                {
                    ReservationId = r.Id,
                    VoyageurNom = r.Locataire!.Nom,
                    MontantARembourser = r.MontantARembourser!.Value,
                    MontantRembourse = r.MontantRembourse ?? 0,
                    DateAnnulation = r.DateAnnulation
                })
                .ToListAsync())
            .Where(r => r.MontantARembourser > r.MontantRembourse)
            .OrderBy(r => r.DateAnnulation).ThenBy(r => r.ReservationId)
            .ToList();

        return dto;
    }

    /// <summary>Mensualités des financements en cours et loyers des locations en cours, échus et non soldés.</summary>
    private async Task<RetardsTableauDeBordDto> RetardsAsync(DateTime aujourdhui)
    {
        // Les montants (decimal) ne se comparent pas en SQL sous SQLite : le reste dû est filtré en mémoire.
        var mensualites = await _context.Mensualites.AsNoTracking()
            .Include(m => m.Paiements)
            .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Locataire)
            .Where(m => m.FinancementLocation!.Statut == StatutFinancement.EnCours
                && m.Statut != StatutMensualite.Payee && m.DateEcheance < aujourdhui)
            .ToListAsync();

        var loyers = await _context.Loyers.AsNoTracking()
            .Include(l => l.Paiements)
            .Include(l => l.Location).ThenInclude(l => l!.Locataire)
            .Where(l => l.Location!.Statut == StatutLocation.EnCours
                && l.Statut != StatutMensualite.Payee && l.DateEcheance < aujourdhui)
            .ToListAsync();

        var retards = mensualites
            .Where(m => m.MontantPaye < m.MontantDu)
            .Select(m => (Suivi: SuiviRetards.Evaluer(m.DateEcheance, m.MontantDu, m.Paiements, aujourdhui), Ligne: new RetardTableauDeBordDto
            {
                Type = "Mensualite",
                CandidatureId = m.FinancementLocation!.Location!.CandidatureId,
                LocationId = m.FinancementLocation.LocationId,
                LocataireNom = m.FinancementLocation.Location.Locataire!.Nom,
                DateEcheance = m.DateEcheance,
                ResteDu = m.MontantDu - m.MontantPaye
            }))
            .Concat(loyers
                .Where(l => l.MontantPaye < l.MontantDu)
                .Select(l => (Suivi: SuiviRetards.Evaluer(l.DateEcheance, l.MontantDu, l.Paiements, aujourdhui), Ligne: new RetardTableauDeBordDto
                {
                    Type = "Loyer",
                    CandidatureId = l.Location!.CandidatureId,
                    LocationId = l.LocationId,
                    LocataireNom = l.Location.Locataire!.Nom,
                    DateEcheance = l.DateEcheance,
                    ResteDu = l.MontantDu - l.MontantPaye
                })))
            .Where(r => r.Suivi.Ponctualite == SuiviRetards.EnRetard)
            .Select(r => { r.Ligne.JoursRetard = r.Suivi.JoursRetard; return r.Ligne; })
            .OrderByDescending(r => r.JoursRetard).ThenBy(r => r.LocationId)
            .ToList();

        return new RetardsTableauDeBordDto
        {
            Nombre = retards.Count,
            Montant = retards.Sum(r => r.ResteDu),
            Liste = retards.Take(TailleListeRetards).ToList()
        };
    }
}
