namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class UtilisateurServices
{
    private readonly AppDbContext _context;
    private readonly TokenServices _tokenServices;

    public UtilisateurServices(AppDbContext context, TokenServices tokenServices)
    {
        _context = context;
        _tokenServices = tokenServices;
    }

    public async Task<UtilisateurResponseDto> InscrireAsync(InscriptionDto dto)
    {
        var emailExiste = await _context.Utilisateurs.AnyAsync(u => u.Email == dto.Email);
        if (emailExiste)
        {
            throw new ConflictException("Un utilisateur avec cet email existe déjà");
        }

        if (!Enum.TryParse<RoleUtilisateur>(dto.Role, true, out var role))
        {
            throw new InvalidOperationException("Rôle invalide. Utilisez Locataire, Bailleur ou Admin");
        }

        // Bootstrap : le tout premier compte du système, ou le tout premier Admin, est auto-validé
        // (sinon personne ne pourrait jamais valider qui que ce soit).
        var estPremierUtilisateur = !await _context.Utilisateurs.AnyAsync();
        var estPremierAdmin = role == RoleUtilisateur.Admin && !await _context.Utilisateurs.AnyAsync(u => u.Role == RoleUtilisateur.Admin);

        var utilisateur = new Utilisateur
        {
            Nom = dto.Nom,
            Telephone = dto.Telephone,
            Email = dto.Email,
            MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(dto.MotDePasse),
            Role = role,
            Statut = (estPremierUtilisateur || estPremierAdmin) ? StatutUtilisateur.Valide : StatutUtilisateur.EnAttente
        };

        await _context.Utilisateurs.AddAsync(utilisateur);
        await _context.SaveChangesAsync();

        return new UtilisateurResponseDto
        {
            Id = utilisateur.Id,
            Nom = utilisateur.Nom,
            Telephone = utilisateur.Telephone,
            Email = utilisateur.Email,
            Role = utilisateur.Role.ToString(),
            Statut = utilisateur.Statut.ToString()
        };
    }

    public async Task<UtilisateurResponseDto> ConnexionAsync(ConnexionDto dto)
    {
        var utilisateur = await _context.Utilisateurs.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (utilisateur is null || !BCrypt.Net.BCrypt.Verify(dto.MotDePasse, utilisateur.MotDePasseHash))
        {
            throw new NotFoundException("Email ou mot de passe incorrect");
        }

        if (utilisateur.Statut == StatutUtilisateur.EnAttente)
        {
            throw new ConflictException("Votre compte est en attente de validation par un administrateur");
        }
        if (utilisateur.Statut == StatutUtilisateur.Refuse)
        {
            throw new ConflictException("Votre compte n'a pas été validé par l'administrateur");
        }

        return new UtilisateurResponseDto
        {
            Id = utilisateur.Id,
            Nom = utilisateur.Nom,
            Email = utilisateur.Email,
            Telephone = utilisateur.Telephone,
            Role = utilisateur.Role.ToString(),
            Statut = utilisateur.Statut.ToString(),
            Token = _tokenServices.GenererToken(utilisateur)
        };
    }

    public async Task<UtilisateurResponseDto> ValiderAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id);
        if (utilisateur is null)
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        utilisateur.Statut = StatutUtilisateur.Valide;
        await _context.SaveChangesAsync();

        return new UtilisateurResponseDto
        {
            Id = utilisateur.Id,
            Nom = utilisateur.Nom,
            Telephone = utilisateur.Telephone,
            Email = utilisateur.Email,
            Role = utilisateur.Role.ToString(),
            Statut = utilisateur.Statut.ToString()
        };
    }

    public async Task<UtilisateurResponseDto> RefuserAsync(int id)
    {
        var utilisateur = await _context.Utilisateurs.FindAsync(id);
        if (utilisateur is null)
        {
            throw new NotFoundException("Cet utilisateur n'existe pas");
        }
        utilisateur.Statut = StatutUtilisateur.Refuse;
        await _context.SaveChangesAsync();

        return new UtilisateurResponseDto
        {
            Id = utilisateur.Id,
            Nom = utilisateur.Nom,
            Telephone = utilisateur.Telephone,
            Email = utilisateur.Email,
            Role = utilisateur.Role.ToString(),
            Statut = utilisateur.Statut.ToString()
        };
    }

    public async Task<PagedResultDto<UtilisateurResponseDto>> ListerAsync(int page = 1, int taille = 20)
    {
        if (page < 1) page = 1;
        if (taille < 1 || taille > 100) taille = 20;

        var totalElements = await _context.Utilisateurs.CountAsync();

        var donnees = await _context.Utilisateurs
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Skip((page - 1) * taille)
            .Take(taille)
            .Select(u => new UtilisateurResponseDto
            {
                Id = u.Id,
                Nom = u.Nom,
                Telephone = u.Telephone,
                Email = u.Email,
                Role = u.Role.ToString(),
                Statut = u.Statut.ToString()
            })
            .ToListAsync();

        return new PagedResultDto<UtilisateurResponseDto>
        {
            Donnees = donnees,
            PageActuelle = page,
            TaillePage = taille,
            TotalElements = totalElements,
            TotalPages = (int)Math.Ceiling(totalElements / (double)taille)
        };
    }
}
