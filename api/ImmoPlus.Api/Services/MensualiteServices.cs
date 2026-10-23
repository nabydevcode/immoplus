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
    private readonly NotificationServices _notificationServices;

    public MensualiteServices(AppDbContext context, NotificationServices notificationServices)
    {
        _context = context;
        _notificationServices = notificationServices;
    }

    public async Task GenererEcheancierAsync(int financementLocationId, int utilisateurId, RoleUtilisateur role)
    {
        // Double clic : la seconde génération attend la première, puis voit l'échéancier existant (409) au lieu de le dupliquer.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.VerrouillerAsync(RessourceVerrouillee.Financement, financementLocationId);
        var financement = await ChargerAvecAutorisationAsync(financementLocationId, utilisateurId, role);
        if (financement.Location!.DateApportPaye is null)
        {
            throw new ConflictException("L'échéancier se génère après le paiement de l'apport");
        }

        var echeancierExiste = await _context.Mensualites.AnyAsync(m => m.FinancementLocationId == financementLocationId);
        if (echeancierExiste)
        {
            throw new ConflictException("L'échéancier a déjà été généré pour ce financement");
        }

        var dateDepart = DateTime.UtcNow.Date;
        // Arrondi au franc, dernière mensualité ajustée : recalculé depuis le capital et le taux (un financement créé avant
        // cette règle peut porter une mensualité à centimes). Les échéanciers déjà générés ne sont jamais recalculés.
        var (montantMensualite, derniere) = FinancementServices.Echeancier(financement.MontantARembourser, financement.TauxFrais, financement.NombreMensualites);
        financement.MontantMensualite = montantMensualite;
        var mensualites = new List<Mensualite>();
        for (int i = 1; i <= financement.NombreMensualites; i++)
        {
            mensualites.Add(new Mensualite
            {
                FinancementLocationId = financementLocationId,
                NumeroEcheance = i,
                DateEcheance = dateDepart.AddMonths(i - 1),
                MontantDu = i == financement.NombreMensualites ? derniere : montantMensualite,
                MontantPaye = 0,
                Statut = StatutMensualite.AVenir
            });
        }
        await _context.Mensualites.AddRangeAsync(mensualites);
        financement.Statut = StatutFinancement.EnCours;
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        await _notificationServices.EcheancierGenereAsync(financement.Location!.Locataire!, financement, mensualites);
    }

    public async Task<List<MensualiteResponseDto>> ListerParFinancementAsync(int financementId, int utilisateurId, RoleUtilisateur role)
    {
        await ChargerAvecAutorisationAsync(financementId, utilisateurId, role);

        var mensualites = await _context.Mensualites
            .AsNoTracking()
            .Include(m => m.Paiements)
            .Where(m => m.FinancementLocationId == financementId)
            .OrderBy(m => m.NumeroEcheance)
            .ToListAsync();
        var aujourdhui = DateTime.UtcNow;
        return mensualites.Select(m => SuiviRetards.VersDto(m, aujourdhui)).ToList();
    }

    private async Task<FinancementLocation> ChargerAvecAutorisationAsync(int financementLocationId, int utilisateurId, RoleUtilisateur role)
    {
        var financement = await _context.FinancementLocations
            .Include(f => f.Location).ThenInclude(l => l!.Appartement)
            .Include(f => f.Location).ThenInclude(l => l!.Locataire)
            .FirstOrDefaultAsync(f => f.Id == financementLocationId);

        if (financement is null)
        {
            throw new NotFoundException("Ce financement n'existe pas");
        }

        var location = financement.Location!;
        var autorise = role == RoleUtilisateur.Admin
            || (role == RoleUtilisateur.Locataire && location.LocataireId == utilisateurId);

        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à ce financement");
        }

        return financement;
    }
}
