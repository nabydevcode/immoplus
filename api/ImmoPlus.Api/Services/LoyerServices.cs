namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Loyers mensuels, sans frais, après la période couverte par l'avance.
/// L'avance (ex. 6 mois de loyer, apport + financement) couvre les premiers mois à partir de la remise des clés ;
/// le premier loyer est dû le mois suivant. Les échéances sont créées au fil de l'eau, un mois à l'avance.
/// </summary>
public class LoyerServices
{
    private readonly AppDbContext _context;

    public LoyerServices(AppDbContext context)
    {
        _context = context;
    }

    public static DateTime? DebutLoyers(Location location, Appartement appartement) =>
        location.DateRemiseCles is null ? null : location.DateDebut.Date.AddMonths(appartement.DureeAvanceExigee ?? 0);

    /// <summary>
    /// Crée les loyers manquants de toutes les locations en cours (rappels quotidiens), pour qu'un loyer soit rappelé et
    /// compté dans les retards même si personne n'a consulté la location. Par lots, comme la maintenance planifiée.
    /// </summary>
    public async Task GarantirLoyersLocationsEnCoursAsync(CancellationToken annulation = default)
    {
        var apresId = 0;
        while (true)
        {
            var lot = await GarantirLoyersLotAsync(apresId, 200, annulation);
            if (lot.Lues < 200)
            {
                return;
            }
            apresId = lot.DernierId;
        }
    }

    /// <summary>
    /// Un lot de locations en cours sans loyer à jour, de n° supérieur à <paramref name="apresId"/> (curseur : une location
    /// dont les loyers ne peuvent pas encore commencer ne bloque pas les suivantes).
    /// </summary>
    public async Task<ReservationServices.Lot> GarantirLoyersLotAsync(int apresId, int taille, CancellationToken annulation = default)
    {
        // Une location dont un loyer tombe après « limite − 1 mois + 3 jours » est à jour : le suivant (≥ 28 jours plus tard)
        // dépasserait la limite (le mois prochain). Seules les autres sont chargées.
        var dejaAJour = DateTime.UtcNow.Date.AddMonths(1).AddMonths(-1).AddDays(3);
        var locations = await _context.Locations
            .Include(l => l.Appartement)
            .Include(l => l.Loyers)
            .Where(l => l.Id > apresId && l.Statut == StatutLocation.EnCours && l.DateRemiseCles != null
                && !l.Loyers.Any(x => x.DateEcheance > dejaAJour))
            .OrderBy(l => l.Id)
            .Take(taille)
            .AsSplitQuery()
            .ToListAsync(annulation);
        var crees = 0;
        foreach (var location in locations)
        {
            var avant = location.Loyers.Count;
            await GarantirLoyersAsync(location);
            crees += location.Loyers.Count > avant ? 1 : 0;
        }
        return new ReservationServices.Lot(locations.Count, locations.Count == 0 ? apresId : locations[^1].Id, crees);
    }

    /// <summary>
    /// Crée les loyers manquants jusqu'au mois prochain inclus, tant que la location est en cours.
    /// La location doit être chargée avec son appartement et ses loyers.
    /// </summary>
    public async Task GarantirLoyersAsync(Location location)
    {
        var debut = DebutLoyers(location, location.Appartement!);
        if (debut is null || location.Statut != StatutLocation.EnCours || location.Appartement!.LoyerMensuel is null)
        {
            return;
        }

        var limite = DateTime.UtcNow.Date.AddMonths(1);
        // Préavis donné : pas de loyer pour une période qui commence à la date de fin ou après.
        if (location.DateFinPrevue is { } fin && fin.AddDays(-1) < limite)
        {
            limite = fin.AddDays(-1);
        }
        var numero = location.Loyers.Count;
        var nouveaux = new List<Loyer>();
        while (debut.Value.AddMonths(numero) <= limite)
        {
            var loyer = new Loyer
            {
                Numero = numero + 1,
                DateEcheance = debut.Value.AddMonths(numero),
                MontantDu = location.Appartement.LoyerMensuel.Value,
                Statut = StatutMensualite.AVenir
            };
            location.Loyers.Add(loyer);
            nouveaux.Add(loyer);
            numero++;
        }
        if (nouveaux.Count == 0)
        {
            return;
        }
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Index unique (LocationId, Numero) : une requête simultanée vient de les créer. On abandonne les nôtres et on relit.
            foreach (var loyer in nouveaux)
            {
                location.Loyers.Remove(loyer);
                _context.Entry(loyer).State = EntityState.Detached;
            }
            await _context.Entry(location).Collection(l => l.Loyers).LoadAsync();
        }
    }

    public async Task<LoyersLocationDto> ListerAsync(int locationId, int utilisateurId, RoleUtilisateur role)
    {
        var location = await _context.Locations
            .Include(l => l.Appartement)
            .Include(l => l.Loyers).ThenInclude(l => l.Paiements)
            .FirstOrDefaultAsync(l => l.Id == locationId)
            ?? throw new NotFoundException("Cette location n'existe pas");

        // Comme le financement : géré par l'admin, consulté par le locataire concerné.
        if (role != RoleUtilisateur.Admin && !(role == RoleUtilisateur.Locataire && location.LocataireId == utilisateurId))
        {
            throw new ConflictException("Vous n'avez pas accès à ces loyers");
        }

        await GarantirLoyersAsync(location);

        var aujourdhui = DateTime.UtcNow;
        var loyers = location.Loyers.OrderBy(l => l.Numero).Select(l => SuiviRetards.VersDto(l, aujourdhui)).ToList();
        return new LoyersLocationDto
        {
            LocationId = location.Id,
            LoyerMensuel = location.Appartement!.LoyerMensuel ?? 0,
            DebutLoyers = DebutLoyers(location, location.Appartement),
            Loyers = loyers,
            PayesATemps = loyers.Count(l => l.Ponctualite == SuiviRetards.ATemps),
            PayesEnRetard = loyers.Count(l => l.Ponctualite == SuiviRetards.PayeeEnRetard),
            EnRetard = loyers.Count(l => l.Ponctualite == SuiviRetards.EnRetard),
            JoursRetardCumules = loyers.Sum(l => l.JoursRetard),
            MontantEnRetard = loyers.Where(l => l.Ponctualite == SuiviRetards.EnRetard).Sum(l => l.MontantDu - l.MontantPaye)
        };
    }
}
