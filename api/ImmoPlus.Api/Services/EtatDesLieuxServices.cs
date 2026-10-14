namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// État des lieux photo d'un séjour confirmé : le voyageur et l'hôte photographient le logement à l'arrivée et au départ,
/// chacun voit les photos de l'autre (preuve en cas de litige sur la caution). Fichiers dans Storage/etats-des-lieux/{reservationId}.
/// </summary>
public class EtatDesLieuxServices
{
    private const long TailleMaxOctets = 10 * 1024 * 1024; // 10 Mo : photos prises directement avec le téléphone
    private const int NombreMaxParMomentEtAuteur = 20;
    private const int LongueurMaxCommentaire = 500;
    private static readonly TimeSpan DelaiSuppression = TimeSpan.FromHours(24);
    private static readonly string[] ExtensionsAutorisees = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environnement;

    public EtatDesLieuxServices(AppDbContext context, IWebHostEnvironment environnement)
    {
        _context = context;
        _environnement = environnement;
    }

    public string DossierRacine => Path.Combine(_environnement.ContentRootPath, "Storage", "etats-des-lieux");

    public async Task<PhotoEtatDesLieuxDto> AjouterAsync(int reservationId, IFormFile? fichier, string? moment, string? commentaire, int utilisateurId)
    {
        var reservation = await _context.Reservations.Include(r => r.Appartement)
            .FirstOrDefaultAsync(r => r.Id == reservationId)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (reservation.LocataireId != utilisateurId && reservation.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Seuls le voyageur et l'hôte du séjour peuvent ajouter des photos d'état des lieux");
        }
        if (reservation.Statut is not (StatutReservation.Confirmee or StatutReservation.Terminee))
        {
            throw new ConflictException("L'état des lieux n'est possible que pour un séjour confirmé");
        }
        if (string.IsNullOrWhiteSpace(moment) || int.TryParse(moment, out _)
            || !Enum.TryParse<MomentEtatDesLieux>(moment.Trim(), true, out var momentLu) || !Enum.IsDefined(momentLu))
        {
            throw new InvalidOperationException("Moment invalide. Utilisez Arrivee ou Depart");
        }

        // Arrivée : dès la veille. Départ : de la veille du départ jusqu'à 3 jours après.
        var aujourdhui = DateTime.UtcNow.Date;
        var arrivee = reservation.DateArrivee.Date;
        var depart = reservation.DateDepart.Date;
        if (momentLu == MomentEtatDesLieux.Arrivee && aujourdhui < arrivee.AddDays(-1))
        {
            throw new ConflictException($"L'état des lieux d'arrivée est possible à partir du {arrivee.AddDays(-1):dd/MM/yyyy}");
        }
        if (momentLu == MomentEtatDesLieux.Depart && (aujourdhui < depart.AddDays(-1) || aujourdhui > depart.AddDays(3)))
        {
            throw new ConflictException($"L'état des lieux de départ est possible du {depart.AddDays(-1):dd/MM/yyyy} au {depart.AddDays(3):dd/MM/yyyy}");
        }

        if (fichier is null || fichier.Length == 0)
        {
            throw new InvalidOperationException("Le fichier est vide");
        }
        if (fichier.Length > TailleMaxOctets)
        {
            throw new InvalidOperationException("Une photo ne doit pas dépasser 10 Mo");
        }
        var extension = Path.GetExtension(fichier.FileName).ToLowerInvariant();
        if (!ExtensionsAutorisees.Contains(extension) || !await EstImageAsync(fichier))
        {
            throw new InvalidOperationException("Format non autorisé. Formats acceptés : JPG, PNG, WEBP");
        }
        var texte = string.IsNullOrWhiteSpace(commentaire) ? null : commentaire.Trim();
        if (texte?.Length > LongueurMaxCommentaire)
        {
            throw new InvalidOperationException($"Le commentaire ne doit pas dépasser {LongueurMaxCommentaire} caractères");
        }

        var nombre = await _context.PhotosEtatDesLieux.CountAsync(p =>
            p.ReservationId == reservationId && p.Moment == momentLu && p.AuteurId == utilisateurId);
        if (nombre >= NombreMaxParMomentEtAuteur)
        {
            throw new ConflictException($"{NombreMaxParMomentEtAuteur} photos maximum par état des lieux");
        }

        // Photo allégée (JPEG 1600 px, sans EXIF ni position GPS) : l'original envoyé n'est pas conservé.
        var nomStocke = $"{Guid.NewGuid()}.jpg";
        await using (var source = fichier.OpenReadStream())
        using (var image = await TraitementImages.ChargerAsync(source, TraitementImages.TailleMaxEtatDesLieux))
        {
            await TraitementImages.EnregistrerJpegAsync(image, Path.Combine(DossierRacine, reservationId.ToString(), nomStocke));
        }

        var photo = new PhotoEtatDesLieux
        {
            ReservationId = reservationId,
            Moment = momentLu,
            AuteurId = utilisateurId,
            Chemin = Path.Combine(reservationId.ToString(), nomStocke),
            Commentaire = texte,
            DateCreation = DateTime.UtcNow
        };
        await _context.PhotosEtatDesLieux.AddAsync(photo);
        await _context.SaveChangesAsync();

        photo.Auteur = await _context.Utilisateurs.FindAsync(utilisateurId);
        return VersDto(photo);
    }

