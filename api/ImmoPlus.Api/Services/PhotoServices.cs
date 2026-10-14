namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class PhotoServices
{
    private const long TailleMaxOctets = 5 * 1024 * 1024; // 5 Mo
    private const long TailleMax360Octets = 15 * 1024 * 1024; // 15 Mo : les panoramas sont bien plus lourds
    private const int NombreMaxParAppartement = 10;
    private const int NombreMax360ParAppartement = 5;
    private static readonly string[] ExtensionsAutorisees = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environnement;

    public PhotoServices(AppDbContext context, IWebHostEnvironment environnement)
    {
        _context = context;
        _environnement = environnement;
    }

    private string DossierRacine => Path.Combine(_environnement.ContentRootPath, "Storage", "photos");
    /// <summary>Cache des miniatures (480 px) : régénérables à tout moment depuis les photos.</summary>
    private string DossierMiniatures => Path.Combine(_environnement.ContentRootPath, "Storage", "miniatures");

    /// <summary>Miniature d'une photo : même dossier et même nom que la photo, en .jpg, sous Storage/miniatures.</summary>
    private string CheminMiniature(PhotoAppartement photo) => Path.Combine(DossierMiniatures, Path.ChangeExtension(photo.CheminStockage, ".jpg"));

    public async Task<PhotoAppartementDto> AjouterAsync(int appartementId, IFormFile fichier, bool est360, string? libelle,
        int utilisateurId, RoleUtilisateur role)
    {
        await VerifierModifiableAsync(appartementId, utilisateurId, role);

        if (fichier is null || fichier.Length == 0)
        {
            throw new InvalidOperationException("Le fichier est vide");
        }
        if (fichier.Length > (est360 ? TailleMax360Octets : TailleMaxOctets))
        {
            throw new InvalidOperationException(est360 ? "Un panorama 360° ne doit pas dépasser 15 Mo" : "Une photo ne doit pas dépasser 5 Mo");
        }
        var extension = Path.GetExtension(fichier.FileName).ToLowerInvariant();
        // Photos servies publiquement : le contenu doit vraiment être une image, pas seulement porter la bonne extension.
        if (!ExtensionsAutorisees.Contains(extension) || !await EtatDesLieuxServices.EstImageAsync(fichier))
        {
            throw new InvalidOperationException("Format non autorisé. Formats acceptés : JPG, PNG, WEBP");
        }

        // Photos classiques et panoramas 360° ont chacun leur galerie, leur limite et leur ordre.
        var photosExistantes = await _context.PhotosAppartement
            .Where(p => p.AppartementId == appartementId && p.Est360 == est360)
            .ToListAsync();
        var maximum = est360 ? NombreMax360ParAppartement : NombreMaxParAppartement;
        if (photosExistantes.Count >= maximum)
        {
            throw new ConflictException(est360 ? $"{maximum} panoramas 360° maximum par logement" : $"{maximum} photos maximum par logement");
        }

        // Photo allégée (JPEG 1920 px, panorama 6000 px, sans EXIF) et sa miniature : l'original envoyé n'est pas conservé.
        var nomStocke = $"{Guid.NewGuid()}.jpg";
        var cheminStockage = Path.Combine(appartementId.ToString(), nomStocke);
        await using (var source = fichier.OpenReadStream())
        using (var image = await TraitementImages.ChargerAsync(source, est360 ? TraitementImages.TailleMaxPanorama : TraitementImages.TailleMaxPhoto))
        {
            await TraitementImages.EnregistrerJpegAsync(image, Path.Combine(DossierRacine, cheminStockage));
            await TraitementImages.EnregistrerMiniatureAsync(image, Path.Combine(DossierMiniatures, cheminStockage));
        }

        var photo = new PhotoAppartement
        {
            AppartementId = appartementId,
            NomFichierOriginal = fichier.FileName,
            CheminStockage = cheminStockage,
            Ordre = photosExistantes.Count == 0 ? 0 : photosExistantes.Max(p => p.Ordre) + 1,
            Est360 = est360,
            Libelle = string.IsNullOrWhiteSpace(libelle) ? null : libelle.Trim()[..Math.Min(libelle.Trim().Length, 50)],
            DateUpload = DateTime.UtcNow
        };
        await _context.PhotosAppartement.AddAsync(photo);
        await _context.SaveChangesAsync();

        return VersDto(photo);
    }

    public async Task SupprimerAsync(int photoId, int utilisateurId, RoleUtilisateur role)
    {
        var photo = await _context.PhotosAppartement.FindAsync(photoId);
        if (photo is null)
        {
            throw new NotFoundException("Cette photo n'existe pas");
        }
        await VerifierModifiableAsync(photo.AppartementId, utilisateurId, role);

        _context.PhotosAppartement.Remove(photo);
        await _context.SaveChangesAsync();

        foreach (var chemin in new[] { Path.Combine(DossierRacine, photo.CheminStockage), CheminMiniature(photo) })
        {
            if (File.Exists(chemin))
            {
                File.Delete(chemin);
            }
        }
    }

    /// <summary>
    /// Photo publique tant que l'annonce l'est : logement supprimé ou hôte désactivé → 404, sauf pour le propriétaire et l'admin (jeton).
    /// </summary>
    /// <param name="miniature">Miniature 480 px (générée et mise en cache à la première demande pour les photos plus anciennes).</param>
    /// <returns>Flux et nom du fichier servi (son extension donne le type MIME).</returns>
    public async Task<(Stream Flux, string NomFichier)> OuvrirAsync(int photoId, int? utilisateurId, RoleUtilisateur? role, bool miniature = false)
    {
        var photo = await _context.PhotosAppartement.AsNoTracking()
            .Include(p => p.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        var autorise = photo?.Appartement is { } appartement
            && (!AppartementServices.PhotosProtegees(appartement)
                || role == RoleUtilisateur.Admin
                || (utilisateurId is not null && appartement.BailleurId == utilisateurId));
        var chemin = photo is null || !autorise ? null : Path.Combine(DossierRacine, photo.CheminStockage);
        if (chemin is null || !File.Exists(chemin))
        {
            throw new NotFoundException("Cette photo n'existe pas");
        }
        if (miniature)
        {
            var cheminMiniature = CheminMiniature(photo!);
            if (!File.Exists(cheminMiniature))
            {
                try
                {
                    await using var source = new FileStream(chemin, FileMode.Open, FileAccess.Read);
                    using var image = await TraitementImages.ChargerAsync(source, TraitementImages.TailleMiniature);
                    await TraitementImages.EnregistrerJpegAsync(image, cheminMiniature, TraitementImages.QualiteMiniature);
                }
                catch (InvalidOperationException)
                {
                    // Photo ancienne illisible par la bibliothèque : on sert la photo complète plutôt qu'une erreur.
                    return (new FileStream(chemin, FileMode.Open, FileAccess.Read), Path.GetFileName(chemin));
                }
            }
            return (new FileStream(cheminMiniature, FileMode.Open, FileAccess.Read), Path.GetFileName(cheminMiniature));
        }
        // Type MIME d'après le fichier stocké (les nouvelles photos sont réencodées en JPEG quel que soit le nom d'origine).
        return (new FileStream(chemin, FileMode.Open, FileAccess.Read), Path.GetFileName(chemin));
    }

    public static PhotoAppartementDto VersDto(PhotoAppartement p) => new()
    {
        Id = p.Id,
        Url = $"appartement/photo/{p.Id}",
        UrlMiniature = $"appartement/photo/{p.Id}?taille=miniature",
        Ordre = p.Ordre,
        Est360 = p.Est360,
        Libelle = p.Libelle
    };

    // Mêmes règles que la modification de l'annonce : propriétaire ou admin, et pas pendant une location.
    private async Task VerifierModifiableAsync(int appartementId, int utilisateurId, RoleUtilisateur role)
    {
        var appartement = await _context.Appartements.FindAsync(appartementId);
        if (appartement is null)
        {
            throw new NotFoundException("Cet appartement n'existe pas");
        }
        if (role != RoleUtilisateur.Admin && appartement.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous ne pouvez modifier que vos propres logements");
        }
        if (await _context.Locations.AnyAsync(l => l.AppartementId == appartementId && l.Statut == StatutLocation.EnCours))
        {
            throw new ConflictException("Ce logement est en cours de location : il ne peut pas être modifié tant que la location dure");
        }
    }
}
