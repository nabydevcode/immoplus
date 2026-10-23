namespace ImmoPlus.Api.Tests;

using System.Net;
using ClosedXML.Excel;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Journal des actions (traçabilité des décisions de l'admin) et exports Excel réservés à l'admin.</summary>
public class JournalExportTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Desactivation_et_connexions_tracees_dans_le_journal()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();

        await f.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = "mauvais-secret-123" })
            .Verifier(HttpStatusCode.NotFound);
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Fraude constatée" }).Verifier(HttpStatusCode.OK);

        await locataire.Client.GetAsync("/admin/journal").Verifier(HttpStatusCode.Unauthorized);
        var autre = await f.LocataireAsync();
        await autre.Client.GetAsync("/admin/journal").Verifier(HttpStatusCode.Forbidden);

        var desactivation = await admin.Client.GetAsync($"/admin/journal?action=utilisateur.desactiver&cible=Utilisateur&cibleId={locataire.Id}")
            .Attendre<PagedResultDto<JournalActionDto>>();
        var entree = Assert.Single(desactivation.Donnees);
        Assert.Equal(admin.Id, entree.UtilisateurId);
        Assert.Equal("Admin", entree.Role);
        Assert.Contains("Fraude constatée", entree.Details);
        Assert.True(entree.Date > DateTime.UtcNow.AddMinutes(-5));

        var connexions = await admin.Client.GetAsync($"/admin/journal?action=connexion&cibleId={locataire.Id}").Attendre<PagedResultDto<JournalActionDto>>();
        Assert.Contains(connexions.Donnees, j => j.Action == "connexion.reussie");
        var echec = Assert.Single(connexions.Donnees, j => j.Action == "connexion.echec");
        Assert.DoesNotContain("mauvais-secret-123", echec.Details);

        // Recherche libre, période, pagination, plus récent d'abord.
        var recherche = await admin.Client.GetAsync($"/admin/journal?q=fraude&du={Api.Jour(0)}&au={Api.Jour(0)}&taille=1")
            .Attendre<PagedResultDto<JournalActionDto>>();
        Assert.Equal(1, recherche.TotalElements);
        Assert.Empty(await admin.Client.GetAsync($"/admin/journal?q=fraude&du={Api.Jour(1)}").AttendreDonnees<JournalActionDto>());
        var tout = await admin.Client.GetAsync("/admin/journal?taille=200").Attendre<PagedResultDto<JournalActionDto>>();
        Assert.Equal(tout.Donnees.OrderByDescending(j => j.Date).ThenByDescending(j => j.Id).Select(j => j.Id), tout.Donnees.Select(j => j.Id));
        await admin.Client.GetAsync("/admin/journal?du=14-10-2026").Verifier(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_des_paiements_en_xlsx_valide_et_reserve_a_l_admin()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        await admin.Client.PayerReservation(reservation.Id, 123_456m).Verifier(HttpStatusCode.OK);

        await bailleur.Client.GetAsync("/admin/exports/paiements.xlsx").Verifier(HttpStatusCode.Forbidden);
        await f.CreateClient().GetAsync("/admin/exports/paiements.xlsx").Verifier(HttpStatusCode.Unauthorized);
        await admin.Client.GetAsync("/admin/exports/inconnu.xlsx").Verifier(HttpStatusCode.NotFound);

        var reponse = await admin.Client.GetAsync($"/admin/exports/paiements.xlsx?du={Api.Jour(-1)}&au={Api.Jour(0)}").Verifier(HttpStatusCode.OK);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", reponse.Content.Headers.ContentType!.MediaType);
        using var classeur = new XLWorkbook(await reponse.Content.ReadAsStreamAsync());
        var feuille = classeur.Worksheet(1);
        Assert.Equal("Réf. paiement", feuille.Cell(1, 1).GetString());
        Assert.Equal("Montant", feuille.Cell(1, 3).GetString());
        Assert.Equal(1, feuille.SheetView.SplitRow);
        Assert.True(feuille.AutoFilter.IsEnabled);
        var ligne = feuille.RowsUsed().Skip(1).Single(r => r.Cell(7).GetString() == $"Réservation {reservation.Id:D6}");
        Assert.Equal(123_456d, ligne.Cell(3).GetDouble());
        Assert.Contains("GNF", ligne.Cell(3).Style.NumberFormat.Format);
        Assert.Equal(Api.Aujourdhui, ligne.Cell(2).GetDateTime().Date);
        Assert.Equal("dd/mm/yyyy", ligne.Cell(2).Style.DateFormat.Format);
        Assert.Matches(@"^\d{6}$", ligne.Cell(1).GetString());

        // Période sans paiement : en-têtes seuls. Tous les types s'ouvrent.
        var vide = await admin.Client.GetAsync($"/admin/exports/paiements.xlsx?du={Api.Jour(5)}").Verifier(HttpStatusCode.OK);
        using (var classeurVide = new XLWorkbook(await vide.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(1, classeurVide.Worksheet(1).LastRowUsed()!.RowNumber());
        }
        foreach (var type in new[] { "loyers", "mensualites", "reservations", "utilisateurs", "locations" })
        {
            var fichier = await admin.Client.GetAsync($"/admin/exports/{type}.xlsx").Verifier(HttpStatusCode.OK);
            using var c = new XLWorkbook(await fichier.Content.ReadAsStreamAsync());
            Assert.False(c.Worksheet(1).Cell(1, 1).IsEmpty());
        }
        var journal = await admin.Client.GetAsync("/admin/journal?action=export").Attendre<PagedResultDto<JournalActionDto>>();
        Assert.True(journal.TotalElements >= 7);
    }
}
