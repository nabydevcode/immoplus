namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class MensualiteServices
{
    private readonly AppDbContext _context;

    public MensualiteServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task GenererEcheancierAsync(int financementLocationId, int utilisateurId, RoleUtilisateur role)
    {
        var financement = await ChargerAvecAutorisationAsync(financementLocationId, utilisateurId, role);

        var echeancierExiste = await _context.Mensualites.AnyAsync(m => m.FinancementLocationId == financementLocationId);
        if (echeancierExiste)
        {
            throw new ConflictException("L'échéancier a déjà été généré pour ce financement");
        }

        var dateDepart = DateTime.UtcNow.Date;
        for (int i = 1; i <= financement.NombreMensualites; i++)
        {
            await _context.Mensualites.AddAsync(new Mensualite
            {
                FinancementLocationId = financementLocationId,
                NumeroEcheance = i,
                DateEcheance = dateDepart.AddMonths(i - 1),
                MontantDu = financement.MontantMensualite,
                MontantPaye = 0,
                Statut = StatutMensualite.AVenir
            });
        }
        financement.Statut = StatutFinancement.EnCours;
        await _context.SaveChangesAsync();
    }

    public async Task<List<MensualiteResponseDto>> ListerParFinancementAsync(int financementId, int utilisateurId, RoleUtilisateur role)
    {
        await ChargerAvecAutorisationAsync(financementId, utilisateurId, role);

        return await _context.Mensualites
            .AsNoTracking()
            .Where(m => m.FinancementLocationId == financementId)
            .OrderBy(m => m.NumeroEcheance)
            .Select(m => new MensualiteResponseDto
            {
                Id = m.Id,
                FinancementLocationId = m.FinancementLocationId,
                NumeroEcheance = m.NumeroEcheance,
                DateEcheance = m.DateEcheance,
                MontantDu = m.MontantDu,
                MontantPaye = m.MontantPaye,
                Statut = m.Statut.ToString()
            })
            .ToListAsync();
    }

    private async Task<FinancementLocation> ChargerAvecAutorisationAsync(int financementLocationId, int utilisateurId, RoleUtilisateur role)
    {
        var financement = await _context.FinancementLocations
            .Include(f => f.Location).ThenInclude(l => l!.Appartement)
            .FirstOrDefaultAsync(f => f.Id == financementLocationId);

        if (financement is null)
        {
            throw new NotFoundException("Ce financement n'existe pas");
        }

        var location = financement.Location!;
        var autorise = role == RoleUtilisateur.Admin
            || (role == RoleUtilisateur.Locataire && location.LocataireId == utilisateurId)
            || (role == RoleUtilisateur.Bailleur && location.Appartement!.BailleurId == utilisateurId);

        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à ce financement");
        }

        return financement;
    }
}
