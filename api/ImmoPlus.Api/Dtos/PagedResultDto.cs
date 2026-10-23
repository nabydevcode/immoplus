namespace ImmoPlus.Api.Dtos;

using Microsoft.EntityFrameworkCore;

public class PagedResultDto<T>
{
    public List<T> Donnees { get; set; } = new();
    public int PageActuelle { get; set; }
    public int TaillePage { get; set; }
    public int TotalElements { get; set; }
    public int TotalPages { get; set; }

    public static PagedResultDto<T> Creer(List<T> donnees, Pagination pagination, int totalElements) => new()
    {
        Donnees = donnees,
        PageActuelle = pagination.Page,
        TaillePage = pagination.Taille,
        TotalElements = totalElements,
        TotalPages = (int)Math.Ceiling(totalElements / (double)pagination.Taille)
    };
}

/// <summary>
/// Page demandée (1 par défaut) et taille (20 par défaut, plafonnée à <paramref name="Max"/> : au-delà, on renvoie la taille
/// maximale plutôt qu'une erreur ; le client lit TaillePage et TotalPages). Toujours avec un tri stable côté requête.
/// </summary>
public readonly record struct Pagination(int Page, int Taille)
{
    public const int TailleParDefaut = 20;
    public const int TailleMax = 100;

    public int Saut => (Page - 1) * Taille;

    public static Pagination Lire(int? page, int? taille, int defaut = TailleParDefaut, int max = TailleMax) =>
        new(page is null or < 1 ? 1 : page.Value, taille is null or < 1 ? defaut : Math.Min(taille.Value, max));
}

public static class PaginationExtensions
{
    /// <summary>Compte puis lit la page demandée d'une requête déjà triée (tri stable : terminer par l'Id).</summary>
    public static async Task<(List<T> Elements, int Total)> PageAsync<T>(this IQueryable<T> requete, Pagination pagination)
    {
        var total = await requete.CountAsync();
        // Page au-delà de la fin : liste vide sans requête inutile.
        var elements = pagination.Saut >= total ? new List<T>()
            : await requete.Skip(pagination.Saut).Take(pagination.Taille).ToListAsync();
        return (elements, total);
    }
}
