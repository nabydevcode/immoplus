namespace ImmoPlus.Api.Services;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Rappels automatiques des échéances (mensualités du financement et loyers) :
/// rappel quand l'échéance tombe dans les 3 jours, alerte dès le 1er jour de retard puis chaque semaine,
/// et récapitulatif quotidien des retards aux admins. Chaque envoi réussi est mémorisé pour ne jamais faire de doublon.
/// </summary>
public class RappelsServices
{
    private const int JoursAvantEcheance = 3;
    private const int JoursEntreAlertesRetard = 7;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // Le service planifié et la route admin peuvent se croiser : une seule exécution à la fois, sinon doublons.
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    private readonly AppDbContext _context;
    private readonly LoyerServices _loyerServices;
    private readonly NotificationServices _notificationServices;
    private readonly ReservationServices _reservationServices;
    private readonly LiensFrontend _liens;
    private readonly JournalServices _journal;

    public RappelsServices(AppDbContext context, LoyerServices loyerServices, NotificationServices notificationServices,
        ReservationServices reservationServices, LiensFrontend liens, JournalServices journal)
    {
        _journal = journal;
        _liens = liens;
        _context = context;
        _loyerServices = loyerServices;
        _notificationServices = notificationServices;
        _reservationServices = reservationServices;
    }

