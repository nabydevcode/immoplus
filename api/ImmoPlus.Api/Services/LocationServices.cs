namespace ImmoPlus.Api.Services;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class LocationServices
{
    /// <summary>Durée légale du préavis avant la fin d'une location.</summary>
    public const int PreavisMois = 3;

    private readonly AppDbContext _context;
    private readonly NotificationServices _notificationServices;
    private readonly LoyerServices _loyerServices;
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    public LocationServices(AppDbContext context, NotificationServices notificationServices, LoyerServices loyerServices)
    {
        _context = context;
        _loyerServices = loyerServices;
        _notificationServices = notificationServices;
    }

    private static readonly Expression<Func<Location, LocationResponseDto>> VersDto = l => new LocationResponseDto
    {
        Id = l.Id,
        LocataireId = l.LocataireId,
        LocataireNom = l.Locataire!.Nom,
        AppartementId = l.AppartementId,
        AppartementAdresse = l.Appartement!.Adresse,
        CandidatureId = l.CandidatureId,
        DateDebut = l.DateDebut,
        DateFin = l.DateFin,
        Statut = l.Statut.ToString(),
        DateApportPaye = l.DateApportPaye,
        DateRemiseCles = l.DateRemiseCles,
        DatePreavis = l.DatePreavis,
        DateFinPrevue = l.DateFinPrevue,
        PreavisDonnePar = l.PreavisDonnePar,
        MotifPreavis = l.MotifPreavis
    };

    // Créée uniquement en interne (par CandidatureServices lors de l'acceptation d'une candidature).
    // Pas de route publique de création : une location longue durée naît toujours d'une candidature acceptée.
    public async Task<Location> CreerDepuisCandidatureAsync(Candidature candidature)
    {
        var location = new Location
        {
            LocataireId = candidature.LocataireId,
            AppartementId = candidature.AppartementId,
            CandidatureId = candidature.Id,
            DateDebut = DateTime.UtcNow.Date,
            Statut = StatutLocation.EnCours
        };
        await _context.Locations.AddAsync(location);
        await _context.SaveChangesAsync();
        return location;
    }

    public async Task<List<LocationResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role)
    {
        var requete = _context.Locations.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(l => l.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(l => l.Appartement!.BailleurId == utilisateurId);
        }
        // Admin : pas de filtre, toutes les locations.

        return await CompleterAvisAsync(await requete.Select(VersDto).ToListAsync());
    }

    /// <summary>Avis déjà donnés et fenêtre d'avis de chaque location (calculés, rien n'est stocké).</summary>
    private async Task<List<LocationResponseDto>> CompleterAvisAsync(List<LocationResponseDto> locations)
    {
        var ids = locations.Select(l => l.Id).ToList();
        var avis = (await _context.Avis.AsNoTracking()
                .Where(a => a.LocationId != null && ids.Contains(a.LocationId!.Value))
                .Select(a => new { LocationId = a.LocationId!.Value, a.Sens })
                .ToListAsync())
            .Select(a => (a.LocationId, a.Sens))
            .ToHashSet();
        foreach (var l in locations)
        {
            l.AvisLocataireDonne = avis.Contains((l.Id, SensAvis.LocataireVersLogement));
            l.AvisBailleurDonne = avis.Contains((l.Id, SensAvis.BailleurVersLocataire));
            (l.AvisPossible, l.AvisPossibleDepuis, l.AvisPossibleJusquau) =
                AvisServices.FenetreAvisLocation(l.Statut, l.DateFin, l.DateRemiseCles, l.DateFinPrevue);
        }
        return locations;
    }

    public async Task<LocationResponseDto> RechercherLocation(int id, int utilisateurId, RoleUtilisateur role)
    {
        var location = await _context.Locations.AsNoTracking()
            .Where(l => l.Id == id)
            .Where(l => role == RoleUtilisateur.Admin
                || (role == RoleUtilisateur.Locataire && l.LocataireId == utilisateurId)
                || (role == RoleUtilisateur.Bailleur && l.Appartement!.BailleurId == utilisateurId))
            .Select(VersDto)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Cette location n'existe pas");
        return (await CompleterAvisAsync([location]))[0];
    }

    private async Task<Location> ChargerAsync(int id) =>
        await _context.Locations
            .Include(l => l.Locataire)
            .Include(l => l.Appartement).ThenInclude(a => a!.Bailleur)
            .Include(l => l.FinancementLocation)
            .FirstOrDefaultAsync(l => l.Id == id)
        ?? throw new NotFoundException("Cette location n'existe pas");

    private async Task<LocationResponseDto> RelireAsync(int id) =>
        (await CompleterAvisAsync([await _context.Locations.AsNoTracking().Where(l => l.Id == id).Select(VersDto).FirstAsync()]))[0];

    /// <summary>Locataire, bailleur et admins concernés par la fin de location.</summary>
    private async Task<List<Utilisateur>> DestinatairesAsync(Location location)
    {
        var admins = await _context.Utilisateurs
            .Where(u => u.Role == RoleUtilisateur.Admin && u.Statut == StatutUtilisateur.Valide)
            .ToListAsync();
        return new[] { location.Locataire!, location.Appartement!.Bailleur! }.Concat(admins).ToList();
    }

    /// <summary>
    /// Préavis de fin de location : la location se termine 3 mois après. Donné par le locataire lui-même,
    /// ou par l'admin pour le compte du locataire ou du bailleur.
    /// </summary>
    public async Task<LocationResponseDto> DonnerPreavisAsync(int id, DonnerPreavisDto dto, int utilisateurId, RoleUtilisateur role)
    {
        var location = await ChargerAsync(id);
        string donnePar;
        if (role == RoleUtilisateur.Locataire)
        {
            if (location.LocataireId != utilisateurId)
            {
                throw new ConflictException("Vous n'avez pas accès à cette location");
            }
            donnePar = "Locataire";
        }
        else
        {
            donnePar = dto.DonnePar is "Locataire" or "Bailleur"
                ? dto.DonnePar
                : throw new InvalidOperationException("Indiquez qui donne le préavis : Locataire ou Bailleur");
        }

        if (location.Statut != StatutLocation.EnCours)
        {
            throw new ConflictException("Cette location n'est plus en cours");
        }
        if (location.DateRemiseCles is null)
        {
            throw new ConflictException("Les clés n'ont pas encore été remises : la location n'a pas commencé");
        }
        if (location.DatePreavis is not null)
        {
            throw new ConflictException($"Un préavis est déjà en cours : fin prévue le {location.DateFinPrevue:dd/MM/yyyy}");
        }

        var aujourdhui = DateTime.UtcNow.Date;
        location.DatePreavis = aujourdhui;
        location.DateFinPrevue = aujourdhui.AddMonths(PreavisMois);
        location.PreavisDonnePar = donnePar;
        location.MotifPreavis = string.IsNullOrWhiteSpace(dto.Motif) ? null : dto.Motif.Trim();
        await _context.SaveChangesAsync();

        foreach (var destinataire in await DestinatairesAsync(location))
        {
            await _notificationServices.PreavisEnregistreAsync(destinataire, location);
        }
        return await RelireAsync(id);
    }

    /// <summary>Annulation du préavis avant la date de fin : par l'admin, ou par le locataire s'il l'avait donné.</summary>
    public async Task<LocationResponseDto> AnnulerPreavisAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var location = await ChargerAsync(id);
        if (role == RoleUtilisateur.Locataire && (location.LocataireId != utilisateurId || location.PreavisDonnePar != "Locataire"))
        {
            throw new ConflictException("Seul l'administrateur peut annuler ce préavis");
        }
        if (location.DatePreavis is null || location.Statut != StatutLocation.EnCours)
        {
            throw new ConflictException("Aucun préavis en cours sur cette location");
        }
        if (location.DateFinPrevue <= DateTime.UtcNow.Date)
        {
            throw new ConflictException("La date de fin est atteinte : le préavis ne peut plus être annulé");
        }

        location.DatePreavis = null;
        location.DateFinPrevue = null;
        location.PreavisDonnePar = null;
        location.MotifPreavis = null;
        await _context.SaveChangesAsync();

        foreach (var destinataire in await DestinatairesAsync(location))
        {
            await _notificationServices.PreavisAnnuleAsync(destinataire, location);
        }
        return await RelireAsync(id);
    }

    /// <summary>
    /// Clôture (admin) à la date de fin prévue : la location est terminée et le logement redevient disponible.
    /// Le financement éventuel doit être soldé.
    /// </summary>
    public async Task<LocationResponseDto> TerminerAsync(int id)
    {
        var location = await ChargerAsync(id);
        if (location.Statut != StatutLocation.EnCours)
        {
            throw new ConflictException("Cette location n'est plus en cours");
        }
        if (location.DateFinPrevue is null)
        {
            throw new ConflictException($"Une location se termine après un préavis de {PreavisMois} mois : enregistrez d'abord le préavis");
        }
        if (location.DateFinPrevue > DateTime.UtcNow.Date)
        {
            throw new ConflictException($"Le préavis court jusqu'au {location.DateFinPrevue:dd/MM/yyyy} : la location ne peut pas être terminée avant");
        }
        if (location.FinancementLocation is { Statut: not StatutFinancement.Solde })
        {
            throw new ConflictException("Le financement de l'avance doit être entièrement remboursé avant de terminer la location");
        }

        // Tous les loyers des mois occupés (échéance avant la date de fin) doivent être réglés.
        // On crée d'abord ceux qui manqueraient, faute de consultation de la location.
        await _context.Entry(location).Collection(l => l.Loyers).LoadAsync();
        await _loyerServices.GarantirLoyersAsync(location);
        var impayes = location.Loyers
            .Where(l => l.DateEcheance < location.DateFinPrevue && l.MontantPaye < l.MontantDu)
            .ToList();
        if (impayes.Count > 0)
        {
            var resteDu = impayes.Sum(l => l.MontantDu - l.MontantPaye);
            var libelle = impayes.Count > 1 ? $"{impayes.Count} loyers restent impayés" : "1 loyer reste impayé";
            throw new ConflictException($"{libelle} ({resteDu.ToString("N0", Fr)} GNF) : enregistrez leur paiement avant de terminer la location");
        }

        location.Statut = StatutLocation.Terminee;
        location.DateFin = location.DateFinPrevue;
        location.Appartement!.Disponible = true;
        await _context.SaveChangesAsync();

        foreach (var destinataire in new[] { location.Locataire!, location.Appartement.Bailleur! })
        {
            await _notificationServices.LocationTermineeAsync(destinataire, location);
        }
        return await RelireAsync(id);
    }
}
