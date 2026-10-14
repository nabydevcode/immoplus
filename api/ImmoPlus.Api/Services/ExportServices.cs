namespace ImmoPlus.Api.Services;

using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Exports Excel (admin) avec ClosedXML (licence MIT) : colonnes en français, références sur 6 chiffres, montants en nombres
/// (format « # ##0 GNF »), dates jj/mm/aaaa, ligne d'en-tête figée, filtres automatiques et largeurs ajustées.
/// </summary>
public class ExportServices
{
    public static readonly string[] Types = ["paiements", "loyers", "mensualites", "reservations", "utilisateurs", "locations"];

    private enum Format { Texte, Montant, Date, Entier }
    private sealed record Colonne(string Titre, Format Format);

    private readonly AppDbContext _context;
    private readonly JournalServices _journal;

    public ExportServices(AppDbContext context, JournalServices journal)
    {
        _context = context;
        _journal = journal;
    }

    public sealed record FichierExport(byte[] Contenu, string NomFichier);

    public async Task<FichierExport> ExporterAsync(string type, DateTime? du, DateTime? au)
    {
        type = (type ?? "").Trim().ToLowerInvariant();
        if (!Types.Contains(type))
        {
            throw new NotFoundException($"Export inconnu. Types disponibles : {string.Join(", ", Types)}");
        }
        if (du is not null && au is not null && au < du)
        {
            throw new InvalidOperationException("La date de fin doit être postérieure à la date de début");
        }
        var debut = du?.Date ?? DateTime.MinValue;
        var fin = au?.Date.AddDays(1) ?? DateTime.MaxValue;

        var (titre, colonnes, lignes) = type switch
        {
            "paiements" => await PaiementsAsync(debut, fin),
            "loyers" => await LoyersAsync(debut, fin),
            "mensualites" => await MensualitesAsync(debut, fin),
            "reservations" => await ReservationsAsync(debut, fin),
            "utilisateurs" => await UtilisateursAsync(debut, fin, du is not null || au is not null),
            _ => await LocationsAsync(debut, fin)
        };

        var contenu = Classeur(titre, colonnes, lignes);
        await _journal.EnregistrerAsync("export.telecharger", "Export", null,
            $"Export Excel « {type} » ({lignes.Count} ligne(s), {(du is null && au is null ? "toutes dates" : $"période {Libelle(du)} → {Libelle(au)}")})");
        return new FichierExport(contenu, $"immoplus-{type}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static string Libelle(DateTime? date) => date?.ToString("dd/MM/yyyy") ?? "…";

    private static byte[] Classeur(string titre, List<Colonne> colonnes, List<object?[]> lignes)
    {
        using var classeur = new XLWorkbook();
        var feuille = classeur.Worksheets.Add(titre);
        for (var c = 0; c < colonnes.Count; c++)
        {
            var cellule = feuille.Cell(1, c + 1);
            cellule.Value = colonnes[c].Titre;
            cellule.Style.Font.Bold = true;
            cellule.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F2EA");
        }
        for (var l = 0; l < lignes.Count; l++)
        {
            for (var c = 0; c < colonnes.Count; c++)
            {
                var cellule = feuille.Cell(l + 2, c + 1);
                switch (lignes[l][c])
                {
                    case null:
                        break;
                    case decimal d:
                        cellule.Value = d;
                        break;
                    case int i:
                        cellule.Value = i;
                        break;
                    case DateTime date:
                        cellule.Value = date.Date;
                        break;
                    case bool b:
                        cellule.Value = b ? "Oui" : "Non";
                        break;
                    default:
                        // Texte : jamais interprété comme une formule (contenu saisi par les utilisateurs).
                        cellule.Value = lignes[l][c]!.ToString();
                        break;
                }
            }
        }
        for (var c = 0; c < colonnes.Count; c++)
        {
            var colonne = feuille.Column(c + 1);
            switch (colonnes[c].Format)
            {
                case Format.Montant:
                    colonne.Style.NumberFormat.Format = "#,##0 \"GNF\"";
                    break;
                case Format.Date:
                    colonne.Style.DateFormat.Format = "dd/mm/yyyy";
                    break;
                case Format.Entier:
                    colonne.Style.NumberFormat.Format = "0";
                    break;
            }
        }
        feuille.SheetView.FreezeRows(1);
        feuille.Range(1, 1, Math.Max(1, lignes.Count + 1), colonnes.Count).SetAutoFilter();
        feuille.Columns(1, colonnes.Count).AdjustToContents(1, Math.Min(lignes.Count + 1, 500));
        foreach (var colonne in feuille.Columns(1, colonnes.Count))
        {
            colonne.Width = Math.Min(Math.Max(colonne.Width, 10), 60);
        }

        using var flux = new MemoryStream();
        classeur.SaveAs(flux);
        return flux.ToArray();
    }

    private static Colonne T(string titre) => new(titre, Format.Texte);
    private static Colonne M(string titre) => new(titre, Format.Montant);
    private static Colonne D(string titre) => new(titre, Format.Date);
    private static Colonne E(string titre) => new(titre, Format.Entier);

    private static string Statut(StatutMensualite s) => s switch
    {
        StatutMensualite.AVenir => "À venir",
        StatutMensualite.Partiellement => "Partiellement payé",
        _ => "Payé"
    };

    private async Task<(string, List<Colonne>, List<object?[]>)> PaiementsAsync(DateTime debut, DateTime fin)
    {
        var paiements = await _context.Paiements.AsNoTracking()
            .Where(p => p.DatePaiement >= debut && p.DatePaiement < fin)
            .OrderBy(p => p.DatePaiement).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id, p.DatePaiement, p.Montant, p.ModePaiement, p.Reference, p.MensualiteId, p.LoyerId, p.ReservationId,
                FinancementId = (int?)p.Mensualite!.FinancementLocationId,
                NumeroMensualite = (int?)p.Mensualite!.NumeroEcheance,
                LocationMensualite = (int?)p.Mensualite!.FinancementLocation!.LocationId,
                LocataireMensualite = p.Mensualite!.FinancementLocation!.Location!.Locataire!.Nom,
                LogementMensualite = p.Mensualite!.FinancementLocation!.Location!.Appartement!.Adresse,
                LocationLoyer = (int?)p.Loyer!.LocationId,
                NumeroLoyer = (int?)p.Loyer!.Numero,
                LocataireLoyer = p.Loyer!.Location!.Locataire!.Nom,
                LogementLoyer = p.Loyer!.Location!.Appartement!.Adresse,
                Voyageur = p.Reservation!.Locataire!.Nom,
                LogementReservation = p.Reservation!.Appartement!.Adresse
            })
            .ToListAsync();
        var colonnes = new List<Colonne> { T("Réf. paiement"), D("Date"), M("Montant"), T("Mode"), T("Référence"), T("Objet"),
            T("Réf. dossier"), T("Payeur"), T("Logement") };
        var lignes = paiements.Select(p =>
        {
            string objet, reference, payeur, logement;
            if (p.MensualiteId is not null)
            {
                objet = $"Mensualité n° {p.NumeroMensualite} (financement n° {p.FinancementId:D6})";
                reference = $"Location {p.LocationMensualite:D6}";
                payeur = p.LocataireMensualite ?? "";
                logement = p.LogementMensualite ?? "";
            }
            else if (p.LoyerId is not null)
            {
                objet = $"Loyer n° {p.NumeroLoyer}";
                reference = $"Location {p.LocationLoyer:D6}";
                payeur = p.LocataireLoyer ?? "";
                logement = p.LogementLoyer ?? "";
            }
            else
            {
                objet = "Séjour court";
                reference = $"Réservation {p.ReservationId:D6}";
                payeur = p.Voyageur ?? "";
                logement = p.LogementReservation ?? "";
            }
            return new object?[] { p.Id.ToString("D6"), p.DatePaiement, p.Montant, p.ModePaiement, p.Reference, objet, reference, payeur, logement };
        }).ToList();
        return ("Paiements", colonnes, lignes);
    }

