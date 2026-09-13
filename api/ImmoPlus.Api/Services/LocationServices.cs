namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class LocationServices
{
    private readonly AppDbContext _context;

    public LocationServices(AppDbContext context)
    {
        _context = context;
    }

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

        return await requete
            .Select(l => new LocationResponseDto
            {
                Id = l.Id,
                LocataireId = l.LocataireId,
                LocataireNom = l.Locataire!.Nom,
                AppartementId = l.AppartementId,
                AppartementAdresse = l.Appartement!.Adresse,
                CandidatureId = l.CandidatureId,
                DateDebut = l.DateDebut,
                DateFin = l.DateFin,
                Statut = l.Statut.ToString()
            }).ToListAsync();
    }

    public async Task<LocationResponseDto> RechercherLocation(int id)
    {
        var location = await _context.Locations
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new LocationResponseDto
            {
                Id = l.Id,
                LocataireId = l.LocataireId,
                LocataireNom = l.Locataire!.Nom,
                AppartementId = l.AppartementId,
                AppartementAdresse = l.Appartement!.Adresse,
                CandidatureId = l.CandidatureId,
                DateDebut = l.DateDebut,
                DateFin = l.DateFin,
                Statut = l.Statut.ToString()
            })
            .FirstOrDefaultAsync();

        if (location is null)
        {
            throw new NotFoundException("Cette location n'existe pas");
        }
        return location;
    }
}
