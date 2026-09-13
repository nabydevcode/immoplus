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

        var mensualiteBase = montantARembourser / nombreMensualites;
        var montantMensualite = mensualiteBase * (1 + tauxFrais);

        return new FinancementLocation
        {
            LocationId = location.Id,
            MontantTotal = montantTotal,
            DureeMois = dureeMois,
            ApportInitial = apportInitial,
            MontantARembourser = montantARembourser,
            TauxFrais = tauxFrais,
            NombreMensualites = nombreMensualites,
            MontantMensualite = Math.Round(montantMensualite, 2),
            Statut = StatutFinancement.EnAttenteValidation
        };
    }

    public async Task<List<FinancementLocationResponseDto>> ListerFinancementLoctions(int utilisateurId, RoleUtilisateur role)
    {
        var requete = _context.FinancementLocations.AsNoTracking().Include(f => f.Location).AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(f => f.Location!.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(f => f.Location!.Appartement!.BailleurId == utilisateurId);
        }

        var financements = await requete.ToListAsync();
        return financements.Select(VersDto).ToList();
    }

    public async Task<FinancementLocationResponseDto> RechercherFinancement(int id)
    {
        var financement = await _context.FinancementLocations.FirstOrDefaultAsync(f => f.Id == id);
        if (financement is null)
        {
            throw new NotFoundException("Ce financement n'existe pas");
        }
        return VersDto(financement);
    }

    private static FinancementLocationResponseDto VersDto(FinancementLocation f) => new()
    {
        Id = f.Id,
        LocationId = f.LocationId,
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
