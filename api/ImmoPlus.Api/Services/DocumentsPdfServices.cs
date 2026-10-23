namespace ImmoPlus.Api.Services;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Exceptions;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Documents PDF téléchargeables : quittance de loyer, reçus (séjour, paiement), contrat de location, état des lieux.
/// Chaque PDF porte le logo texte ImmoPlus, la référence sur 6 chiffres, la date d'émission et le pied « Document généré par ImmoPlus ».
/// Accès : le locataire / voyageur concerné, le bailleur / hôte concerné et l'admin (409 sinon, 404 si le document n'existe pas).
/// </summary>
public class DocumentsPdfServices
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private const string Or = "#B8892F";
    private const string Gris = "#6B7280";
    private const string Encre = "#15161A";

    static DocumentsPdfServices()
    {
        // Licence Community : gratuite pour les entreprises sous 1 M USD de chiffre d'affaires annuel (voir le rapport / README).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _context;
    /// <summary>Photos d'état des lieux intégrées au PDF (miniatures lues depuis le stockage, disque ou S3).</summary>
    private readonly IStockageFichiers _stockage;

    public DocumentsPdfServices(AppDbContext context, IStockageFichiers stockage)
    {
        _context = context;
        _stockage = stockage;
    }

    public readonly record struct FichierPdf(byte[] Contenu, string NomFichier);

    // --- Quittance de loyer ---

    public async Task<FichierPdf> QuittanceLoyerAsync(int loyerId, int utilisateurId, RoleUtilisateur role)
    {
        var loyer = await _context.Loyers.AsNoTracking()
            .Include(l => l.Paiements)
            .Include(l => l.Location).ThenInclude(l => l!.Locataire)
            .Include(l => l.Location).ThenInclude(l => l!.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(l => l.Id == loyerId)
            ?? throw new NotFoundException("Ce loyer n'existe pas");
        var location = loyer.Location!;
        var appartement = location.Appartement!;
        VerifierAcces(role, utilisateurId, location.LocataireId, appartement.BailleurId);
        if (loyer.Statut != StatutMensualite.Payee || loyer.MontantPaye < loyer.MontantDu)
        {
            throw new ConflictException("La quittance n'est disponible qu'une fois le loyer entièrement payé");
        }

        var debut = loyer.DateEcheance.Date;
        var fin = debut.AddMonths(1).AddDays(-1);
        var paiements = loyer.Paiements.OrderBy(p => p.DatePaiement).ThenBy(p => p.Id).ToList();
        var contenu = Generer("Quittance de loyer", $"Quittance n° {loyer.Id:D6}", col =>
        {
            Parties(col, ("Locataire", location.Locataire!), ("Bailleur", appartement.Bailleur!));
            Tableau(col, "Logement",
                ("Adresse", AdresseComplete(appartement)),
                ("Location n°", location.Id.ToString("D6")),
                ("Période", $"du {Date(debut)} au {Date(fin)}"),
                ("Loyer de", debut.ToString("MMMM yyyy", Fr)));
            Tableau(col, "Montant",
                ("Loyer", Montant(loyer.MontantDu)),
                ("Montant reçu", Montant(loyer.MontantPaye)));
            Paiements(col, paiements);
            col.Item().PaddingTop(12).Text(
                $"Le bailleur reconnaît avoir reçu de {location.Locataire!.Nom}, par l'intermédiaire d'ImmoPlus, la somme de {Montant(loyer.MontantPaye)} "
                + $"au titre du loyer du logement situé {AdresseComplete(appartement)} pour la période du {Date(debut)} au {Date(fin)}, et lui en donne quittance, "
                + "sous réserve de tous droits.").FontSize(10);
        });
        return new FichierPdf(contenu, $"quittance-loyer-{loyer.Id:D6}.pdf");
    }

    // --- Reçu de séjour ---

    public async Task<FichierPdf> RecuReservationAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await ChargerReservationAsync(reservationId, utilisateurId, role);
        var appartement = reservation.Appartement!;
        var paiements = await _context.Paiements.AsNoTracking()
            .Where(p => p.ReservationId == reservationId)
            .OrderBy(p => p.DatePaiement).ThenBy(p => p.Id)
            .ToListAsync();
        var totalPaye = paiements.Sum(p => p.Montant);
        var resteDu = reservation.Statut is StatutReservation.Annulee or StatutReservation.Refusee
            ? 0
            : Math.Max(0, reservation.MontantTotal - totalPaye);

        var contenu = Generer("Reçu de séjour", $"Réservation n° {reservation.Id:D6}", col =>
        {
            Parties(col, ("Voyageur", reservation.Locataire!), ("Hôte", appartement.Bailleur!));
            var nuits = (reservation.DateDepart.Date - reservation.DateArrivee.Date).Days;
            Tableau(col, "Séjour",
                ("Logement", AdresseSejour(reservation, appartement, utilisateurId, role)),
                ("Arrivée", Date(reservation.DateArrivee)),
                ("Départ", Date(reservation.DateDepart)),
                ("Durée", $"{nuits} nuit{(nuits > 1 ? "s" : "")}"),
                ("Voyageurs", reservation.NombreVoyageurs.ToString()),
                ("Statut", LibelleStatut(reservation.Statut)));
            var prix = new List<(string, string)>();
            if (reservation.PrixParNuit > 0)
            {
                prix.Add(("Hébergement", $"{nuits} × {Montant(reservation.PrixParNuit)}"));
            }
            if (reservation.ReductionMontant > 0)
            {
                prix.Add(("Réduction", $"− {Montant(reservation.ReductionMontant)}"));
            }
            if (reservation.FraisMenage > 0)
            {
                prix.Add(("Frais de ménage", Montant(reservation.FraisMenage)));
            }
            prix.Add(("Montant total du séjour", Montant(reservation.MontantTotal)));
            prix.Add(("Total réglé", Montant(totalPaye)));
            prix.Add(("Reste dû", Montant(resteDu)));
            if (reservation.Caution > 0)
            {
                prix.Add(("Caution (remboursable, à part)", Montant(reservation.Caution)));
            }
            if (reservation.MontantARembourser is > 0)
            {
                prix.Add(("Remboursement dû", Montant(reservation.MontantARembourser)));
                prix.Add(("Déjà remboursé", Montant(reservation.MontantRembourse ?? 0)));
            }
            Tableau(col, "Montants", prix.ToArray());
            Paiements(col, paiements);
            Section(col, "Politique d'annulation");
            col.Item().Text(DescriptionPolitique(reservation.PolitiqueAnnulation)).FontSize(10);
        });
        return new FichierPdf(contenu, $"recu-sejour-{reservation.Id:D6}.pdf");
    }

    // --- Reçu d'un paiement ---

    public async Task<FichierPdf> RecuPaiementAsync(int paiementId, int utilisateurId, RoleUtilisateur role)
    {
        var paiement = await _context.Paiements.AsNoTracking()
            .Include(p => p.Mensualite).ThenInclude(m => m!.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Locataire)
            .Include(p => p.Mensualite).ThenInclude(m => m!.FinancementLocation).ThenInclude(f => f!.Location).ThenInclude(l => l!.Appartement).ThenInclude(a => a!.Bailleur)
            .Include(p => p.Loyer).ThenInclude(l => l!.Location).ThenInclude(l => l!.Locataire)
            .Include(p => p.Loyer).ThenInclude(l => l!.Location).ThenInclude(l => l!.Appartement).ThenInclude(a => a!.Bailleur)
            .Include(p => p.Reservation).ThenInclude(r => r!.Locataire)
            .Include(p => p.Reservation).ThenInclude(r => r!.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(p => p.Id == paiementId)
            ?? throw new NotFoundException("Ce paiement n'existe pas");

        Utilisateur payeur;
        Appartement appartement;
        string objet;
        var details = new List<(string, string)>();
        if (paiement.Mensualite is { } m)
        {
            var location = m.FinancementLocation!.Location!;
            // Le financement est une affaire entre ImmoPlus et le locataire : le bailleur ne le consulte pas.
            VerifierAcces(role, utilisateurId, location.LocataireId, bailleurId: null);
            payeur = location.Locataire!;
            appartement = location.Appartement!;
            objet = $"Mensualité n° {m.NumeroEcheance} du financement n° {m.FinancementLocationId:D6}";
            details.Add(("Échéance du", Date(m.DateEcheance)));
            details.Add(("Montant de l'échéance", Montant(m.MontantDu)));
            details.Add(("Reste dû sur l'échéance", Montant(Math.Max(0, m.MontantDu - m.MontantPaye))));
            details.Add(("Location n°", location.Id.ToString("D6")));
        }
        else if (paiement.Loyer is { } l)
        {
            var location = l.Location!;
            VerifierAcces(role, utilisateurId, location.LocataireId, location.Appartement!.BailleurId);
            payeur = location.Locataire!;
            appartement = location.Appartement!;
            objet = $"Loyer de {l.DateEcheance.ToString("MMMM yyyy", Fr)}";
            details.Add(("Loyer du mois", Montant(l.MontantDu)));
            details.Add(("Reste dû sur ce loyer", Montant(Math.Max(0, l.MontantDu - l.MontantPaye))));
            details.Add(("Location n°", location.Id.ToString("D6")));
        }
        else if (paiement.Reservation is { } r)
        {
            appartement = r.Appartement!;
            VerifierAcces(role, utilisateurId, r.LocataireId, appartement.BailleurId);
            payeur = r.Locataire!;
            objet = $"Séjour du {Date(r.DateArrivee)} au {Date(r.DateDepart)}";
            details.Add(("Réservation n°", r.Id.ToString("D6")));
            details.Add(("Montant total du séjour", Montant(r.MontantTotal)));
        }
        else
        {
            throw new NotFoundException("Ce paiement n'existe pas");
        }

        var contenu = Generer("Reçu de paiement", $"Paiement n° {paiement.Id:D6}", col =>
        {
            Parties(col, ("Payé par", payeur));
            Tableau(col, "Paiement",
                new[] { ("Objet", objet), ("Logement", appartement.TypeOffre == TypeOffre.SejourCourt && paiement.Reservation is { } res
                        ? AdresseSejour(res, appartement, utilisateurId, role) : AdresseComplete(appartement)),
                    ("Montant reçu", Montant(paiement.Montant)), ("Date du paiement", Date(paiement.DatePaiement)),
                    ("Mode de paiement", paiement.ModePaiement), ("Référence", paiement.Reference ?? "-") }
                .Concat(details).ToArray());
            col.Item().PaddingTop(12).Text($"ImmoPlus atteste avoir reçu la somme de {Montant(paiement.Montant)} de {payeur.Nom}.").FontSize(10);
        });
        return new FichierPdf(contenu, $"recu-paiement-{paiement.Id:D6}.pdf");
    }

    // --- Contrat de location longue durée ---

    public async Task<FichierPdf> ContratLocationAsync(int locationId, int utilisateurId, RoleUtilisateur role)
    {
        var location = await _context.Locations.AsNoTracking()
            .Include(l => l.Locataire)
            .Include(l => l.Appartement).ThenInclude(a => a!.Bailleur)
            .Include(l => l.Candidature)
            .Include(l => l.FinancementLocation).ThenInclude(f => f!.Mensualites)
            .FirstOrDefaultAsync(l => l.Id == locationId)
            ?? throw new NotFoundException("Cette location n'existe pas");
        var appartement = location.Appartement!;
        VerifierAcces(role, utilisateurId, location.LocataireId, appartement.BailleurId);

        var loyer = appartement.LoyerMensuel ?? 0;
        var moisAvance = appartement.DureeAvanceExigee ?? 0;
        var financement = location.FinancementLocation;
        var mensualites = financement?.Mensualites.OrderBy(m => m.NumeroEcheance).ToList() ?? [];
        var debutLoyers = LoyerServices.DebutLoyers(location, appartement);

        var contenu = Generer("Contrat de location", $"Location n° {location.Id:D6}", col =>
        {
            col.Item().Background("#FEF3C7").Padding(8).Text(
                "Modèle indicatif, à faire relire par un professionnel avant signature : il ne remplace pas un contrat établi par un notaire ou un avocat.")
                .FontSize(9).Italic();
            Parties(col, ("Bailleur", appartement.Bailleur!), ("Locataire", location.Locataire!));
            Tableau(col, "Logement loué",
                ("Adresse", AdresseComplete(appartement)),
                ("Description", string.IsNullOrWhiteSpace(appartement.Description) ? "-" : appartement.Description!),
                ("Chambres", appartement.Chambres?.ToString() ?? "-"),
                ("Salles de bain", appartement.SallesDeBain?.ToString() ?? "-"));
            var conditions = new List<(string, string)>
            {
                ("Loyer mensuel", Montant(loyer)),
                ("Avance exigée", moisAvance > 0 ? $"{moisAvance} mois de loyer, soit {Montant(loyer * moisAvance)}" : "-"),
                ("Apport du locataire", Montant(location.Candidature?.ApportInitial ?? financement?.ApportInitial)),
                ("Début de la location", Date(location.DateDebut)),
                ("Remise des clés", location.DateRemiseCles is null ? "À venir" : Date(location.DateRemiseCles)),
                ("Premier loyer mensuel", debutLoyers is null ? "Après la période couverte par l'avance" : Date(debutLoyers))
            };
            Tableau(col, "Conditions financières", conditions.ToArray());
            if (financement is not null)
            {
                var resume = new List<(string, string)>
                {
                    ("Financement n°", financement.Id.ToString("D6")),
                    ("Montant financé par ImmoPlus", Montant(financement.MontantARembourser)),
                    ("Frais de financement", $"{(financement.TauxFrais * 100).ToString("0.##", Fr)} %"),
                    ("Remboursement", $"{financement.NombreMensualites} mensualité{(financement.NombreMensualites > 1 ? "s" : "")} de {Montant(financement.MontantMensualite)}")
                };
                if (mensualites.Count > 0)
                {
                    resume.Add(("Première échéance", Date(mensualites[0].DateEcheance)));
                    resume.Add(("Dernière échéance", $"{Date(mensualites[^1].DateEcheance)} ({Montant(mensualites[^1].MontantDu)})"));
                }
                else
                {
                    resume.Add(("Échéancier", "Généré à la remise des clés"));
                }
                Tableau(col, "Financement de l'avance et échéancier", resume.ToArray());
            }
            Section(col, "Clauses principales");
            var clauses = new[]
            {
                "Le bailleur met le logement décrit ci-dessus à la disposition du locataire, qui l'occupe en bon père de famille et l'entretient.",
                $"L'avance de {moisAvance} mois couvre les premiers mois de location à partir de la remise des clés ; les loyers mensuels, sans frais, "
                    + "sont ensuite dus le même jour chaque mois et réglés auprès d'ImmoPlus.",
                financement is null
                    ? "L'avance est réglée intégralement par le locataire (aucun financement)."
                    : "La part de l'avance non couverte par l'apport est financée par ImmoPlus et remboursée par le locataire selon l'échéancier ci-dessus.",
                "Chaque partie peut mettre fin à la location en respectant un préavis de 3 mois. Les loyers restent dus jusqu'à la fin du préavis, "
                    + "date à laquelle les clés sont restituées et la location clôturée.",
                "Tout retard de paiement fait l'objet de rappels ; les impayés doivent être régularisés avant la clôture de la location."
            };
            foreach (var (clause, i) in clauses.Select((c, i) => (c, i + 1)))
            {
                col.Item().PaddingBottom(4).Text($"{i}. {clause}").FontSize(10);
            }
            col.Item().PaddingTop(24).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Le bailleur").SemiBold();
                    c.Item().PaddingTop(30).Text("Signature :").FontColor(Gris);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Le locataire").SemiBold();
                    c.Item().PaddingTop(30).Text("Signature :").FontColor(Gris);
                });
            });
        });
        return new FichierPdf(contenu, $"contrat-location-{location.Id:D6}.pdf");
    }

    // --- État des lieux photo d'un séjour ---

    public async Task<FichierPdf> EtatDesLieuxAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await ChargerReservationAsync(reservationId, utilisateurId, role);
        var appartement = reservation.Appartement!;
        var photos = await _context.PhotosEtatDesLieux.AsNoTracking()
            .Include(p => p.Auteur)
            .Where(p => p.ReservationId == reservationId)
            .OrderBy(p => p.Moment).ThenBy(p => p.DateCreation).ThenBy(p => p.Id)
            .ToListAsync();
        var images = new Dictionary<int, byte[]?>();
        foreach (var photo in photos)
        {
            images[photo.Id] = await TraitementImages.MiniatureJpegAsync(_stockage, EtatDesLieuxServices.Cle(photo));
        }

        var contenu = Generer("État des lieux", $"Réservation n° {reservation.Id:D6}", col =>
        {
            Parties(col, ("Voyageur", reservation.Locataire!), ("Hôte", appartement.Bailleur!));
            Tableau(col, "Séjour",
                ("Logement", AdresseSejour(reservation, appartement, utilisateurId, role)),
                ("Arrivée", Date(reservation.DateArrivee)),
                ("Départ", Date(reservation.DateDepart)));
            foreach (var moment in new[] { MomentEtatDesLieux.Arrivee, MomentEtatDesLieux.Depart })
            {
                Section(col, moment == MomentEtatDesLieux.Arrivee ? "Photos à l'arrivée" : "Photos au départ");
                var duMoment = photos.Where(p => p.Moment == moment).ToList();
                if (duMoment.Count == 0)
                {
                    col.Item().Text("Aucune photo.").FontColor(Gris).FontSize(10);
                    continue;
                }
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    foreach (var photo in duMoment)
                    {
                        table.Cell().Padding(4).Column(c =>
                        {
                            if (images[photo.Id] is { } octets)
                            {
                                c.Item().Height(150).AlignCenter().Image(octets).FitArea();
                            }
                            else
                            {
                                c.Item().Height(150).AlignCenter().AlignMiddle().Text("Photo indisponible").FontColor(Gris);
                            }
                            c.Item().PaddingTop(2).Text($"{photo.Auteur?.Nom ?? "-"} — {DateHeure(photo.DateCreation)} (UTC)").FontSize(8).FontColor(Gris);
                            if (!string.IsNullOrWhiteSpace(photo.Commentaire))
                            {
                                c.Item().Text(photo.Commentaire).FontSize(9);
                            }
                        });
                    }
                });
            }
        });
        return new FichierPdf(contenu, $"etat-des-lieux-{reservation.Id:D6}.pdf");
    }

    // --- Accès ---

    private async Task<Reservation> ChargerReservationAsync(int reservationId, int utilisateurId, RoleUtilisateur role)
    {
        var reservation = await _context.Reservations.AsNoTracking()
            .Include(r => r.Locataire)
            .Include(r => r.Appartement).ThenInclude(a => a!.Bailleur)
            .FirstOrDefaultAsync(r => r.Id == reservationId)
            ?? throw new NotFoundException("Cette réservation n'existe pas");
        VerifierAcces(role, utilisateurId, reservation.LocataireId, reservation.Appartement!.BailleurId);
        return reservation;
    }

    private static void VerifierAcces(RoleUtilisateur role, int utilisateurId, int locataireId, int? bailleurId)
    {
        if (role != RoleUtilisateur.Admin && utilisateurId != locataireId && utilisateurId != bailleurId)
        {
            throw new ConflictException("Vous n'avez pas accès à ce document");
        }
    }

    /// <summary>Séjour court : la rue n'est donnée au voyageur qu'une fois le séjour confirmé (mêmes règles que les emails).</summary>
    private static string AdresseSejour(Reservation r, Appartement a, int utilisateurId, RoleUtilisateur role) =>
        role != RoleUtilisateur.Admin && utilisateurId != a.BailleurId
            && r.Statut is not (StatutReservation.Confirmee or StatutReservation.Terminee)
            ? AppartementServices.AdresseApproximative(a)
            : AdresseComplete(a);

    private static string AdresseComplete(Appartement a) =>
        string.Join(", ", new[] { a.Adresse, a.Quartier, a.Ville }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct());

    // --- Mise en page commune ---

    private static byte[] Generer(string titre, string reference, Action<ColumnDescriptor> contenu)
    {
        var emission = DateTime.UtcNow;
        return QuestPDF.Fluent.Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Encre));
                page.Header().PaddingBottom(16).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("ImmoPlus").FontSize(22).Bold().FontColor(Or);
                        c.Item().Text("Location et séjours en Guinée").FontSize(9).FontColor(Gris);
                    });
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(titre).FontSize(16).SemiBold();
                        c.Item().AlignRight().Text(reference).FontSize(10);
                        c.Item().AlignRight().Text($"Émis le {Date(emission)}").FontSize(9).FontColor(Gris);
                    });
                });
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    contenu(col);
                });
                page.Footer().BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text($"Document généré par ImmoPlus le {DateHeure(emission)} (UTC) — {reference}").FontSize(8).FontColor(Gris);
                    row.ConstantItem(60).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Gris));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        })
        .WithMetadata(new DocumentMetadata { Title = $"{titre} — {reference}", Author = "ImmoPlus", Creator = "ImmoPlus" })
        .GeneratePdf();
    }

    private static void Section(ColumnDescriptor col, string titre) =>
        col.Item().PaddingTop(10).BorderBottom(0.5f).BorderColor(Or).PaddingBottom(2).Text(titre).FontSize(12).SemiBold();

    private static void Tableau(ColumnDescriptor col, string titre, params (string Libelle, string Valeur)[] lignes)
    {
        Section(col, titre);
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(170);
                c.RelativeColumn();
            });
            foreach (var (libelle, valeur) in lignes)
            {
                table.Cell().PaddingVertical(2).Text(libelle).FontColor(Gris);
                table.Cell().PaddingVertical(2).Text(valeur).SemiBold();
            }
        });
    }

    private static void Parties(ColumnDescriptor col, params (string Role, Utilisateur Personne)[] parties)
    {
        Section(col, parties.Length > 1 ? "Parties" : "Client");
        col.Item().Row(row =>
        {
            foreach (var (libelle, personne) in parties)
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(libelle).FontColor(Gris).FontSize(9);
                    c.Item().Text(personne.Nom).SemiBold();
                    c.Item().Text(personne.Telephone).FontSize(9);
                    c.Item().Text(personne.Email).FontSize(9);
                });
            }
        });
    }

    private static void Paiements(ColumnDescriptor col, IReadOnlyList<Paiement> paiements)
    {
        Section(col, "Paiements enregistrés");
        if (paiements.Count == 0)
        {
            col.Item().Text("Aucun paiement enregistré.").FontColor(Gris);
            return;
        }
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(80);
                c.RelativeColumn();
                c.RelativeColumn();
                c.ConstantColumn(110);
            });
            table.Header(h =>
            {
                foreach (var entete in new[] { "Date", "Mode", "Référence", "Montant" })
                {
                    h.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(2)
                        .Text(entete).FontColor(Gris).FontSize(9);
                }
            });
            foreach (var p in paiements)
            {
                table.Cell().PaddingVertical(2).Text(Date(p.DatePaiement));
                table.Cell().PaddingVertical(2).Text(p.ModePaiement);
                table.Cell().PaddingVertical(2).Text(p.Reference ?? "-");
                table.Cell().PaddingVertical(2).AlignRight().Text(Montant(p.Montant));
            }
        });
    }

    private static string DescriptionPolitique(PolitiqueAnnulation politique) => politique switch
    {
        PolitiqueAnnulation.Flexible => "Flexible : remboursement intégral en cas d'annulation au moins 1 jour avant l'arrivée ; ensuite, les nuitées ne sont pas remboursées.",
        PolitiqueAnnulation.Moderee => "Modérée : remboursement intégral jusqu'à 5 jours avant l'arrivée, 50 % des nuitées entre 1 et 5 jours avant ; ensuite, les nuitées ne sont pas remboursées.",
        _ => "Stricte : remboursement intégral si l'annulation a lieu dans les 48 h suivant la réservation et au moins 14 jours avant l'arrivée ; "
            + "50 % des nuitées jusqu'à 7 jours avant l'arrivée ; ensuite, les nuitées ne sont pas remboursées."
    } + " Les frais de ménage sont remboursés si l'annulation a lieu avant l'arrivée ; la caution est traitée à part. Une annulation par l'hôte est toujours intégralement remboursée.";

    private static string LibelleStatut(StatutReservation statut) => statut switch
    {
        StatutReservation.EnAttente => "En attente de paiement",
        StatutReservation.Confirmee => "Confirmée",
        StatutReservation.Annulee => "Annulée",
        StatutReservation.Terminee => "Terminée",
        StatutReservation.DemandeEnvoyee => "Demande envoyée à l'hôte",
        StatutReservation.Refusee => "Refusée",
        _ => statut.ToString()
    };

    private static string Montant(decimal? montant) =>
        montant is null ? "-" : $"{montant.Value.ToString("N0", Fr).Replace(' ', ' ').Replace(' ', ' ')} GNF";
    private static string Date(DateTime? date) => date?.ToString("dd/MM/yyyy", Fr) ?? "-";
    private static string DateHeure(DateTime date) => date.ToString("dd/MM/yyyy 'à' HH'h'mm", Fr);
}
