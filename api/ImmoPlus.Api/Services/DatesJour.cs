namespace ImmoPlus.Api.Services;

using System.Globalization;

/// <summary>Lecture des dates « yyyy-MM-dd » passées en paramètre d'URL, avec un message d'erreur en français (400).</summary>
public static class DatesJour
{
    public static DateTime? Lire(string? valeur, string nom)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return null;
        }
        if (DateTime.TryParseExact(valeur.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.Date;
        }
        throw new InvalidOperationException($"Le paramètre « {nom} » doit être une date au format yyyy-MM-dd");
    }
}
