namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Models;

/// <summary>
/// Maintenance fréquente (toutes les 2 min par défaut) de l'état des réservations et des loyers, auparavant faite pendant
/// les lectures (liste admin des réservations, recherche par dates, tableau de bord) sur toute la base :
/// demandes sans réponse de l'hôte depuis 48 h refusées, réservations impayées depuis 24 h annulées, séjours finis terminés
/// (invitation à laisser un avis), loyers du mois créés. Mêmes règles et mêmes emails (une seule fois) qu'avant.
/// Par lots bornés (Maintenance:TailleLot, Maintenance:LotsMaxParPassage) : un arriéré important est rattrapé en plusieurs
/// passages, sans transaction ni salve d'emails géante. Un seul exécutant sur toutes les instances (VerrouPlanifie).
/// </summary>
public class MaintenanceServices
{
    /// <summary>Nom du traitement dans ExecutionsPlanifiees.</summary>
    public const string NomExecution = "maintenance";

    private readonly AppDbContext _context;
    private readonly ReservationServices _reservations;
    private readonly LoyerServices _loyers;
    private readonly LiensFrontend _liens;
    private readonly int _tailleLot;
    private readonly int _lotsMax;

    public MaintenanceServices(AppDbContext context, ReservationServices reservations, LoyerServices loyers, LiensFrontend liens,
        IConfiguration configuration)
    {
        _context = context;
        _reservations = reservations;
        _loyers = loyers;
        _liens = liens;
        _tailleLot = Math.Clamp(configuration.GetValue("Maintenance:TailleLot", 100), 1, 1000);
        _lotsMax = Math.Clamp(configuration.GetValue("Maintenance:LotsMaxParPassage", 5), 1, 100);
    }

    public sealed record Resultat(int DemandesExpirees, int ImpayeesAnnulees, int SejoursTermines, int LocationsLoyersCrees)
    {
        public int Total => DemandesExpirees + ImpayeesAnnulees + SejoursTermines + LocationsLoyersCrees;
    }

    /// <summary>
    /// Passage planifié : rien si une autre instance travaille déjà, ou si le dernier passage (toutes instances confondues)
    /// date de moins de la moitié de <paramref name="intervalle"/>. null si rien n'a été exécuté.
    /// </summary>
    public async Task<Resultat?> ExecuterSiDuAsync(TimeSpan intervalle, CancellationToken annulation = default)
    {
        await using var verrou = await VerrouPlanifie.EssayerAsync(ChaineConnexion, VerrouPlanifie.Maintenance, annulation);
        if (verrou is null)
        {
            return null;
        }
        var derniere = await _context.ExecutionsPlanifiees.AsNoTracking()
            .Where(e => e.Nom == NomExecution).Select(e => (DateTime?)e.DerniereExecution).FirstOrDefaultAsync(annulation);
        if (derniere is { } date && DateTime.UtcNow - date < intervalle / 2)
        {
            return null;
        }
        var resultat = await ExecuterSousVerrouAsync(annulation);
        await EnregistrerExecutionAsync(annulation);
        return resultat;
    }

    private string ChaineConnexion => _context.Database.GetConnectionString()
        ?? throw new InvalidOperationException("Chaîne de connexion PostgreSQL introuvable");

    private async Task<Resultat> ExecuterSousVerrouAsync(CancellationToken annulation)
    {
        // Emails envoyés hors requête HTTP : liens vers Frontend:Url.
        _liens.UtiliserUrlConfiguree = true;

        var demandes = 0;
        for (var i = 0; i < _lotsMax; i++)
        {
            var n = await _reservations.ExpirerDemandesAsync(annulation, _tailleLot);
            demandes += n;
            if (n < _tailleLot)
            {
                break;
            }
        }
        var impayees = await ParLotsAsync(_reservations.ExpirerImpayeesLotAsync, annulation);
        var sejours = await ParLotsAsync(_reservations.TerminerSejoursLotAsync, annulation);
        var loyers = await ParLotsAsync(_loyers.GarantirLoyersLotAsync, annulation);
        return new Resultat(demandes, impayees, sejours, loyers);
    }

    /// <summary>Lots successifs (curseur sur le n°) jusqu'au dernier ou jusqu'au plafond du passage ; le reste attend le suivant.</summary>
    private async Task<int> ParLotsAsync(Func<int, int, CancellationToken, Task<ReservationServices.Lot>> lot, CancellationToken annulation)
    {
        var traitees = 0;
        var apresId = 0;
        for (var i = 0; i < _lotsMax; i++)
        {
            var resultat = await lot(apresId, _tailleLot, annulation);
            traitees += resultat.Traitees;
            if (resultat.Lues < _tailleLot)
            {
                break;
            }
            apresId = resultat.DernierId;
        }
        return traitees;
    }

    private async Task EnregistrerExecutionAsync(CancellationToken annulation)
    {
        _context.ChangeTracker.Clear();
        var execution = await _context.ExecutionsPlanifiees.FirstOrDefaultAsync(e => e.Nom == NomExecution, annulation);
        if (execution is null)
        {
            execution = new ExecutionPlanifiee { Nom = NomExecution };
            _context.ExecutionsPlanifiees.Add(execution);
        }
        execution.DerniereExecution = DateTime.UtcNow;
        execution.Instance = Environment.MachineName.Length > 100 ? Environment.MachineName[..100] : Environment.MachineName;
        await _context.SaveChangesAsync(annulation);
    }
}

/// <summary>
/// Maintenance toutes les Maintenance:IntervalleMinutes (2 par défaut, de 1 à 60), sauf si Maintenance:Actif vaut false.
/// Chaque instance essaie à chaque intervalle ; le verrou PostgreSQL et la date du dernier passage évitent les doublons.
/// Une erreur est journalisée et n'arrête ni l'API ni les passages suivants.
/// </summary>
public class MaintenanceHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<MaintenanceHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Maintenance:Actif", true))
        {
            logger.LogInformation("Maintenance planifiée désactivée (Maintenance:Actif = false)");
            return;
        }
        var intervalle = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Maintenance:IntervalleMinutes", 2.0), 1, 60));

        // Ne pas retarder le démarrage de l'API.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var debut = System.Diagnostics.Stopwatch.GetTimestamp();
                var resultat = await scope.ServiceProvider.GetRequiredService<MaintenanceServices>().ExecuterSiDuAsync(intervalle, stoppingToken);
                if (resultat is { Total: > 0 })
                {
                    logger.LogInformation("Maintenance : {Demandes} demande(s) expirée(s), {Impayees} impayée(s) annulée(s), {Sejours} séjour(s) terminé(s), {Loyers} location(s) avec de nouveaux loyers, en {DureeMs} ms",
                        resultat.DemandesExpirees, resultat.ImpayeesAnnulees, resultat.SejoursTermines, resultat.LocationsLoyersCrees,
                        Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(debut).TotalMilliseconds));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de la maintenance planifiée");
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
