namespace ImmoPlus.Api.Services;

using System.Globalization;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Calcul du remboursement d'un séjour annulé, sans accès à la base : partagé par l'aperçu et l'annulation
/// pour que le montant annoncé soit exactement celui enregistré.
/// </summary>
public static class PolitiqueRemboursement
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public readonly record struct Resultat(decimal MontantRembourse, int PourcentageNuitees, string Explication);

    /// <param name="montantPaye">Total des paiements enregistrés.</param>
    /// <param name="montantTotal">Prix du séjour (nuitées après réduction + ménage), caution exclue.</param>
    /// <param name="dateCreation">Date de la réservation (délai de 48 h de la politique stricte).</param>
    /// <param name="parHote">Annulation par l'hôte ou par l'admin pour son compte : toujours intégrale.</param>
    public static Resultat Calculer(PolitiqueAnnulation politique, decimal montantPaye, decimal montantTotal, decimal fraisMenage,
        DateTime dateArrivee, DateTime dateCreation, DateTime maintenant, bool parHote)
    {
        var joursAvant = (dateArrivee.Date - maintenant.Date).Days;
        // Annuler le jour de l'arrivée reste « avant l'arrivée » : le logement n'a pas été occupé, le ménage n'est pas dû.
        var avantArrivee = joursAvant >= 0;
        var menage = Math.Min(fraisMenage, montantTotal);
        var nuitees = montantTotal - menage;

        int pourcentage;
        string regle;
        if (parHote)
        {
            pourcentage = 100;
            regle = "Annulation par l'hôte : le voyageur est intégralement remboursé.";
        }
        else
        {
            (pourcentage, regle) = politique switch
            {
                PolitiqueAnnulation.Flexible => joursAvant >= 1
                    ? (100, "Politique flexible : annulation au moins 1 jour avant l'arrivée, remboursement intégral.")
                    : (0, "Politique flexible : annulation moins d'1 jour avant l'arrivée, les nuitées ne sont pas remboursées."),
                PolitiqueAnnulation.Moderee => joursAvant >= 5
                    ? (100, "Politique modérée : annulation au moins 5 jours avant l'arrivée, remboursement intégral.")
                    : joursAvant >= 1
                        ? (50, "Politique modérée : annulation entre 1 et 5 jours avant l'arrivée, 50 % des nuitées remboursées.")
                        : (0, "Politique modérée : annulation moins d'1 jour avant l'arrivée, les nuitées ne sont pas remboursées."),
                _ => maintenant - dateCreation <= TimeSpan.FromHours(48) && joursAvant >= 14
                    ? (100, "Politique stricte : annulation dans les 48 h suivant la réservation et au moins 14 jours avant l'arrivée, remboursement intégral.")
                    : joursAvant >= 7
                        ? (50, "Politique stricte : annulation au moins 7 jours avant l'arrivée, 50 % des nuitées remboursées.")
                        : (0, "Politique stricte : annulation moins de 7 jours avant l'arrivée, les nuitées ne sont pas remboursées.")
            };
        }

        var calcul = pourcentage == 100
            ? montantTotal
            : Math.Round(nuitees * pourcentage / 100m, 0, MidpointRounding.AwayFromZero) + (avantArrivee ? menage : 0);
        var rembourse = Math.Max(0, Math.Min(montantPaye, calcul));

        string detail;
        if (montantPaye <= 0)
        {
            detail = "Aucun paiement n'a été enregistré : il n'y a rien à rembourser.";
        }
        else
        {
            var menageTexte = pourcentage < 100 && avantArrivee && menage > 0 ? $" Les frais de ménage ({Montant(menage)}) sont remboursés." : "";
            detail = $"{menageTexte} Sur {Montant(montantPaye)} payés, {Montant(rembourse)} seront remboursés (la caution est traitée à part).".TrimStart();
        }
        return new Resultat(rembourse, pourcentage, $"{regle} {detail}");
    }

    private static string Montant(decimal montant) => $"{montant.ToString("N0", Fr)} GNF";
}
