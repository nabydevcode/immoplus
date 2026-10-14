namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Politiques d'annulation à chaque palier. Séjour de 4 nuits à 100 000 GNF + 20 000 GNF de ménage = 420 000 GNF, entièrement payé :
/// le ménage est toujours rendu si l'on annule avant l'arrivée (jour de l'arrivée compris).
/// </summary>
public class AnnulationTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private const decimal Total = 420_000m;
    private const decimal Menage = 20_000m;

    private async Task<(Compte Voyageur, Compte Bailleur, ReservationResponseDto Reservation)> SejourPayeAsync(string politique, int arrivee,
        double heuresDepuisReservation = 0)
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(prixParNuit: 100_000m, fraisMenage: Menage, politique: politique);
        var reservation = await voyageur.ReserverOk(logement.Id, arrivee, 4);
        Assert.Equal(Total, reservation.MontantTotal);
        Assert.Equal(politique, reservation.PolitiqueAnnulation);
        await admin.Client.PayerReservation(reservation.Id, Total).Verifier(HttpStatusCode.OK);
        if (heuresDepuisReservation > 0)
        {
            await f.ModifierBaseAsync(async db =>
                (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).DateCreation = DateTime.UtcNow.AddHours(-heuresDepuisReservation));
        }
        return (voyageur, bailleur, reservation);
    }

    [Theory]
    // Flexible : 100 % jusqu'à 1 jour avant, sinon 0 % des nuitées (ménage rendu).
    [InlineData("Flexible", 1, 0, 100, 420_000)]
    [InlineData("Flexible", 0, 0, 0, 20_000)]
    // Modérée : 100 % à 5 jours ou plus, 50 % des nuitées entre 1 et 5 jours, sinon 0 %.
    [InlineData("Moderee", 5, 0, 100, 420_000)]
    [InlineData("Moderee", 4, 0, 50, 220_000)]
    [InlineData("Moderee", 1, 0, 50, 220_000)]
    [InlineData("Moderee", 0, 0, 0, 20_000)]
    // Stricte : 100 % dans les 48 h suivant la réservation ET à 14 jours ou plus ; 50 % à 7 jours ou plus ; sinon 0 %.
    [InlineData("Stricte", 14, 0, 100, 420_000)]
    [InlineData("Stricte", 14, 47, 100, 420_000)]
    [InlineData("Stricte", 14, 49, 50, 220_000)]
    [InlineData("Stricte", 13, 0, 50, 220_000)]
    [InlineData("Stricte", 7, 0, 50, 220_000)]
    [InlineData("Stricte", 6, 0, 0, 20_000)]
    public async Task Remboursement_du_voyageur_selon_la_politique_et_le_delai(string politique, int joursAvantArrivee,
        double heuresDepuisReservation, int pourcentageAttendu, int remboursementAttendu)
    {
        var (voyageur, _, reservation) = await SejourPayeAsync(politique, joursAvantArrivee, heuresDepuisReservation);

        var apercu = await voyageur.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Attendre<ApercuAnnulationDto>();
        Assert.Equal(politique, apercu.Politique);
        Assert.Equal(Total, apercu.MontantPaye);
        Assert.Equal(pourcentageAttendu, apercu.PourcentageNuitees);
        Assert.Equal(remboursementAttendu, apercu.MontantRembourse);

        // L'annulation enregistre exactement le montant annoncé par l'aperçu.
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler", new { motif = "Changement de programme" })
            .Verifier(HttpStatusCode.NoContent);
        var annulee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Annulee", annulee.Statut);
        Assert.Equal("Voyageur", annulee.AnnuleePar);
        Assert.Equal(remboursementAttendu, annulee.MontantARembourser);
        Assert.Equal("Changement de programme", annulee.MotifAnnulation);
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Annulation_par_l_hote_toujours_remboursee_integralement()
    {
        var (voyageur, bailleur, reservation) = await SejourPayeAsync("Stricte", 2);

        var apercuHote = await bailleur.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Attendre<ApercuAnnulationDto>();
        Assert.Equal(100, apercuHote.PourcentageNuitees);
        var apercuVoyageur = await voyageur.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Attendre<ApercuAnnulationDto>();
        Assert.Equal(Menage, apercuVoyageur.MontantRembourse);

        // Corps vide accepté.
        await bailleur.Client.PostAsync($"/reservation/{reservation.Id}/annuler", null).Verifier(HttpStatusCode.NoContent);
        var annulee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Hote", annulee.AnnuleePar);
        Assert.Equal(Total, annulee.MontantARembourser);
    }

    [Fact]
    public async Task Annulation_par_l_admin_pour_le_compte_de_l_hote_remboursee_integralement()
    {
        var admin = await f.AdminAsync();
        var (voyageur, _, reservation) = await SejourPayeAsync("Moderee", 0);

        Assert.Equal(Total, (await admin.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Attendre<ApercuAnnulationDto>()).MontantRembourse);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/annuler", new { motif = "Logement inondé" }).Verifier(HttpStatusCode.NoContent);
        var annulee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Admin", annulee.AnnuleePar);
        Assert.Equal(Total, annulee.MontantARembourser);
    }

    [Fact]
    public async Task Sejour_commence_le_voyageur_ne_peut_plus_annuler()
    {
        var (voyageur, bailleur, reservation) = await SejourPayeAsync("Flexible", 3);
        await f.ModifierBaseAsync(async db =>
        {
            var r = await db.Reservations.SingleAsync(x => x.Id == reservation.Id);
            r.DateArrivee = Api.Aujourdhui.AddDays(-1);
            r.DateDepart = Api.Aujourdhui.AddDays(3);
        });
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Annulation_sans_paiement_ne_rembourse_rien_et_un_tiers_ne_peut_pas_annuler()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var intrus = await f.LocataireAsync();
        var autreBailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync(politique: "Flexible");
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);

        await intrus.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
        await autreBailleur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
        await intrus.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Erreur(HttpStatusCode.Conflict);

        var apercu = await voyageur.Client.GetAsync($"/reservation/{reservation.Id}/annulation/apercu").Attendre<ApercuAnnulationDto>();
        Assert.Equal(0m, apercu.MontantPaye);
        Assert.Equal(0m, apercu.MontantRembourse);
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Verifier(HttpStatusCode.NoContent);
        Assert.Equal(0m, (await voyageur.Client.Reservation(reservation.Id)).MontantARembourser);
    }

    [Fact]
    public async Task Remboursement_enregistre_par_l_admin_plafonne_au_montant_du()
    {
        var admin = await f.AdminAsync();
        var (voyageur, _, reservation) = await SejourPayeAsync("Moderee", 3);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", new { montant = 1000, modePaiement = "Espèces", date = Api.Jour(0) })
            .Erreur(HttpStatusCode.Conflict); // pas encore annulée

        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Verifier(HttpStatusCode.NoContent);
        object Remboursement(decimal montant, string? date = null) => new { montant, modePaiement = "Orange Money", reference = "RB-1", date = date ?? Api.Jour(0) };

        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(1000)).Verifier(HttpStatusCode.Forbidden);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(220_001)).Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(1000, Api.Jour(1))).Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(120_000)).Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(100_001)).Erreur(HttpStatusCode.BadRequest);
        var rembourse = await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(100_000))
            .Attendre<ReservationResponseDto>();
        Assert.Equal(220_000m, rembourse.MontantRembourse);
        await admin.Client.PostJson($"/reservation/{reservation.Id}/remboursement", Remboursement(1)).Erreur(HttpStatusCode.Conflict);
    }
}
