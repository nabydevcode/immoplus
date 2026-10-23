namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Vérification d'identité : le bailleur ou le locataire envoie sa pièce (recto obligatoire, verso facultatif), un admin la contrôle.
/// Les fichiers restent privés (clés « identites/{userId}/… »), servis uniquement aux admins. Images réencodées sans EXIF.
/// </summary>
public class IdentiteServices
{
    private const long TailleMaxOctets = 10 * 1024 * 1024; // 10 Mo
    private const int TailleMaxImage = 2400;
    private static readonly string[] ExtensionsImage = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly AppDbContext _context;
    private readonly IStockageFichiers _stockage;
    private readonly NotificationServices _notificationServices;
    private readonly JournalServices _journal;
    private readonly ILogger<IdentiteServices> _logger;

    private readonly ICacheHotes _cacheHotes;

    public IdentiteServices(AppDbContext context, IStockageFichiers stockage, NotificationServices notificationServices, JournalServices journal,
        ILogger<IdentiteServices> logger, ICacheHotes cacheHotes)
    {
        _cacheHotes = cacheHotes;
        _context = context;
        _stockage = stockage;
        _notificationServices = notificationServices;
        _journal = journal;
        _logger = logger;
    }

    public async Task<PieceIdentiteDto> EnvoyerAsync(int utilisateurId, string? type, IFormFile? recto, IFormFile? verso)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(utilisateurId) ?? throw new NotFoundException("Cet utilisateur n'existe pas");
        if (utilisateur.Role == RoleUtilisateur.Admin)
        {
            throw new ConflictException("La vérification d'identité concerne les bailleurs et les locataires");
        }
        if (utilisateur.IdentiteVerifiee)
        {
            throw new ConflictException("Votre identité est déjà vérifiée");
        }
        if (string.IsNullOrWhiteSpace(type) || int.TryParse(type, out _)
            || !Enum.TryParse<TypePieceIdentite>(type.Trim(), true, out var typePiece) || !Enum.IsDefined(typePiece))
        {
            throw new InvalidOperationException("Type de pièce invalide. Utilisez CNI, Passeport, PermisConduire ou CarteConsulaire");
        }
        if (recto is null || recto.Length == 0)
        {
            throw new InvalidOperationException("Le recto de la pièce d'identité est obligatoire");
        }
        if (await _context.PiecesIdentite.AnyAsync(p => p.UtilisateurId == utilisateurId && p.Statut == StatutPieceIdentite.EnAttente))
        {
            throw new ConflictException("Une demande de vérification est déjà en attente : attendez la décision de l'administrateur");
        }
        await VerifierFichierAsync(recto, "Le recto");
        if (verso is not null && verso.Length > 0)
        {
            await VerifierFichierAsync(verso, "Le verso");
        }

        var cheminRecto = await EnregistrerAsync(recto, utilisateurId);
        var cheminVerso = verso is { Length: > 0 } ? await EnregistrerAsync(verso, utilisateurId) : null;