    private async Task<(string, List<Colonne>, List<object?[]>)> LoyersAsync(DateTime debut, DateTime fin)
    {
        var loyers = await _context.Loyers.AsNoTracking()
            .Where(l => l.DateEcheance >= debut && l.DateEcheance < fin)
            .OrderBy(l => l.DateEcheance).ThenBy(l => l.LocationId).ThenBy(l => l.Numero)
            .Select(l => new { l.LocationId, l.Numero, l.DateEcheance, Locataire = l.Location!.Locataire!.Nom, Logement = l.Location!.Appartement!.Adresse,
                l.MontantDu, l.MontantPaye, l.Statut })
            .ToListAsync();
        var colonnes = new List<Colonne> { T("Réf. location"), E("N° loyer"), D("Échéance"), T("Locataire"), T("Logement"),
            M("Montant dû"), M("Montant payé"), M("Reste dû"), T("Statut") };
        var lignes = loyers.Select(l => new object?[] { l.LocationId.ToString("D6"), l.Numero, l.DateEcheance, l.Locataire, l.Logement,
            l.MontantDu, l.MontantPaye, l.MontantDu - l.MontantPaye, Statut(l.Statut) }).ToList();
        return ("Loyers", colonnes, lignes);
    }

    private async Task<(string, List<Colonne>, List<object?[]>)> MensualitesAsync(DateTime debut, DateTime fin)
    {
        var mensualites = await _context.Mensualites.AsNoTracking()
            .Where(m => m.DateEcheance >= debut && m.DateEcheance < fin)
            .OrderBy(m => m.DateEcheance).ThenBy(m => m.FinancementLocationId).ThenBy(m => m.NumeroEcheance)
            .Select(m => new { m.FinancementLocationId, m.FinancementLocation!.LocationId, m.NumeroEcheance, m.DateEcheance,
                Locataire = m.FinancementLocation!.Location!.Locataire!.Nom, m.MontantDu, m.MontantPaye, m.Statut })
            .ToListAsync();
        var colonnes = new List<Colonne> { T("Réf. financement"), T("Réf. location"), E("N° mensualité"), D("Échéance"), T("Locataire"),
            M("Montant dû"), M("Montant payé"), M("Reste dû"), T("Statut") };
        var lignes = mensualites.Select(m => new object?[] { m.FinancementLocationId.ToString("D6"), m.LocationId.ToString("D6"), m.NumeroEcheance,
            m.DateEcheance, m.Locataire, m.MontantDu, m.MontantPaye, m.MontantDu - m.MontantPaye, Statut(m.Statut) }).ToList();
        return ("Mensualités", colonnes, lignes);
    }

