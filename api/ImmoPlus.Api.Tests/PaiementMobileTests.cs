namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>Paiement mobile simulé : initiation, webhook signé et idempotent, interaction avec l'annulation automatique sous 24 h.</summary>
public class PaiementMobileTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private static Task<HttpResponseMessage> Initier(Compte voyageur, int reservationId, string telephone = "+224 620 12 34 56", decimal? montant = null) =>
        voyageur.Client.PostJson("/paiement-mobile/initier", new { reservationId, operateur = "OrangeMoney", telephone, montant });

    private Task<HttpResponseMessage> Webhook(string reference, string statut, string? signature = null)
    {
        var corps = $$"""{"referenceFournisseur":"{{reference}}","statut":"{{statut}}"}""";
        var requete = new HttpRequestMessage(HttpMethod.Post, "/paiement-mobile/webhook/OrangeMoney")
        {
            Content = new StringContent(corps, Encoding.UTF8, "application/json")
        };
        requete.Headers.Add("X-Signature", signature
            ?? Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ImmoPlusFactory.SecretWebhook), Encoding.UTF8.GetBytes(corps))).ToLowerInvariant());
        return f.CreateClient().SendAsync(requete);
    }

    private async Task<(Compte Voyageur, ReservationResponseDto Reservation)> ReservationAsync(bool instantanee = true)
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: instantanee);
        return (voyageur, await voyageur.ReserverOk(logement.Id, 10, 3));
    }

    [Fact]
    public async Task Initiation_controlee_puis_webhook_signe_et_idempotent()
    {
        var (voyageur, reservation) = await ReservationAsync();
        var intrus = await f.LocataireAsync();

        await Initier(voyageur, reservation.Id, "0612345678").Erreur(HttpStatusCode.BadRequest);
        await Initier(voyageur, reservation.Id, montant: reservation.MontantTotal + 1).Erreur(HttpStatusCode.BadRequest);
        await Initier(intrus, reservation.Id).Erreur(HttpStatusCode.Conflict);

        var transaction = await Initier(voyageur, reservation.Id).Attendre<TransactionMobileDto>(HttpStatusCode.Created);
        Assert.Equal("EnAttente", transaction.Statut);
        Assert.Equal(reservation.MontantTotal, transaction.Montant);
        Assert.Equal("6•• •• •• 56", transaction.Telephone);
        Assert.StartsWith("SIM-", transaction.ReferenceFournisseur);
        await Initier(voyageur, reservation.Id).Erreur(HttpStatusCode.Conflict);
        await intrus.Client.GetAsync($"/paiement-mobile/{transaction.Id}").Erreur(HttpStatusCode.Conflict);

        await Webhook(transaction.ReferenceFournisseur!, "SUCCESS", signature: "00ff").Erreur(HttpStatusCode.Unauthorized);
        await Webhook(transaction.ReferenceFournisseur!, "SUCCESS", signature: "").Verifier(HttpStatusCode.Unauthorized);

        // Notifications répétées et simultanées : un seul paiement.
        var reponses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Webhook(transaction.ReferenceFournisseur!, "SUCCESS")));
        Assert.All(reponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await Webhook(transaction.ReferenceFournisseur!, "FAILED").Verifier(HttpStatusCode.OK);

        Assert.Equal("Reussie", (await voyageur.Client.GetAsync($"/paiement-mobile/{transaction.Id}").Attendre<TransactionMobileDto>()).Statut);
        var payee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Confirmee", payee.Statut);
        Assert.Equal(reservation.MontantTotal, payee.MontantPaye);
        var paiement = Assert.Single(await voyageur.Client.GetAsync($"/reservation/{reservation.Id}/paiements").Attendre<List<PaiementReservationDto>>());
        Assert.Equal("Orange Money", paiement.ModePaiement);
        Assert.Equal(transaction.ReferenceFournisseur, paiement.Reference);
    }

    [Fact]
    public async Task Paiement_mobile_refuse_tant_que_l_hote_n_a_pas_accepte()
    {
        var (voyageur, demande) = await ReservationAsync(instantanee: false);
        await Initier(voyageur, demande.Id).Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Echec_simule_puis_nouvelle_tentative()
    {
        var (voyageur, reservation) = await ReservationAsync();
        var transaction = await Initier(voyageur, reservation.Id, montant: 100_000m).Attendre<TransactionMobileDto>(HttpStatusCode.Created);

        var echec = await voyageur.Client.PostJson($"/paiement-mobile/{transaction.Id}/simuler", new { succes = false }).Attendre<TransactionMobileDto>();
        Assert.Equal("Echouee", echec.Statut);
        Assert.Equal("EnAttente", (await voyageur.Client.Reservation(reservation.Id)).Statut);

        var reprise = await Initier(voyageur, reservation.Id, montant: 100_000m).Attendre<TransactionMobileDto>(HttpStatusCode.Created);
        var reussie = await voyageur.Client.PostJson($"/paiement-mobile/{reprise.Id}/simuler", new { succes = true }).Attendre<TransactionMobileDto>();
        Assert.Equal("Reussie", reussie.Statut);
        var partielle = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal(100_000m, partielle.MontantPaye);
        Assert.Equal("EnAttente", partielle.Statut);
    }

    [Fact]
    public async Task Annulation_sous_24_h_suspendue_pendant_un_paiement_mobile_en_cours()
    {
        var (voyageur, reservation) = await ReservationAsync();
        var transaction = await Initier(voyageur, reservation.Id).Attendre<TransactionMobileDto>(HttpStatusCode.Created);
        await f.ModifierBaseAsync(async db =>
            (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).DateCreation = DateTime.UtcNow.AddHours(-25));

        // Le voyageur est en train de valider sur son téléphone : la réservation attend.
        Assert.Equal("EnAttente", (await voyageur.Client.Reservation(reservation.Id)).Statut);

        // Transaction abandonnée (plus de 10 minutes) : la réservation est annulée.
        await f.ModifierBaseAsync(async db =>
            (await db.TransactionsMobiles.SingleAsync(t => t.Id == transaction.Id)).DateCreation = DateTime.UtcNow.AddMinutes(-11));
        var annulee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Annulee", annulee.Statut);
        Assert.Equal("Systeme", annulee.AnnuleePar);

        // Validation tardive par l'opérateur : l'argent est tracé et entièrement à rembourser.
        await Webhook(transaction.ReferenceFournisseur!, "SUCCESS").Verifier(HttpStatusCode.OK);
        var apres = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Annulee", apres.Statut);
        Assert.Equal(reservation.MontantTotal, apres.MontantPaye);
        Assert.Equal(reservation.MontantTotal, apres.MontantARembourser);
    }

    // Trop-perçu : un paiement mobile validé après un règlement complet doit être ajouté au montant à rembourser.
    [Fact]
    public async Task Paiement_mobile_valide_apres_un_reglement_en_especes_trop_percu_a_rembourser()
    {
        var admin = await f.AdminAsync();
        var (voyageur, reservation) = await ReservationAsync();
        var transaction = await Initier(voyageur, reservation.Id).Attendre<TransactionMobileDto>(HttpStatusCode.Created);

        // Pendant la validation sur le téléphone, l'admin enregistre un règlement en espèces du total.
        await admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal).Verifier(HttpStatusCode.OK);
        await Webhook(transaction.ReferenceFournisseur!, "SUCCESS").Verifier(HttpStatusCode.OK);

        var relue = await voyageur.Client.Reservation(reservation.Id);
        var tropPercu = relue.MontantPaye - relue.MontantTotal;
        Assert.True(tropPercu <= 0 || relue.MontantARembourser >= tropPercu,
            $"Payé {relue.MontantPaye} pour un total de {relue.MontantTotal}, à rembourser : {relue.MontantARembourser?.ToString() ?? "rien"}");
    }
}
