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
    private readonly IStockageFichiers _stockage;
    private readonly ILogger<PhotoServices> _logger;

    public PhotoServices(AppDbContext context, IStockageFichiers stockage, ILogger<PhotoServices> logger)
    {
        _context = context;
        _stockage = stockage;
        _logger = logger;
    }

    private static string ClePhoto(PhotoAppartement photo) => ClesStockage.Cle(ClesStockage.Photos, photo.CheminStockage);

    /// <summary>
    /// Miniature (480 px) d'une photo, régénérable à tout moment : même dossier et même nom que la photo, en .jpg, sous « miniatures/ ».
    /// </summary>
    private static string CleMiniature(PhotoAppartement photo) =>
        ClesStockage.Cle(ClesStockage.Miniatures, Path.ChangeExtension(photo.CheminStockage.Replace('\\', '/'), ".jpg"));

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
        var cheminStockage = $"{appartementId}/{nomStocke}";
        byte[] jpeg, miniature;
        await using (var source = fichier.OpenReadStream())
        using (var image = await TraitementImages.ChargerAsync(source, est360 ? TraitementImages.TailleMaxPanorama : TraitementImages.TailleMaxPhoto))
        {
            jpeg = TraitementImages.Jpeg(image);
            miniature = TraitementImages.MiniatureJpeg(image);
        }
        await _stockage.EnregistrerAsync(ClesStockage.Cle(ClesStockage.Photos, cheminStockage), new MemoryStream(jpeg), "image/jpeg");
        await _stockage.EnregistrerAsync(ClesStockage.Cle(ClesStockage.Miniatures, cheminStockage), new MemoryStream(miniature), "image/jpeg");

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

        foreach (var cle in new[] { ClePhoto(photo), CleMiniature(photo) })
        {
            try
            {
                await _stockage.SupprimerAsync(cle);
            }
            catch (Exception ex)
            {
                // La photo n'est plus référencée : un fichier orphelin ne gêne personne, l'action reste réussie.
                _logger.LogWarning(ex, "Fichier de la photo {Id} non supprimé ({Cle})", photo.Id, cle);
            }
        }
    }

    /// <summary>
    /// Photo publique tant que l'annonce l'est : logement supprimé ou hôte désactivé → 404, sauf pour le propriétaire et l'admin (jeton).
    /// </summary>
    /// <param name="miniature">Miniature 480 px (générée et mise en cache à la première demande pour les photos plus anciennes).</param>
    /// <returns>Flux et nom du fichier servi (son extension donne le type MIME).</returns>
    public async Task<(Stream Flux, string NomFichier)> OuvrirAsync(int photoId, int? utilisateurId, RoleUtilisateur? role, bool miniature = false) =>
        await OuvrirAsync((await AccesAsync(photoId, utilisateurId, role)).Photo, miniature);

    /// <summary>
    /// Photo accessible à l'appelant (404 sinon). Protegee : réservée au propriétaire et à l'admin (annonce supprimée ou hôte
    /// désactivé) — jamais mise en cache public ; sinon photo publique, cacheable par les navigateurs et les proxys.
    /// </summary>
    public async Task<(PhotoAppartement Photo, bool Protegee)> AccesAsync(int photoId, int? utilisateurId, RoleUtilisateur? role)
    {
        var photo = await _context.PhotosAppartement.AsNoTracking()
            .Include(p => p.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        var protegee = photo?.Appartement is { } logement && AppartementServices.PhotosProtegees(logement);
        var autorise = photo?.Appartement is { } appartement
            && (!protegee
                || role == RoleUtilisateur.Admin
                || (utilisateurId is not null && appartement.BailleurId == utilisateurId));
        if (photo is null || !autorise)
        {
            throw new NotFoundException("Cette photo n'existe pas");
        }
        return (photo, protegee);
    }

    /// <summary>Fichier d'une photo dont l'accès a été vérifié par <see cref="AccesAsync"/>.</summary>
    public async Task<(Stream Flux, string NomFichier)> OuvrirAsync(PhotoAppartement photo, bool miniature)
    {
        var clePhoto = ClePhoto(photo);
        if (miniature)
        {
            var cleMiniature = CleMiniature(photo);
            if (await _stockage.OuvrirAsync(cleMiniature) is { } fluxMiniature)
            {
                return (fluxMiniature, Path.GetFileName(cleMiniature));
            }
            await using (var source = await _stockage.OuvrirAsync(clePhoto))
            {
                if (source is null)
                {
                    throw new NotFoundException("Cette photo n'existe pas");
                }
                byte[] contenu;
                try
                {
                    using var image = await TraitementImages.ChargerAsync(source, TraitementImages.TailleMiniature);
                    contenu = TraitementImages.Jpeg(image, TraitementImages.QualiteMiniature);
                }
                catch (InvalidOperationException)
                {
                    // Photo ancienne illisible par la bibliothèque : on sert la photo complète plutôt qu'une erreur.
                    return (await _stockage.OuvrirAsync(clePhoto) ?? throw new NotFoundException("Cette photo n'existe pas"), Path.GetFileName(clePhoto));
                }
                // Mise en cache pour les demandes suivantes (et les autres instances de l'API).
                await _stockage.EnregistrerAsync(cleMiniature, new MemoryStream(contenu), "image/jpeg");
                return (new MemoryStream(contenu), Path.GetFileName(cleMiniature));
            }
        }
        // Type MIME d'après le fichier stocké (les nouvelles photos sont réencodées en JPEG quel que soit le nom d'origine).
        return (await _stockage.OuvrirAsync(clePhoto) ?? throw new NotFoundException("Cette photo n'existe pas"), Path.GetFileName(clePhoto));
    }

    /// <summary>Durée de cache navigateur / proxy d'une photo publique (une photo retirée disparaît des caches au plus tard après ce délai).</summary>
    public static readonly TimeSpan DureeCachePublic = TimeSpan.FromDays(1);

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