    private async Task<(string, List<Colonne>, List<object?[]>)> ReservationsAsync(DateTime debut, DateTime fin)
    {
        var reservations = await _context.Reservations.AsNoTracking()
            .Where(r => r.DateArrivee >= debut && r.DateArrivee < fin)
            .OrderBy(r => r.DateArrivee).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.DateCreation, r.DateArrivee, r.DateDepart, r.NombreNuits, r.NombreVoyageurs, Voyageur = r.Locataire!.Nom,
                Logement = r.Appartement!.Adresse, Hote = r.Appartement!.Bailleur!.Nom, r.MontantTotal, r.Statut, r.AnnuleePar,
                r.MontantARembourser, r.MontantRembourse })
            .ToListAsync();
        var ids = reservations.Select(r => r.Id).ToList();
        var payes = (await _context.Paiements.AsNoTracking()
                .Where(p => p.ReservationId != null && ids.Contains(p.ReservationId!.Value))
                .Select(p => new { ReservationId = p.ReservationId!.Value, p.Montant })
                .ToListAsync())
            .GroupBy(p => p.ReservationId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Montant));
        var colonnes = new List<Colonne> { T("Réf. réservation"), D("Créée le"), D("Arrivée"), D("Départ"), E("Nuits"), E("Voyageurs"),
            T("Voyageur"), T("Logement"), T("Hôte"), M("Montant total"), M("Payé"), T("Statut"), T("Annulée par"), M("À rembourser"), M("Remboursé") };
        var lignes = reservations.Select(r => new object?[] { r.Id.ToString("D6"), r.DateCreation, r.DateArrivee, r.DateDepart, r.NombreNuits,
            r.NombreVoyageurs, r.Voyageur, r.Logement, r.Hote, r.MontantTotal, payes.GetValueOrDefault(r.Id), r.Statut.ToString(),
            r.AnnuleePar?.ToString(), r.MontantARembourser, r.MontantRembourse }).ToList();
        return ("Réservations", colonnes, lignes);
    }

    private async Task<(string, List<Colonne>, List<object?[]>)> UtilisateursAsync(DateTime debut, DateTime fin, bool filtre)
    {
        // Sans période : tous les comptes (y compris ceux créés avant le suivi de la date d'inscription).
        var utilisateurs = await _context.Utilisateurs.AsNoTracking()
            .Where(u => !filtre || (u.DateCreation != null && u.DateCreation >= debut && u.DateCreation < fin))
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Nom, u.Email, u.Telephone, u.Role, u.Statut, u.DateCreation, u.IdentiteVerifiee })
            .ToListAsync();
        var colonnes = new List<Colonne> { T("Réf. utilisateur"), T("Nom"), T("Email"), T("Téléphone"), T("Rôle"), T("Statut"),
            D("Inscrit le"), T("Identité vérifiée") };
        var lignes = utilisateurs.Select(u => new object?[] { u.Id.ToString("D6"), u.Nom, u.Email, u.Telephone, u.Role.ToString(),
            u.Statut.ToString(), u.DateCreation, u.IdentiteVerifiee }).ToList();
        return ("Utilisateurs", colonnes, lignes);
    }

    private async Task<(string, List<Colonne>, List<object?[]>)> LocationsAsync(DateTime debut, DateTime fin)
    {
        var locations = await _context.Locations.AsNoTracking()
            .Where(l => l.DateDebut >= debut && l.DateDebut < fin)
            .OrderBy(l => l.DateDebut).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.CandidatureId, Locataire = l.Locataire!.Nom, Logement = l.Appartement!.Adresse, Bailleur = l.Appartement!.Bailleur!.Nom,
                l.DateDebut, l.DateFin, l.Statut, l.DateApportPaye, l.DateRemiseCles, l.DatePreavis, l.DateFinPrevue, l.Appartement!.LoyerMensuel })
            .ToListAsync();
        var colonnes = new List<Colonne> { T("Réf. location"), T("Réf. dossier"), T("Locataire"), T("Logement"), T("Bailleur"), D("Début"),
            D("Fin"), T("Statut"), D("Apport payé le"), D("Clés remises le"), D("Préavis le"), D("Fin prévue"), M("Loyer mensuel") };
        var lignes = locations.Select(l => new object?[] { l.Id.ToString("D6"), l.CandidatureId?.ToString("D6"), l.Locataire, l.Logement, l.Bailleur,
            l.DateDebut, l.DateFin, l.Statut.ToString(), l.DateApportPaye, l.DateRemiseCles, l.DatePreavis, l.DateFinPrevue, l.LoyerMensuel }).ToList();
        return ("Locations", colonnes, lignes);
    }
}
