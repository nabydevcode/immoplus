namespace ImmoPlus.Api.Dtos;

public class PagedResultDto<T>
{
    public List<T> Donnees { get; set; } = new();
    public int PageActuelle { get; set; }
    public int TaillePage { get; set; }
    public int TotalElements { get; set; }
    public int TotalPages { get; set; }
}