    public async Task<List<PhotoEtatDesLieuxDto>> ListerAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        await VerifierAccesAsync(reservationId, utilisateurId, role);
        var photos = await _context.PhotosEtatDesLieux.AsNoTracking()
            .Include(p => p.Auteur)
            .Where(p => p.ReservationId == reservationId)
            .OrderBy(p => p.Moment).ThenBy(p => p.DateCreation).ThenBy(p => p.Id)
            .ToListAsync();
        return photos.Select(VersDto).ToList();
    }

    public async Task<(Stream Flux, string NomFichier)> OuvrirAsync(int photoId, int utilisateurId, RoleUtilisateur role)
    {
        var photo = await _context.PhotosEtatDesLieux.AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId)
            ?? throw new NotFoundException("Cette photo n'existe pas");
        await VerifierAccesAsync(photo.ReservationId, utilisateurId, role);
        var chemin = Path.Combine(DossierRacine, photo.Chemin);
        if (!File.Exists(chemin))
        {
            throw new NotFoundException("Cette photo n'existe pas");
        }
        return (new FileStream(chemin, FileMode.Open, FileAccess.Read), Path.GetFileName(photo.Chemin));
    }

    /// <summary>L'auteur peut retirer sa photo dans les 24 h (erreur de prise de vue) ; ensuite elle fait foi.</summary>
    public async Task SupprimerAsync(int photoId, int utilisateurId)
    {
        var photo = await _context.PhotosEtatDesLieux.FirstOrDefaultAsync(p => p.Id == photoId)
            ?? throw new NotFoundException("Cette photo n'existe pas");
        if (photo.AuteurId != utilisateurId)
        {
            throw new ConflictException("Seul l'auteur de la photo peut la supprimer");
        }
        if (DateTime.UtcNow - photo.DateCreation > DelaiSuppression)
        {
            throw new ConflictException("Une photo d'état des lieux ne peut plus être supprimée 24 h après son ajout");
        }
        _context.PhotosEtatDesLieux.Remove(photo);
        await _context.SaveChangesAsync();

        var chemin = Path.Combine(DossierRacine, photo.Chemin);
        if (File.Exists(chemin))
        {
            File.Delete(chemin);
        }
    }

    private async Task VerifierAccesAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations.AsNoTracking().Include(r => r.Appartement)
            .FirstOrDefaultAsync(r => r.Id == reservationId)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        if (role != RoleUtilisateur.Admin && reservation.LocataireId != utilisateurId && reservation.Appartement!.BailleurId != utilisateurId)
        {
            throw new ConflictException("Vous n'avez pas accès à cette réservation");
        }
    }

    /// <summary>Vérifie la signature du fichier (JPEG, PNG, WEBP) : l'extension seule ne suffit pas pour un fichier resservi ensuite.</summary>
    public static async Task<bool> EstImageAsync(IFormFile fichier)
    {
        var entete = new byte[12];
        await using var flux = fichier.OpenReadStream();
        var lus = await flux.ReadAtLeastAsync(entete, entete.Length, throwOnEndOfStream: false);
        if (lus < 12)
        {
            return false;
        }
        var jpeg = entete[0] == 0xFF && entete[1] == 0xD8 && entete[2] == 0xFF;
        var png = entete.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var webp = entete.AsSpan(0, 4).SequenceEqual("RIFF"u8) && entete.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        return jpeg || png || webp;
    }

    /// <summary>Signature d'un PDF (« %PDF- »).</summary>
    public static async Task<bool> EstPdfAsync(IFormFile fichier)
    {
        var entete = new byte[5];
        await using var flux = fichier.OpenReadStream();
        var lus = await flux.ReadAtLeastAsync(entete, entete.Length, throwOnEndOfStream: false);
        return lus == 5 && entete.AsSpan().SequenceEqual("%PDF-"u8);
    }

    private static PhotoEtatDesLieuxDto VersDto(PhotoEtatDesLieux p) => new()
    {
        Id = p.Id,
        Moment = p.Moment.ToString(),
        AuteurId = p.AuteurId,
        AuteurNom = p.Auteur?.Nom ?? "",
        Url = $"etat-des-lieux/photo/{p.Id}",
        Commentaire = p.Commentaire,
        DateCreation = p.DateCreation
    };
}
