namespace ImmoPlus.Api.Services;

/// <summary>
/// Recherches « contient » insensibles à la casse et aux accents, traduites en SQL PostgreSQL :
/// <c>EF.Functions.ILike(EF.Functions.Unaccent(colonne), EF.Functions.Unaccent(RechercheTexte.Contient(saisie)))</c>
/// (« kipe » trouve « Kipé », « CONAKRY » trouve « Conakry »).
/// </summary>
public static class RechercheTexte
{
    /// <summary>Motif ILIKE « %saisie% » ; les caractères spéciaux saisis (%, _, \) sont cherchés tels quels.</summary>
    public static string Contient(string saisie) =>
        "%" + saisie.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

    /// <summary>
    /// N° de référence saisi (« 000123 », « n° 123 », « #123 ») → 123 ; null si la saisie n'est pas un numéro.
    /// Les listes acceptent ainsi ?q= avec la référence affichée sur 6 chiffres.
    /// </summary>
    public static int? Reference(string? saisie)
    {
        var texte = (saisie ?? "").Trim().TrimStart('#').Trim();
        if (texte.StartsWith("n°", StringComparison.OrdinalIgnoreCase))
        {
            texte = texte[2..].Trim();
        }
        return texte.Length is > 0 and <= 9 && texte.All(char.IsAsciiDigit) && int.TryParse(texte, out var numero) && numero > 0 ? numero : null;
    }
}
