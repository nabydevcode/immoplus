namespace ImmoPlus.Api.Services;

using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

public class AppartementServices
{
    private readonly AppDbContext _context;

    public AppartementServices(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AppartementResponseDto> AjouterAppartementAsync(AppartementDto dto)
    {
        var bailleur = await _context.Utilisateurs.FindAsync(dto.BailleurId);
        if (bailleur is null)
        {
            throw new NotFoundException("Ce bailleur n'existe pas, impossible d'ajouter cet appartement");
        }
        if (bailleur.Role != RoleUtilisateur.Bailleur)
        {
            throw new ConflictException("Cet utilisateur n'a pas le rôle Bailleur");
        }

        if (!Enum.TryParse<TypeOffre>(dto.TypeOffre, true, out var typeOffre))
        {
            throw new InvalidOperationException("TypeOffre invalide. Utilisez LongueDuree ou SejourCourt");
        }

        if (typeOffre == TypeOffre.LongueDuree)
        {
            if (dto.LoyerMensuel is null || dto.LoyerMensuel <= 0 || dto.DureeAvanceExigee is null || dto.DureeAvanceExigee <= 0)
            {
                throw new InvalidOperationException("LoyerMensuel et DureeAvanceExigee sont obligatoires pour une offre LongueDuree");
            }
        }
        else
        {
            if (dto.PrixParNuit is null || dto.PrixParNuit <= 0)
            {
                throw new InvalidOperationException("PrixParNuit est obligatoire pour une offre SejourCourt");
            }
        }

        var appartement = new Appartement
        {
            BailleurId = dto.BailleurId,
            Adresse = dto.Adresse,
            Description = dto.Description,
            Disponible = true,
            TypeOffre = typeOffre,
            LoyerMensuel = typeOffre == TypeOffre.LongueDuree ? dto.LoyerMensuel : null,
            DureeAvanceExigee = typeOffre == TypeOffre.LongueDuree ? dto.DureeAvanceExigee : null,
            PrixParNuit = typeOffre == TypeOffre.SejourCourt ? dto.PrixParNuit : null,
            DureeMaxMois = typeOffre == TypeOffre.SejourCourt ? (dto.DureeMaxMois ?? 1) : null
        };

        await _context.Appartements.AddAsync(appartement);
        await _context.SaveChangesAsync();

        return VersDto(appartement, bailleur.Nom);
    }

    public async Task<List<AppartementResponseDto>> ListerAsync(TypeOffre? typeOffre = null)
    {
        var requete = _context.Appartements.AsNoTracking().Include(a => a.Bailleur).AsQueryable();

        if (typeOffre is not null)
        {
            requete = requete.Where(a => a.TypeOffre == typeOffre);
        }

        var appartements = await requete.ToListAsync();

        return appartements.Select(a => VersDto(a, a.Bailleur!.Nom)).ToList();
    }

    private static AppartementResponseDto VersDto(Appartement appartement, string bailleurNom) => new()
    {
        Id = appartement.Id,
        BailleurId = appartement.BailleurId,
        BailleurNom = bailleurNom,
        Adresse = appartement.Adresse,
        Description = appartement.Description,
        Disponible = appartement.Disponible,
        TypeOffre = appartement.TypeOffre.ToString(),
        LoyerMensuel = appartement.LoyerMensuel,
        DureeAvanceExigee = appartement.DureeAvanceExigee,
        PrixParNuit = appartement.PrixParNuit,
        DureeMaxMois = appartement.DureeMaxMois
    };
}
