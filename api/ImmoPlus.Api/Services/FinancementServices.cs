namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class FinancementServices
{
    private readonly AppDbContext _context;

    public FinancementServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task<FinancementLocationResponseDto> CreerFinancementLocation(FinancementLocationDto dto)
    {
        var location = await _context.Locations
            .Include(l => l.Appartement)
            .FirstOrDefaultAsync(l => l.Id == dto.LocationId);
        if (location is null)
        {
            throw new NotFoundException("Cette location n'existe pas");
        }
        if (location.Statut != StatutLocation.EnCours)
        {
            throw new ConflictException("Cette location n'est plus en cours : impossible de lui associer un financement");
        }

        var financementExiste = await _context.FinancementLocations.AnyAsync(f => f.LocationId == dto.LocationId);
        if (financementExiste)
        {
            throw new ConflictException("Cette location a déjà un financement associé");
        }

        var financement = ConstruireFinancement(location, location.Appartement!, dto.ApportInitial, dto.TauxFrais);

        await _context.FinancementLocations.AddAsync(financement);
        await _context.SaveChangesAsync();

        return VersDto(financement);
    }

    // Utilisé en interne par CandidatureServices au moment de l'acceptation, si l'apport est insuffisant.
    public async Task<FinancementLocation> CreerPourLocationAsync(Location location, Appartement appartement, decimal apportInitial, decimal tauxFrais)
    {
        var financement = ConstruireFinancement(location, appartement, apportInitial, tauxFrais);
        await _context.FinancementLocations.AddAsync(financement);
        await _context.SaveChangesAsync();
        return financement;
    }

    private static FinancementLocation ConstruireFinancement(Location location, Appartement appartement, decimal apportInitial, decimal tauxFrais)
    {
        var loyerMensuel = appartement.LoyerMensuel!.Value;
        var dureeMois = appartement.DureeAvanceExigee!.Value;
        var montantTotal = loyerMensuel * dureeMois;

        var apportMinimum = loyerMensuel * 2;
        if (apportInitial < apportMinimum)
        {
            throw new ConflictException($"L'apport initial doit être d'au moins {apportMinimum} GNF (2 mois de loyer)");
        }
        if (tauxFrais < 0.02m || tauxFrais > 0.05m)
        {
            throw new ConflictException("Le taux de frais doit être compris entre 2% et 5%");
        }

        var montantARembourser = montantTotal - apportInitial;
        var nombreMensualites = dureeMois - (int)(apportInitial / loyerMensuel);
        if (nombreMensualites <= 0)
        {
            throw new ConflictException("L'apport initial couvre déjà la totalité de la durée exigée");
        }

        // Montants au franc près (le GNF n'a pas de centimes) : voir Echeancier pour la dernière mensualité.
        var (montantMensualite, _) = Echeancier(montantARembourser, tauxFrais, nombreMensualites);

        return new FinancementLocation
        {
            LocationId = location.Id,
            MontantTotal = montantTotal,
            DureeMois = dureeMois,
            ApportInitial = apportInitial,
            MontantARembourser = montantARembourser,
            TauxFrais = tauxFrais,
            NombreMensualites = nombreMensualites,
            MontantMensualite = montantMensualite,
            Statut = StatutFinancement.EnAttenteValidation
        };
    }

    /// <summary>
    /// Mensualités arrondies au franc (MidpointRounding.AwayFromZero) : le total dû (capital + frais) est arrondi au franc,
    /// chaque mensualité aussi, et la dernière absorbe l'écart pour que leur somme soit exactement ce total.
    /// </summary>
    public static (decimal Mensualite, decimal Derniere) Echeancier(decimal montantARembourser, decimal tauxFrais, int nombreMensualites)
    {
        var totalDu = Math.Round(montantARembourser * (1 + tauxFrais), 0, MidpointRounding.AwayFromZero);
        var mensualite = Math.Round(totalDu / nombreMensualites, 0, MidpointRounding.AwayFromZero);
        return (mensualite, totalDu - mensualite * (nombreMensualites - 1));
    }

    /// <summary>
    /// Financements visibles (locataire : les siens ; admin : tous), paginés, les plus récents d'abord (n° décroissant).
    /// statut : EnAttenteValidation, EnCours ou Solde. q : n° de financement ou de location, nom du locataire ou adresse.
    /// </summary>
    public async Task<PagedResultDto<FinancementLocationResponseDto>> ListerFinancementLoctions(int utilisateurId, RoleUtilisateur role,
        Pagination pagination, string? statut = null, string? q = null)
    {
        var requete = _context.FinancementLocations.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(f => f.Location!.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(f => f.Location!.Appartement!.BailleurId == utilisateurId);
        }
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (int.TryParse(statut, out _) || !Enum.TryParse<StatutFinancement>(statut.Trim(), true, out var s) || !Enum.IsDefined(s))
            {
                throw new InvalidOperationException("Statut invalide. Utilisez EnAttenteValidation, EnCours ou Solde");
            }
            requete = requete.Where(f => f.Statut == s);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var numero = RechercheTexte.Reference(q);
            var motif = RechercheTexte.Contient(q);
            requete = requete.Where(f => f.Id == numero || f.LocationId == numero
                || EF.Functions.ILike(AppDbContext.SansAccents(f.Location!.Locataire!.Nom), AppDbContext.SansAccents(motif))
                || EF.Functions.ILike(AppDbContext.SansAccents(f.Location!.Appartement!.Adresse), AppDbContext.SansAccents(motif)));
        }

        // Échéancier et paiements chargés pour la seule page (bilan des retards), en requêtes séparées (pas de produit cartésien).
        var (financements, total) = await requete
            .OrderByDescending(f => f.Id)
            .Include(f => f.Location).ThenInclude(l => l!.Locataire)
            .Include(f => f.Location).ThenInclude(l => l!.Appartement)
            .Include(f => f.Mensualites).ThenInclude(m => m.Paiements)
            .AsSplitQuery()
            .PageAsync(pagination);
        return PagedResultDto<FinancementLocationResponseDto>.Creer(financements.Select(VersDto).ToList(), pagination, total);
    }

    private static FinancementLocationResponseDto VersDto(FinancementLocation f)
    {
        var dto = VersDtoSansBilan(f);
        SuiviRetards.RemplirBilan(dto, f.Mensualites, DateTime.UtcNow);
        return dto;
    }

    private static FinancementLocationResponseDto VersDtoSansBilan(FinancementLocation f) => new()
    {
        Id = f.Id,
        LocationId = f.LocationId,
        CandidatureId = f.Location?.CandidatureId,
        LocataireNom = f.Location?.Locataire?.Nom ?? "",
        AppartementAdresse = f.Location?.Appartement?.Adresse ?? "",
        MontantTotal = f.MontantTotal,
        DureeMois = f.DureeMois,
        ApportInitial = f.ApportInitial,
        MontantARembourser = f.MontantARembourser,
        TauxFrais = f.TauxFrais,
        NombreMensualites = f.NombreMensualites,
        MontantMensualite = f.MontantMensualite,
        Statut = f.Statut.ToString()
    };
}
