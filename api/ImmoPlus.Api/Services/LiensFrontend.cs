namespace ImmoPlus.Api.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Adresse du site (front) placée dans les liens des emails. Par défaut Frontend:Url. En développement, si la requête
/// qui déclenche l'email vient d'une origine listée dans Frontend:OriginesAutorisees (ex. « https://*.devtunnels.ms »),
/// les liens de CET email pointent vers cette origine (test depuis un téléphone via un tunnel).
/// Les traitements planifiés (rappels) et ceux qui l'exigent utilisent toujours Frontend:Url (voir UtiliserUrlConfiguree).
/// </summary>
public class LiensFrontend
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment _environnement;

    public LiensFrontend(IConfiguration configuration, IHttpContextAccessor httpContextAccessor, IHostEnvironment environnement)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _environnement = environnement;
    }

    /// <summary>Force Frontend:Url pour le reste de la portée (ex. rappels lancés depuis la route admin).</summary>
    public bool UtiliserUrlConfiguree { get; set; }

    public string UrlConfiguree => (_configuration["Frontend:Url"] ?? "http://localhost:5173").TrimEnd('/');

    /// <summary>Base des liens (sans « / » final).</summary>
    public string Url => UtiliserUrlConfiguree || !_environnement.IsDevelopment() ? UrlConfiguree : OrigineAutorisee() ?? UrlConfiguree;

    private string? OrigineAutorisee()
    {
        var requete = _httpContextAccessor.HttpContext?.Request;
        if (requete is null)
        {
            return null;
        }
        var origine = requete.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origine)
            && Uri.TryCreate(requete.Headers.Referer.FirstOrDefault(), UriKind.Absolute, out var referent))
        {
            origine = referent.GetLeftPart(UriPartial.Authority);
        }
        if (string.IsNullOrWhiteSpace(origine) || !Uri.TryCreate(origine, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }
        var normalisee = uri.GetLeftPart(UriPartial.Authority);
        var motifs = _configuration.GetSection("Frontend:OriginesAutorisees").Get<string[]>() ?? [];
        return motifs.Any(m => Correspond(normalisee, m)) ? normalisee : null;
    }

    /// <summary>« https://*.devtunnels.ms » : « * » remplace un ou plusieurs sous-domaines (jamais « / » ni « : »).</summary>
    public static bool Correspond(string origine, string motif)
    {
        if (string.IsNullOrWhiteSpace(motif))
        {
            return false;
        }
        var regex = "^" + Regex.Escape(motif.Trim().TrimEnd('/')).Replace(@"\*", @"[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*") + "$";
        return Regex.IsMatch(origine, regex, RegexOptions.IgnoreCase);
    }
}
