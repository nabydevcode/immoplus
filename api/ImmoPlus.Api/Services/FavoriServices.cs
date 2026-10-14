namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>Logements mis en favori par un utilisateur connecté (locataire ou bailleur).</summary>
public class FavoriServices
{
    private readonly AppDbContext _context;
    private readonly AppartementServices _appartementServices;

    public FavoriServices(AppDbContext context, AppartementServices appartementServices)
    {
        _context = context;
        _appartementServices = appartementServices;
    }

    /// <summary>Idempotent : un logement déjà en favori le reste, sans erreur.</summary>
    public async Task AjouterAsync(int utilisateurId, int appartementId)
    {
        // Une annonce supprimée ou d'un hôte désactivé n'est plus visible : on ne peut pas l'ajouter (celles déjà en favori y restent).
        var visible = await _context.Appartements.AnyAsync(a => a.Id == appartementId
            && (a.BailleurId == utilisateurId || (!a.EstDesactive && a.Bailleur!.Statut != StatutUtilisateur.Desactive)));
        if (!visible)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (await _context.Favoris.AnyAsync(f => f.UtilisateurId == utilisateurId && f.AppartementId == appartementId))
        {
            return;
        }
        await _context.Favoris.AddAsync(new Favori { UtilisateurId = utilisateurId, AppartementId = appartementId, DateCreation = DateTime.UtcNow });
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double clic : la clé composite garantit un seul favori, l'autre requête l'a déjà créé.
        }
    }

    public async Task RetirerAsync(int utilisateurId, int appartementId) =>
        await _context.Favoris.Where(f => f.UtilisateurId == utilisateurId && f.AppartementId == appartementId).ExecuteDeleteAsync();

    /// <summary>Du plus récent au plus ancien, y compris les logements devenus indisponibles (champ disponible).</summary>
    public async Task<List<AppartementResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role) =>
        await _appartementServices.ListerParIdsAsync(await IdsAsync(utilisateurId), utilisateurId, role);

    public async Task<List<int>> IdsAsync(int utilisateurId) =>
        await _context.Favoris.AsNoTracking()
            .Where(f => f.UtilisateurId == utilisateurId)
            .OrderByDescending(f => f.DateCreation).ThenByDescending(f => f.AppartementId)
            .Select(f => f.AppartementId)
            .ToListAsync();
}
