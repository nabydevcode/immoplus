namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class CandidatureServices
{
    private readonly AppDbContext _context;
    private readonly LocationServices _locationServices;
    private readonly FinancementServices _financementServices;

    public CandidatureServices(AppDbContext context, LocationServices locationServices, FinancementServices financementServices)
    {
        _context = context;
        _locationServices = locationServices;
        _financementServices = financementServices;
    }

    public async Task<CandidatureResponseDto> CreerAsync(int locataireId, CandidatureDto dto)
    {
        var appartement = await _context.Appartements.FindAsync(dto.AppartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (appartement.TypeOffre != TypeOffre.LongueDuree)
        {
            throw new ConflictException("Cette annonce est un séjour court : réservez directement, pas besoin de candidature");
        }
        if (!appartement.Disponible)
        {
            throw new ConflictException("Cet appartement n'est plus disponible");
        }
        if (!Enum.TryParse<SituationProfessionnelle>(dto.SituationProfessionnelle, true, out var situation))
        {
            throw new InvalidOperationException("SituationProfessionnelle invalide");
        }

        var dejaCandidat = await _context.Candidatures.AnyAsync(c =>
            c.LocataireId == locataireId && c.AppartementId == dto.AppartementId && c.Statut == StatutCandidature.EnAttente);
        if (dejaCandidat)
        {
            throw new ConflictException("Vous avez déjà une candidature en attente pour cet appartement");
        }

        var candidature = new Candidature
        {
            LocataireId = locataireId,
            AppartementId = dto.AppartementId,
            ApportInitial = dto.ApportInitial,
            SituationProfessionnelle = situation,
            Statut = StatutCandidature.EnAttente,
            DateSoumission = DateTime.UtcNow
        };

        if (dto.Garant is not null)
        {
            if (!Enum.TryParse<SituationProfessionnelle>(dto.Garant.SituationProfessionnelle, true, out var situationGarant))
            {
                throw new InvalidOperationException("SituationProfessionnelle du garant invalide");
            }
            candidature.Garant = new Garant
            {
                Nom = dto.Garant.Nom,
                Telephone = dto.Garant.Telephone,
                Email = dto.Garant.Email,
                SituationProfessionnelle = situationGarant
            };
        }

        await _context.Candidatures.AddAsync(candidature);
        await _context.SaveChangesAsync();

        return await RechercherAsync(candidature.Id, locataireId, RoleUtilisateur.Locataire);
    }

    public async Task<PagedResultDto<CandidatureResponseDto>> ListerAsync(int utilisateurId, RoleUtilisateur role, int page, int taille)
    {
        if (page < 1) page = 1;
        if (taille < 1 || taille > 100) taille = 20;

        var requete = _context.Candidatures.AsNoTracking().AsQueryable();

        if (role == RoleUtilisateur.Locataire)
        {
            requete = requete.Where(c => c.LocataireId == utilisateurId);
        }
        else if (role == RoleUtilisateur.Bailleur)
        {
            requete = requete.Where(c => c.Appartement!.BailleurId == utilisateurId);
        }

        var totalElements = await requete.CountAsync();

        var candidatures = await requete
            .OrderByDescending(c => c.DateSoumission)
            .Skip((page - 1) * taille)
            .Take(taille)
            .Include(c => c.Locataire)
            .Include(c => c.Appartement)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .ToListAsync();

        var locationsParCandidature = await _context.Locations
            .Where(l => l.CandidatureId != null && candidatures.Select(c => c.Id).Contains(l.CandidatureId!.Value))
            .Select(l => new { l.CandidatureId, l.Id, FinancementId = l.FinancementLocation != null ? l.FinancementLocation.Id : (int?)null })
            .ToListAsync();

        var donnees = candidatures.Select(c => VersDto(c, locationsParCandidature.FirstOrDefault(l => l.CandidatureId == c.Id)?.Id,
            locationsParCandidature.FirstOrDefault(l => l.CandidatureId == c.Id)?.FinancementId)).ToList();

        return new PagedResultDto<CandidatureResponseDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = totalElements,
            TotalPages = (int)Math.Ceiling(totalElements / (double)taille)
        };
    }

    public async Task<CandidatureResponseDto> RechercherAsync(int id, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Locataire)
            .Include(c => c.Appartement)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }

        VerifierAutorisation(candidature, utilisateurId, role);

        var location = await _context.Locations
            .Where(l => l.CandidatureId == id)
            .Select(l => new { l.Id, FinancementId = l.FinancementLocation != null ? l.FinancementLocation.Id : (int?)null })
            .FirstOrDefaultAsync();

        return VersDto(candidature, location?.Id, location?.FinancementId);
    }

    public async Task<CandidatureResponseDto> AccepterAsync(int id, int utilisateurId, RoleUtilisateur role, AccepterCandidatureDto dto)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (candidature.Statut != StatutCandidature.EnAttente)
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }

        var appartement = candidature.Appartement!;
        var montantTotal = appartement.LoyerMensuel!.Value * appartement.DureeAvanceExigee!.Value;

        // Transaction : si le financement échoue (ex. apport insuffisant), rien ne doit être persisté
        // (ni la Location, ni l'Appartement marqué indisponible, ni le statut de la candidature).
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            appartement.Disponible = false;
            var location = await _locationServices.CreerDepuisCandidatureAsync(candidature);

            int? financementId = null;
            if (candidature.ApportInitial < montantTotal)
            {
                var tauxFrais = dto.TauxFrais ?? 0.03m;
                var financement = await _financementServices.CreerPourLocationAsync(location, appartement, candidature.ApportInitial, tauxFrais);
                financementId = financement.Id;
            }

            candidature.Statut = StatutCandidature.Acceptee;
            candidature.DateTraitement = DateTime.UtcNow;

            // Les autres candidatures en attente pour ce même appartement deviennent caduques.
            var autresCandidatures = await _context.Candidatures
                .Where(c => c.AppartementId == appartement.Id && c.Statut == StatutCandidature.EnAttente && c.Id != id)
                .ToListAsync();
            foreach (var autre in autresCandidatures)
            {
                autre.Statut = StatutCandidature.Refusee;
                autre.DateTraitement = DateTime.UtcNow;
                autre.MotifRefus = "Appartement attribué à un autre candidat";
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return VersDto(candidature, location.Id, financementId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<CandidatureResponseDto> RefuserAsync(int id, int utilisateurId, RoleUtilisateur role, RefuserCandidatureDto dto)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .Include(c => c.Locataire)
            .Include(c => c.Garant)
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (candidature.Statut != StatutCandidature.EnAttente)
        {
            throw new ConflictException("Cette candidature a déjà été traitée");
        }

        candidature.Statut = StatutCandidature.Refusee;
        candidature.DateTraitement = DateTime.UtcNow;
        candidature.MotifRefus = dto.MotifRefus;

        await _context.SaveChangesAsync();

        return VersDto(candidature, null, null);
    }

    private static void VerifierAutorisation(Candidature candidature, int utilisateurId, RoleUtilisateur role)
    {
        var autorise = role == RoleUtilisateur.Admin
            || (role == RoleUtilisateur.Locataire && candidature.LocataireId == utilisateurId)
            || (role == RoleUtilisateur.Bailleur && candidature.Appartement!.BailleurId == utilisateurId);
        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
    }

    private static CandidatureResponseDto VersDto(Candidature c, int? locationId, int? financementId) => new()
    {
        Id = c.Id,
        LocataireId = c.LocataireId,
        LocataireNom = c.Locataire?.Nom ?? "",
        AppartementId = c.AppartementId,
        AppartementAdresse = c.Appartement?.Adresse ?? "",
        ApportInitial = c.ApportInitial,
        SituationProfessionnelle = c.SituationProfessionnelle.ToString(),
        Statut = c.Statut.ToString(),
        DateSoumission = c.DateSoumission,
        DateTraitement = c.DateTraitement,
        MotifRefus = c.MotifRefus,
        Garant = c.Garant is null ? null : new GarantResponseDto
        {
            Id = c.Garant.Id,
            Nom = c.Garant.Nom,
            Telephone = c.Garant.Telephone,
            Email = c.Garant.Email,
            SituationProfessionnelle = c.Garant.SituationProfessionnelle.ToString()
        },
        Documents = c.Documents.Select(d => new DocumentResponseDto
        {
            Id = d.Id,
            CandidatureId = d.CandidatureId,
            Proprietaire = d.Proprietaire.ToString(),
            TypeDocument = d.TypeDocument.ToString(),
            NomFichierOriginal = d.NomFichierOriginal,
            TailleOctets = d.TailleOctets,
            DateUpload = d.DateUpload
        }).ToList(),
        LocationId = locationId,
        FinancementLocationId = financementId
    };
}