        var piece = new PieceIdentite
        {
            UtilisateurId = utilisateurId,
            Type = typePiece,
            Chemin = cheminRecto,
            CheminVerso = cheminVerso,
            Statut = StatutPieceIdentite.EnAttente,
            DateEnvoi = DateTime.UtcNow
        };
        _context.PiecesIdentite.Add(piece);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Index unique filtré (une seule demande en attente) : deux envois simultanés.
            await SupprimerFichiersAsync(cheminRecto, cheminVerso);
            throw new ConflictException("Une demande de vérification est déjà en attente : attendez la décision de l'administrateur");
        }
        await _journal.EnregistrerAsync("identite.envoyer", "Utilisateur", utilisateurId,
            $"Demande de vérification d'identité n° {piece.Id:D6} envoyée ({typePiece}{(cheminVerso is null ? ", recto seul" : ", recto et verso")})");
        return VersDto(piece, null, null);
    }

    public async Task<StatutIdentiteDto> StatutAsync(int utilisateurId)
    {
        var utilisateur = await _context.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == utilisateurId)
            ?? throw new NotFoundException("Cet utilisateur n'existe pas");
        var derniere = await _context.PiecesIdentite.AsNoTracking()
            .Where(p => p.UtilisateurId == utilisateurId)
            .OrderByDescending(p => p.DateEnvoi).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        return new StatutIdentiteDto
        {
            IdentiteVerifiee = utilisateur.IdentiteVerifiee,
            DateVerificationIdentite = utilisateur.DateVerificationIdentite,
            Demande = derniere is null ? null : VersDto(derniere, null, null)
        };
    }

    /// <summary>Admin : demandes paginées, en attente d'abord, puis les plus récentes (envoi puis n°).</summary>
    public async Task<PagedResultDto<PieceIdentiteDto>> ListerAsync(string? statut, Pagination pagination)
    {
        var requete = _context.PiecesIdentite.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(statut))
        {
            if (int.TryParse(statut, out _) || !Enum.TryParse<StatutPieceIdentite>(statut.Trim(), true, out var s) || !Enum.IsDefined(s))
            {
                throw new InvalidOperationException("Statut invalide. Utilisez EnAttente, Validee ou Refusee");
            }
            requete = requete.Where(p => p.Statut == s);
        }
        var (pieces, total) = await requete
            .OrderBy(p => p.Statut == StatutPieceIdentite.EnAttente ? 0 : 1)
            .ThenByDescending(p => p.DateEnvoi).ThenByDescending(p => p.Id)
            .Include(p => p.Utilisateur).Include(p => p.DecidePar)
            .PageAsync(pagination);
        return PagedResultDto<PieceIdentiteDto>.Creer(pieces.Select(p => VersDto(p, p.Utilisateur, p.DecidePar)).ToList(), pagination, total);
    }

    /// <summary>Fichier recto / verso d'une demande (admin uniquement : vérifié par la route).</summary>
    public async Task<(Stream Flux, string ContentType, string NomFichier)> OuvrirFichierAsync(int id, string face)
    {
        var piece = await _context.PiecesIdentite.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException("Cette demande n'existe pas");
        var chemin = face.ToLowerInvariant() switch
        {
            "recto" => piece.Chemin,
            "verso" => piece.CheminVerso ?? throw new NotFoundException("Aucun verso n'a été envoyé pour cette demande"),
            _ => throw new InvalidOperationException("Indiquez « recto » ou « verso »")
        };
        // Clé contrôlée (ni « .. » ni chemin absolu) : impossible de sortir de « identites/ ».
        var flux = await _stockage.OuvrirAsync(ClesStockage.Cle(ClesStockage.Identites, chemin))
            ?? throw new NotFoundException("Le fichier est introuvable sur le serveur");
        var pdf = chemin.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        return (flux, pdf ? "application/pdf" : "image/jpeg",
            $"identite-{id:D6}-{face.ToLowerInvariant()}{(pdf ? ".pdf" : ".jpg")}");
    }

    public async Task<PieceIdentiteDto> ValiderAsync(int id, int adminId)
    {
        var piece = await ChargerEnAttenteAsync(id);
        var maintenant = DateTime.UtcNow;
        piece.Statut = StatutPieceIdentite.Validee;
        piece.DateDecision = maintenant;
        piece.DecideParId = adminId;
        piece.MotifRefus = null;
        piece.Utilisateur!.IdentiteVerifiee = true;
        piece.Utilisateur.DateVerificationIdentite = maintenant;
        await _context.SaveChangesAsync();
        // Badge « identité vérifiée » visible tout de suite sur ses annonces (sinon après 2 min de cache).
        await _cacheHotes.InvaliderAsync(piece.UtilisateurId);

        await _notificationServices.IdentiteValideeAsync(piece.Utilisateur, piece);
        await _journal.EnregistrerAsync("identite.valider", "Utilisateur", piece.UtilisateurId,
            $"Identité de {piece.Utilisateur.Nom} vérifiée (demande n° {piece.Id:D6}, {piece.Type})");
        return VersDto(piece, piece.Utilisateur, await _context.Utilisateurs.FindAsync(adminId));
    }

    public async Task<PieceIdentiteDto> RefuserAsync(int id, string? motif, int adminId)
    {
        motif = motif?.Trim();
        if (string.IsNullOrEmpty(motif))
        {
            throw new InvalidOperationException("Indiquez le motif du refus");
        }
        if (motif.Length > 500)
        {
            throw new InvalidOperationException("Le motif ne doit pas dépasser 500 caractères");
        }
        var piece = await ChargerEnAttenteAsync(id);
        piece.Statut = StatutPieceIdentite.Refusee;
        piece.DateDecision = DateTime.UtcNow;
        piece.DecideParId = adminId;
        piece.MotifRefus = motif;
        await _context.SaveChangesAsync();

        await _notificationServices.IdentiteRefuseeAsync(piece.Utilisateur!, piece);
        await _journal.EnregistrerAsync("identite.refuser", "Utilisateur", piece.UtilisateurId,
            $"Vérification d'identité de {piece.Utilisateur!.Nom} refusée (demande n° {piece.Id:D6}) : {motif}");
        return VersDto(piece, piece.Utilisateur, await _context.Utilisateurs.FindAsync(adminId));
    }

    public Task<int> NombreEnAttenteAsync() => _context.PiecesIdentite.CountAsync(p => p.Statut == StatutPieceIdentite.EnAttente);

    private async Task<PieceIdentite> ChargerEnAttenteAsync(int id)
    {
        var piece = await _context.PiecesIdentite.Include(p => p.Utilisateur).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException("Cette demande n'existe pas");
        if (piece.Statut != StatutPieceIdentite.EnAttente)
        {
            throw new ConflictException("Cette demande a déjà été traitée");
        }
        return piece;
    }

    private static async Task VerifierFichierAsync(IFormFile fichier, string libelle)
    {
        if (fichier.Length > TailleMaxOctets)
        {
            throw new InvalidOperationException($"{libelle} dépasse la taille maximale autorisée (10 Mo)");
        }
        var extension = Path.GetExtension(fichier.FileName).ToLowerInvariant();
        var valide = extension == ".pdf"
            ? await EtatDesLieuxServices.EstPdfAsync(fichier)
            : ExtensionsImage.Contains(extension) && await EtatDesLieuxServices.EstImageAsync(fichier);
        if (!valide)
        {
            throw new InvalidOperationException($"{libelle} : format non autorisé. Formats acceptés : JPG, PNG, WEBP ou PDF");
        }
    }

    /// <summary>Image réencodée en JPEG (EXIF et position GPS retirés), PDF copié tel quel ; chemin relatif à « identites/ ».</summary>
    private async Task<string> EnregistrerAsync(IFormFile fichier, int utilisateurId)
    {
        var pdf = Path.GetExtension(fichier.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var chemin = $"{utilisateurId}/{Guid.NewGuid():N}{(pdf ? ".pdf" : ".jpg")}";
        var cle = ClesStockage.Cle(ClesStockage.Identites, chemin);
        await using var source = fichier.OpenReadStream();
        if (pdf)
        {
            await _stockage.EnregistrerAsync(cle, source, "application/pdf");
        }
        else
        {
            using var image = await TraitementImages.ChargerAsync(source, TailleMaxImage);
            await _stockage.EnregistrerAsync(cle, new MemoryStream(TraitementImages.Jpeg(image)), "image/jpeg");
        }
        return chemin;
    }

    private async Task SupprimerFichiersAsync(params string?[] chemins)
    {
        foreach (var chemin in chemins.Where(c => c is not null))
        {
            try
            {
                await _stockage.SupprimerAsync(ClesStockage.Cle(ClesStockage.Identites, chemin!));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pièce d'identité orpheline non supprimée ({Chemin})", chemin);
            }
        }
    }

    private static PieceIdentiteDto VersDto(PieceIdentite p, Utilisateur? utilisateur, Utilisateur? decidePar) => new()
    {
        Id = p.Id,
        UtilisateurId = p.UtilisateurId,
        UtilisateurNom = utilisateur?.Nom,
        UtilisateurEmail = utilisateur?.Email,
        UtilisateurRole = utilisateur?.Role.ToString(),
        Type = p.Type.ToString(),
        Statut = p.Statut.ToString(),
        MotifRefus = p.MotifRefus,
        DateEnvoi = p.DateEnvoi,
        DateDecision = p.DateDecision,
        DecideParNom = decidePar?.Nom,
        TypeRecto = p.Chemin.EndsWith(".pdf") ? "application/pdf" : "image/jpeg",
        AVerso = p.CheminVerso is not null,
        TypeVerso = p.CheminVerso is null ? null : p.CheminVerso.EndsWith(".pdf") ? "application/pdf" : "image/jpeg"
    };
}
