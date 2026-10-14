namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Désactivation d'un hôte par l'admin et suppression (désactivation) d'une annonce par son bailleur.</summary>
public class DesactivationTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Hote_desactive_reservations_non_confirmees_annulees_et_annonces_masquees()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var instantane = await bailleur.SejourCourtAsync();
        var surDemande = await bailleur.SejourCourtAsync(instantanee: false);

        var demande = await voyageur.ReserverOk(surDemande.Id, 10, 2);
        var acompte = await voyageur.ReserverOk(instantane.Id, 10, 2);
        var confirmee = await voyageur.ReserverOk(instantane.Id, 20, 2);
        await admin.Client.PayerReservation(acompte.Id, 100_000m).Verifier(HttpStatusCode.OK);
        await admin.Client.PayerReservation(confirmee.Id, confirmee.MontantTotal).Verifier(HttpStatusCode.OK);

        var resultat = await admin.Client.PostJson($"/utilisateur/{bailleur.Id}/desactiver", new { motif = "Annonces frauduleuses" })
            .Attendre<UtilisateurResponseDto>();

        Assert.Equal("Desactive", resultat.Statut);
        Assert.Equal(2, resultat.ReservationsAnnulees);
        Assert.Equal(1, resultat.ReservationsConfirmeesAVenir);

        var reservations = await voyageur.Client.GetAsync("/reservation/lister").Attendre<List<ReservationResponseDto>>();
        foreach (var (id, payé) in new[] { (demande.Id, 0m), (acompte.Id, 100_000m) })
        {
            var r = reservations.Single(x => x.Id == id);
            Assert.Equal("Annulee", r.Statut);
            Assert.Equal("Admin", r.AnnuleePar);
            Assert.Equal("Compte de l'hôte désactivé", r.MotifAnnulation);
            Assert.Equal(payé, r.MontantARembourser);
        }
        Assert.Equal("Confirmee", reservations.Single(x => x.Id == confirmee.Id).Statut);
        Assert.Contains(f.Smtp.EmailsPour(voyageur.Email), e => e.Sujet.Contains($"{acompte.Id:D6}"));

        // Le bailleur est déconnecté, ses annonces disparaissent pour le public et ne se réservent plus.
        await bailleur.Client.GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.Unauthorized);
        var publiques = await f.CreateClient().GetAsync("/appartement/lister").Attendre<List<AppartementResponseDto>>();
        Assert.DoesNotContain(publiques, a => a.BailleurId == bailleur.Id);
        await f.CreateClient().GetAsync($"/appartement/{instantane.Id}").Verifier(HttpStatusCode.NotFound);
        await admin.Client.GetAsync($"/appartement/{instantane.Id}").Verifier(HttpStatusCode.OK);
        var devis = await f.CreateClient().GetAsync($"/reservation/devis?appartementId={instantane.Id}&arrivee={Api.Jour(40)}&depart={Api.Jour(42)}")
            .Attendre<DevisReservationDto>();
        Assert.False(devis.Disponible);
        await voyageur.Reserver(instantane.Id, 40, 2).Erreur(HttpStatusCode.Conflict);

        // L'admin peut ensuite rembourser l'acompte.
        await admin.Client.PostJson($"/reservation/{acompte.Id}/remboursement", new { montant = 100_000m, modePaiement = "Espèces", date = Api.Jour(0) })
            .Verifier(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Annonce_supprimee_masquee_puis_reactivee()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);

        // Une réservation à venir empêche la suppression.
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Erreur(HttpStatusCode.Conflict);
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Verifier(HttpStatusCode.NoContent);
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Verifier(HttpStatusCode.NoContent);
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Erreur(HttpStatusCode.Conflict);

        Assert.DoesNotContain(await f.CreateClient().GetAsync("/appartement/lister").Attendre<List<AppartementResponseDto>>(), a => a.Id == logement.Id);
        await f.CreateClient().GetAsync($"/appartement/{logement.Id}").Verifier(HttpStatusCode.NotFound);
        Assert.True((await bailleur.Client.GetAsync($"/appartement/{logement.Id}").Attendre<AppartementResponseDto>()).EstDesactive);
        await voyageur.Reserver(logement.Id, 20, 2).Erreur(HttpStatusCode.Conflict);

        await bailleur.Client.PostJson($"/appartement/{logement.Id}/reactiver").Verifier(HttpStatusCode.OK);
        Assert.Contains(await f.CreateClient().GetAsync("/appartement/lister").Attendre<List<AppartementResponseDto>>(), a => a.Id == logement.Id);
        await voyageur.ReserverOk(logement.Id, 20, 2);
    }

    [Fact]
    public async Task Candidature_impossible_sur_une_annonce_supprimee()
    {
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var logement = await bailleur.LongueDureeAsync();
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Verifier(HttpStatusCode.NoContent);

        await locataire.Client.PostJson("/candidature/creer", new { appartementId = logement.Id, apportInitial = 6_000_000m, situationProfessionnelle = "CDI" })
            .Erreur(HttpStatusCode.Conflict);
    }
}
