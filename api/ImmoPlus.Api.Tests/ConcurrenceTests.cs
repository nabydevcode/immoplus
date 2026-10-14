namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>Doubles clics et opérations simultanées : jamais de doublon ni de montant incohérent.</summary>
public class ConcurrenceTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Deux_acceptations_simultanees_un_seul_dossier_attribue()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.LongueDureeAsync();
        var dossiers = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            var candidat = await f.LocataireAsync();
            var dossier = await candidat.Client.PostJson("/candidature/creer", new { appartementId = logement.Id, apportInitial = 6_000_000m, situationProfessionnelle = "CDI" })
                .Attendre<CandidatureResponseDto>(HttpStatusCode.Created);
            var visite = (await bailleur.Client.PostJson($"/candidature/{dossier.Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(1) })
                .Attendre<CandidatureResponseDto>()).Visites.Single().Id;
            await f.ModifierBaseAsync(async db => (await db.Visites.SingleAsync(v => v.Id == visite)).DateHeure = DateTime.UtcNow.AddHours(-1));
            await bailleur.Client.PostJson($"/visite/{visite}/cloturer", new { effectuee = true }).Verifier(HttpStatusCode.OK);
            dossiers.Add(dossier.Id);
        }

        var reponses = await Task.WhenAll(dossiers.Select(id => bailleur.Client.PostJson($"/candidature/{id}/accepter", new { })));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await f.LireBaseAsync(db => db.Locations.CountAsync(l => l.AppartementId == logement.Id)));
    }

    [Fact]
    public async Task Double_generation_de_l_echeancier_une_seule_serie_de_mensualites()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var logement = await bailleur.LongueDureeAsync(1_000_000m, 6);
        var dossier = await locataire.Client.PostJson("/candidature/creer", new { appartementId = logement.Id, apportInitial = 2_000_000m, situationProfessionnelle = "CDD" })
            .Attendre<CandidatureResponseDto>(HttpStatusCode.Created);
        var visite = (await bailleur.Client.PostJson($"/candidature/{dossier.Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(1) })
            .Attendre<CandidatureResponseDto>()).Visites.Single().Id;
        await f.ModifierBaseAsync(async db => (await db.Visites.SingleAsync(v => v.Id == visite)).DateHeure = DateTime.UtcNow.AddHours(-1));
        await bailleur.Client.PostJson($"/visite/{visite}/cloturer", new { effectuee = true }).Verifier(HttpStatusCode.OK);
        var accepte = await bailleur.Client.PostJson($"/candidature/{dossier.Id}/accepter", new { }).Attendre<CandidatureResponseDto>();
        await admin.Client.PostJson($"/candidature/{dossier.Id}/apport", new { modePaiement = "Espèces", datePaiement = Api.Jour(0) }).Verifier(HttpStatusCode.OK);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            admin.Client.PostJson($"/financement/{accepte.FinancementLocationId}/generer-echeancier")));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.NoContent);
        var mensualites = await locataire.Client.GetAsync($"/financement/{accepte.FinancementLocationId}/mensualites").Attendre<List<MensualiteResponseDto>>();
        // 4 000 000 × 1,03 = 4 120 000 sur 4 mensualités.
        Assert.Equal(4, mensualites.Count);
        Assert.Equal(4_120_000m, mensualites.Sum(m => m.MontantDu));
    }

    [Fact]
    public async Task Annulation_et_paiement_simultanes_le_remboursement_couvre_tout_ce_qui_est_paye()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        for (var essai = 0; essai < 3; essai++)
        {
            var voyageur = await f.LocataireAsync();
            var logement = await bailleur.SejourCourtAsync(politique: "Flexible");
            var reservation = await voyageur.ReserverOk(logement.Id, 30, 2);
            await admin.Client.PayerReservation(reservation.Id, 100_000m).Verifier(HttpStatusCode.OK);

            var annulation = voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler");
            var paiement = admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal - 100_000m);
            await Task.WhenAll(annulation, paiement);

            Assert.Equal(HttpStatusCode.NoContent, annulation.Result.StatusCode);
            Assert.Contains(paiement.Result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var relue = await voyageur.Client.Reservation(reservation.Id);
            Assert.Equal("Annulee", relue.Statut);
            // Politique flexible, annulation 30 jours avant : tout ce qui a été payé est rendu.
            Assert.Equal(relue.MontantPaye, relue.MontantARembourser);
        }
    }

    [Fact]
    public async Task Double_clic_sur_le_remboursement_jamais_plus_que_le_montant_du()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(politique: "Flexible");
        var reservation = await voyageur.ReserverOk(logement.Id, 30, 2);
        await admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal).Verifier(HttpStatusCode.OK);
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Verifier(HttpStatusCode.NoContent);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement",
            new { montant = reservation.MontantTotal, modePaiement = "Espèces", date = Api.Jour(0) })));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(reservation.MontantTotal, (await admin.Client.Reservation(reservation.Id)).MontantRembourse);
    }
}
