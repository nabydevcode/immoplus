namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class DocumentServices
{
    private const long TailleMaxOctets = 10 * 1024 * 1024; // 10 Mo
    private static readonly string[] ExtensionsAutorisees = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environnement;
    private readonly NotificationServices _notificationServices;

    public DocumentServices(AppDbContext context, IWebHostEnvironment environnement, NotificationServices notificationServices)
    {
        _context = context;
        _environnement = environnement;
        _notificationServices = notificationServices;
    }

    private string DossierRacine => Path.Combine(_environnement.ContentRootPath, "Storage", "documents");

    public async Task<DocumentResponseDto> UploaderAsync(
        int candidatureId, int utilisateurId, RoleUtilisateur role,
        string proprietaire, string typeDocument, IFormFile fichier)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .FirstOrDefaultAsync(c => c.Id == candidatureId);
        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && candidature.LocataireId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette candidature");
        }
        if (!Enum.TryParse<ProprietaireDocument>(proprietaire, true, out var proprietaireEnum))
        {
            throw new InvalidOperationException("Proprietaire invalide. Utilisez Locataire ou Garant");
        }
        if (!Enum.TryParse<TypeDocument>(typeDocument, true, out var typeEnum))
        {
            throw new InvalidOperationException("TypeDocument invalide");
        }
        if (fichier is null || fichier.Length == 0)
        {
            throw new InvalidOperationException("Le fichier est vide");
        }
        if (fichier.Length > TailleMaxOctets)
        {
            throw new InvalidOperationException("Le fichier dépasse la taille maximale autorisée (10 Mo)");
        }

        var extension = Path.GetExtension(fichier.FileName).ToLowerInvariant();
        var contenuValide = extension == ".pdf" ? await EtatDesLieuxServices.EstPdfAsync(fichier) : await EtatDesLieuxServices.EstImageAsync(fichier);
        if (!ExtensionsAutorisees.Contains(extension) || !contenuValide)
        {
            throw new InvalidOperationException("Type de fichier non autorisé. Formats acceptés : PDF, JPG, PNG");
        }

        var dossierCandidature = Path.Combine(DossierRacine, candidatureId.ToString());
        Directory.CreateDirectory(dossierCandidature);

        var nomStocke = $"{Guid.NewGuid()}{extension}";
        var cheminComplet = Path.Combine(dossierCandidature, nomStocke);

        await using (var flux = new FileStream(cheminComplet, FileMode.Create))
        {
            await fichier.CopyToAsync(flux);
        }

        var document = new Document
        {
            CandidatureId = candidatureId,
            Proprietaire = proprietaireEnum,
            TypeDocument = typeEnum,
            NomFichierOriginal = fichier.FileName,
            CheminStockage = Path.Combine(candidatureId.ToString(), nomStocke),
            TailleOctets = fichier.Length,
            DateUpload = DateTime.UtcNow
        };

        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();

        await PrevenirSiDossierCompleteAsync(candidatureId);
        return VersDto(document);
    }

    public async Task<List<DocumentResponseDto>> ListerParCandidatureAsync(int candidatureId, int utilisateurId, RoleUtilisateur role)
    {
        var candidature = await _context.Candidatures
            .Include(c => c.Appartement)
            .FirstOrDefaultAsync(c => c.Id == candidatureId);
        if (candidature is null)
        {
            throw new NotFoundException("Cette candidature n'existe pas");
        }
        VerifierAutorisationLecture(candidature, utilisateurId, role);

        return await _context.Documents
            .AsNoTracking()
            .Where(d => d.CandidatureId == candidatureId)
            .Select(d => new DocumentResponseDto
            {
                Id = d.Id,
                CandidatureId = d.CandidatureId,
                Proprietaire = d.Proprietaire.ToString(),
                TypeDocument = d.TypeDocument.ToString(),
                NomFichierOriginal = d.NomFichierOriginal,
                TailleOctets = d.TailleOctets,
                DateUpload = d.DateUpload
            })
            .ToListAsync();
    }

    public async Task<(Stream Flux, string NomFichier)> TelechargerAsync(int documentId, int utilisateurId, RoleUtilisateur role)
    {
        var document = await _context.Documents
            .Include(d => d.Candidature).ThenInclude(c => c!.Appartement)
            .FirstOrDefaultAsync(d => d.Id == documentId);
        if (document is null)
        {
            throw new NotFoundException("Ce document n'existe pas");
        }
        VerifierAutorisationLecture(document.Candidature!, utilisateurId, role);

        var cheminComplet = Path.Combine(DossierRacine, document.CheminStockage);
        if (!File.Exists(cheminComplet))
        {
            throw new NotFoundException("Le fichier associé est introuvable sur le serveur");
        }

        Stream flux = new FileStream(cheminComplet, FileMode.Open, FileAccess.Read);
        return (flux, document.NomFichierOriginal);
    }

    /// <summary>Si ce dépôt complète une demande de pièces du bailleur, le prévient (une seule fois par demande).</summary>
    private async Task PrevenirSiDossierCompleteAsync(int candidatureId)
    {
        var demandes = await _context.DemandesComplement
            .Where(d => d.CandidatureId == candidatureId && !d.CompletNotifie)
            .ToListAsync();
        if (demandes.Count == 0)
        {
            return;
        }

        var candidature = await _context.Candidatures
            .Include(c => c.Documents)
            .Include(c => c.Locataire)
            .Include(c => c.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstAsync(c => c.Id == candidatureId);
        var completes = demandes.Where(d => d.EstComplete(candidature.Documents)).ToList();
        if (completes.Count == 0)
        {
            return;
        }

        completes.ForEach(d => d.CompletNotifie = true);
        await _context.SaveChangesAsync();
        await _notificationServices.DossierCompleteAsync(candidature.Appartement!.Bailleur!, candidature, candidature.Appartement, candidature.Locataire!);
    }

    private static void VerifierAutorisationLecture(Candidature candidature, int utilisateurId, RoleUtilisateur role)
    {
        var autorise = role == RoleUtilisateur.Admin
            || candidature.LocataireId == utilisateurId
            || candidature.Appartement!.BailleurId == utilisateurId;
        if (!autorise)
        {
            throw new ConflictException("Vous n'avez pas accès à ce document");
        }
    }

    private static DocumentResponseDto VersDto(Document d) => new()
    {
        Id = d.Id,
        CandidatureId = d.CandidatureId,
        Proprietaire = d.Proprietaire.ToString(),
        TypeDocument = d.TypeDocument.ToString(),
        NomFichierOriginal = d.NomFichierOriginal,
        TailleOctets = d.TailleOctets,
        DateUpload = d.DateUpload
    };
}