    public async Task<RappelsResultatDto> ExecuterAsync(CancellationToken annulation = default)
    {
        // Emails planifiés : liens vers Frontend:Url, même lancés depuis la route admin (pas l'origine de l'admin).
        _liens.UtiliserUrlConfiguree = true;
        await Verrou.WaitAsync(annulation);
        try
        {
            var resultat = new RappelsResultatDto();
            var maintenant = DateTime.UtcNow;
            var aujourdhui = maintenant.Date;
            var retards = new List<NotificationServices.LigneRetard>();

            // Demandes de séjour sans réponse de l'hôte : refusées au bout de 48 h pour libérer les dates.
            resultat.DemandesReservationExpirees = await _reservationServices.ExpirerDemandesAsync(annulation);
            // Réservations acceptées (ou instantanées) restées sans paiement 24 h : annulées pour libérer les dates.
            resultat.ReservationsImpayeesAnnulees = await _reservationServices.ExpirerImpayeesAsync(annulation: annulation);
            // Séjours confirmés dont le départ est passé : terminés, avec l'invitation à laisser un avis.
            resultat.SejoursTermines = await _reservationServices.TerminerSejoursAsync(annulation: annulation);

            // Veille de l'arrivée : instructions au voyageur et copie courte à l'hôte, une seule fois par séjour.
            resultat.InstructionsArrivee = await EnvoyerInstructionsArriveeAsync(aujourdhui, maintenant, annulation);

            // Les loyers sont créés à la demande : on crée ceux du mois avant de chercher les échéances.
            await _loyerServices.GarantirLoyersLocationsEnCoursAsync(annulation);

            var mensualites = await _context.Mensualites
                .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Locataire)
                .Include(m => m.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Appartement)
                .Where(m => m.FinancementLocation!.Statut == StatutFinancement.EnCours
                    && m.Statut != StatutMensualite.Payee
                    && m.DateEcheance <= aujourdhui.AddDays(JoursAvantEcheance + 1))
                .ToListAsync(annulation);

            foreach (var m in mensualites.Where(m => m.MontantPaye < m.MontantDu).OrderBy(m => m.DateEcheance))
            {
                var financement = m.FinancementLocation!;
                var location = financement.Location!;
                var libelle = $"mensualité n° {m.NumeroEcheance} du financement n° {financement.Id:D6}";
                var (rappel, retard) = await TraiterAsync(location, libelle, m.DateEcheance, m.MontantDu - m.MontantPaye,
                    m.DateRappelEcheance, m.DateDernierRappelRetard, aujourdhui, retards);
                if (rappel) { m.DateRappelEcheance = maintenant; resultat.RappelsMensualites++; }
                if (retard) { m.DateDernierRappelRetard = maintenant; resultat.RetardsMensualites++; }
            }

            var loyers = await _context.Loyers
                .Include(l => l.Location).ThenInclude(l => l!.Locataire)
                .Include(l => l.Location).ThenInclude(l => l!.Appartement)
                .Where(l => l.Location!.Statut == StatutLocation.EnCours
                    && l.Statut != StatutMensualite.Payee
                    && l.DateEcheance <= aujourdhui.AddDays(JoursAvantEcheance + 1))
                .ToListAsync(annulation);

            foreach (var l in loyers.Where(l => l.MontantPaye < l.MontantDu).OrderBy(l => l.DateEcheance))
            {
                var libelle = $"loyer {DuMois(l.DateEcheance)}";
                var (rappel, retard) = await TraiterAsync(l.Location!, libelle, l.DateEcheance, l.MontantDu - l.MontantPaye,
                    l.DateRappelEcheance, l.DateDernierRappelRetard, aujourdhui, retards);
                if (rappel) { l.DateRappelEcheance = maintenant; resultat.RappelsLoyers++; }
                if (retard) { l.DateDernierRappelRetard = maintenant; resultat.RetardsLoyers++; }
            }

            // Mémorisé au fur et à mesure du traitement, enregistré avant le récapitulatif.
            await _context.SaveChangesAsync(annulation);

            resultat.EcheancesEnRetard = retards.Count;
            if (retards.Count > 0)
            {
                var admins = await _context.Utilisateurs
                    .Where(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide)
                    .ToListAsync(annulation);
                var tries = retards.OrderByDescending(r => r.JoursRetard).ToList();
                foreach (var admin in admins.Where(a => a.DateDernierRecapRetards?.Date != aujourdhui))
                {
                    if (await _notificationServices.RecapitulatifRetardsAsync(admin, tries))
                    {
                        admin.DateDernierRecapRetards = maintenant;
                        resultat.RecapitulatifsAdmin++;
                    }
                }
                await _context.SaveChangesAsync(annulation);
            }

            // Journal des actions : conservé 2 ans.
            resultat.JournalPurge = await _journal.PurgerAsync(annulation);

            return resultat;
        }
        finally
        {
            Verrou.Release();
        }
    }

    /// <summary>
    /// Séjours confirmés qui commencent demain : email « Votre arrivée demain » au voyageur (adresse, heures, instructions,
    /// téléphone de l'hôte) et copie courte à l'hôte. Mémorisé dans DateEnvoiInstructions seulement si l'envoi au voyageur réussit.
    /// </summary>
    private async Task<int> EnvoyerInstructionsArriveeAsync(DateTime aujourdhui, DateTime maintenant, CancellationToken annulation)
    {
        var demain = aujourdhui.AddDays(1);
        var reservations = await _context.Reservations
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .Where(r => r.Statut == StatutReservation.Confirmee && r.DateArrivee == demain && r.DateEnvoiInstructions == null)
            .ToListAsync(annulation);

        var envoyes = 0;
        foreach (var r in reservations)
        {
            var appartement = r.Appartement!;
            var hote = appartement.Bailleur!;
            var envoye = await _notificationServices.ArriveeDemainAsync(r.Locataire!, r, appartement, ReservationServices.AdresseComplete(appartement),
                AppartementServices.HeureArriveeDe(appartement), AppartementServices.HeureDepartDe(appartement),
                string.IsNullOrWhiteSpace(hote.Telephone) ? null : hote.Telephone);
            if (!envoye)
            {
                continue;
            }
            r.DateEnvoiInstructions = maintenant;
            envoyes++;
            await _notificationServices.ArriveeDemainHoteAsync(hote, r, appartement, r.Locataire!);
        }
        await _context.SaveChangesAsync(annulation);
        return envoyes;
    }

    /// <summary>Envoie, si besoin, le rappel avant échéance ou l'alerte de retard d'une échéance non réglée.</summary>
    /// <returns>Quels emails sont effectivement partis (à mémoriser par l'appelant).</returns>
    private async Task<(bool Rappel, bool Retard)> TraiterAsync(Location location, string libelle, DateTime dateEcheance, decimal resteDu,
        DateTime? dateRappel, DateTime? dateDernierRetard, DateTime aujourdhui, List<NotificationServices.LigneRetard> retards)
    {
        var echeance = dateEcheance.Date;
        var locataire = location.Locataire!;
        var adresse = location.Appartement!.Adresse;

        if (echeance >= aujourdhui)
        {
            var rappelDu = dateRappel is null && echeance <= aujourdhui.AddDays(JoursAvantEcheance);
            return (rappelDu && await _notificationServices.RappelEcheanceAsync(locataire, location.CandidatureId, libelle, echeance, resteDu, adresse), false);
        }

        var joursRetard = (aujourdhui - echeance).Days;
        var reference = location.CandidatureId is { } dossier
            ? $"Dossier n° {dossier:D6} / location n° {location.Id:D6}"
            : $"Location n° {location.Id:D6}";
        retards.Add(new NotificationServices.LigneRetard(locataire.Nom, reference, libelle, echeance, joursRetard, resteDu));

        var alerteDue = dateDernierRetard is null || (aujourdhui - dateDernierRetard.Value.Date).Days >= JoursEntreAlertesRetard;
        return (false, alerteDue && await _notificationServices.RetardEcheanceAsync(locataire, location.CandidatureId, libelle, echeance, joursRetard, resteDu, adresse));
    }

    /// <summary>« d'octobre 2026 », « de novembre 2026 ».</summary>
    private static string DuMois(DateTime date)
    {
        var mois = date.ToString("MMMM yyyy", Fr);
        return "aeiouyâéèêî".Contains(mois[0]) ? $"d'{mois}" : $"de {mois}";
    }
}

/// <summary>
/// Lance les rappels au démarrage puis toutes les 24 h (Rappels:IntervalleHeures), sauf si Rappels:Actif vaut false.
/// Une erreur est journalisée et n'arrête ni l'API ni les exécutions suivantes.
/// </summary>
public class RappelsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RappelsHostedService> _logger;

    public RappelsHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RappelsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Rappels:Actif", true))
        {
            _logger.LogInformation("Rappels automatiques désactivés (Rappels:Actif = false)");
            return;
        }
        var intervalle = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("Rappels:IntervalleHeures", 24.0)));

        // Ne pas retarder le démarrage de l'API.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var resultat = await scope.ServiceProvider.GetRequiredService<RappelsServices>().ExecuterAsync(stoppingToken);
                _logger.LogInformation("Rappels des échéances : {Total} email(s) envoyé(s), {Retards} échéance(s) en retard",
                    resultat.Total, resultat.EcheancesEnRetard);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de l'exécution des rappels des échéances");
            }

            try
            {
                await Task.Delay(intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
