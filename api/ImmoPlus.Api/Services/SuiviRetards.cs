namespace ImmoPlus.Api.Services;

using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;

/// <summary>
/// Ponctualité des mensualités, calculée à partir de l'historique des paiements :
/// une mensualité est réglée le jour du paiement qui atteint le montant dû ; son retard se compte depuis l'échéance.
/// Tant qu'elle n'est pas réglée et que l'échéance est dépassée, elle est « en retard » depuis l'échéance jusqu'à aujourd'hui.
/// </summary>
public static class SuiviRetards
{
    public const string AVenir = "AVenir";
    public const string ATemps = "ATemps";
    public const string PayeeEnRetard = "PayeeEnRetard";
    public const string EnRetard = "EnRetard";

    /// <summary>Ponctualité d'une échéance (mensualité ou loyer) d'après ses paiements.</summary>
    public static (string Ponctualite, int JoursRetard, DateTime? DateReglement, List<PaiementResponseDto> Paiements) Evaluer(
        DateTime dateEcheance, decimal montantDu, IEnumerable<Paiement> paiementsBruts, DateTime aujourdhui)
    {
        var paiements = paiementsBruts.OrderBy(p => p.DatePaiement).ThenBy(p => p.Id).ToList();

        DateTime? reglementComplet = null;
        decimal cumul = 0;
        foreach (var p in paiements)
        {
            cumul += p.Montant;
            if (cumul >= montantDu)
            {
                reglementComplet = p.DatePaiement.Date;
                break;
            }
        }

        var echeance = dateEcheance.Date;
        string ponctualite;
        int joursRetard;
        if (reglementComplet is { } regle)
        {
            joursRetard = Math.Max(0, (regle - echeance).Days);
            ponctualite = joursRetard > 0 ? PayeeEnRetard : ATemps;
        }
        else if (echeance < aujourdhui.Date)
        {
            joursRetard = (aujourdhui.Date - echeance).Days;
            ponctualite = EnRetard;
        }
        else
        {
            joursRetard = 0;
            ponctualite = AVenir;
        }

        var dtos = paiements.Select(p => new PaiementResponseDto
        {
            Id = p.Id,
            MensualiteId = p.MensualiteId,
            Montant = p.Montant,
            DatePaiement = p.DatePaiement,
            ModePaiement = p.ModePaiement,
            Reference = p.Reference
        }).ToList();
        return (ponctualite, joursRetard, reglementComplet, dtos);
    }

    public static MensualiteResponseDto VersDto(Mensualite m, DateTime aujourdhui)
    {
        var (ponctualite, joursRetard, dateReglement, paiements) = Evaluer(m.DateEcheance, m.MontantDu, m.Paiements, aujourdhui);
        return new MensualiteResponseDto
        {
            Id = m.Id,
            FinancementLocationId = m.FinancementLocationId,
            NumeroEcheance = m.NumeroEcheance,
            DateEcheance = m.DateEcheance,
            MontantDu = m.MontantDu,
            MontantPaye = m.MontantPaye,
            Statut = m.Statut.ToString(),
            Ponctualite = ponctualite,
            JoursRetard = joursRetard,
            DateReglement = dateReglement,
            Paiements = paiements
        };
    }

    public static LoyerResponseDto VersDto(Loyer l, DateTime aujourdhui)
    {
        var (ponctualite, joursRetard, dateReglement, paiements) = Evaluer(l.DateEcheance, l.MontantDu, l.Paiements, aujourdhui);
        return new LoyerResponseDto
        {
            Id = l.Id,
            LocationId = l.LocationId,
            Numero = l.Numero,
            DateEcheance = l.DateEcheance,
            MontantDu = l.MontantDu,
            MontantPaye = l.MontantPaye,
            Statut = l.Statut.ToString(),
            Ponctualite = ponctualite,
            JoursRetard = joursRetard,
            DateReglement = dateReglement,
            Paiements = paiements
        };
    }

    /// <summary>Bilan du financement : ponctualité de toutes ses mensualités.</summary>
    public static void RemplirBilan(FinancementLocationResponseDto dto, IEnumerable<Mensualite> mensualites, DateTime aujourdhui)
    {
        var suivi = mensualites.Select(m => VersDto(m, aujourdhui)).ToList();
        dto.MensualitesPayeesATemps = suivi.Count(s => s.Ponctualite == ATemps);
        dto.MensualitesPayeesEnRetard = suivi.Count(s => s.Ponctualite == PayeeEnRetard);
        dto.MensualitesEnRetard = suivi.Count(s => s.Ponctualite == EnRetard);
        dto.JoursRetardCumules = suivi.Sum(s => s.JoursRetard);
        dto.MontantEnRetard = suivi.Where(s => s.Ponctualite == EnRetard).Sum(s => s.MontantDu - s.MontantPaye);
    }
}
