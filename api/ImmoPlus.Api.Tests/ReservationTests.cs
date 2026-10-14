namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models.Enums;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>Cycle de vie d'une réservation : demande, réponse de l'hôte, paiements, expirations automatiques, fin de séjour.</summary>
public class ReservationTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Demande_acceptee_par_l_hote_puis_payee_en_deux_fois()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var autreBailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: false);

        var demande = await voyageur.ReserverOk(logement.Id, 10, 3, voyageurs: 2);
        Assert.Equal("DemandeEnvoyee", demande.Statut);
        Assert.Equal(2, demande.NombreVoyageurs);
        Assert.Equal(320_000m, demande.MontantTotal);
        Assert.Null(demande.PayerAvant);
        Assert.Contains(f.Smtp.EmailsPour(bailleur.Email), e => e.Sujet.Contains($"Réservation n° {demande.Id:D6}"));

        // Pas de paiement tant que l'hôte n'a pas accepté.
        await admin.Client.PayerReservation(demande.Id, 320_000m).Erreur(HttpStatusCode.BadRequest);
        // Seul l'hôte du logement (ou l'admin) répond ; le voyageur n'a pas le rôle.
        await autreBailleur.Client.PostJson($"/reservation/{demande.Id}/accepter").Erreur(HttpStatusCode.Conflict);
        await voyageur.Client.PostJson($"/reservation/{demande.Id}/accepter").Verifier(HttpStatusCode.Forbidden);

        var acceptee = await bailleur.Client.PostJson($"/reservation/{demande.Id}/accepter").Attendre<ReservationResponseDto>();
        Assert.Equal("EnAttente", acceptee.Statut);
        Assert.NotNull(acceptee.DateReponseHote);
        Assert.NotNull(acceptee.PayerAvant);
        await bailleur.Client.PostJson($"/reservation/{demande.Id}/accepter").Erreur(HttpStatusCode.Conflict);

        // Montant supérieur au reste dû, date future, montant nul : refusés.
        await admin.Client.PayerReservation(demande.Id, 320_001m).Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PayerReservation(demande.Id, 100_000m, Api.Jour(1)).Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PayerReservation(demande.Id, 0m).Erreur(HttpStatusCode.BadRequest);
        await voyageur.Client.PayerReservation(demande.Id, 100_000m).Verifier(HttpStatusCode.Forbidden);

        await admin.Client.PayerReservation(demande.Id, 120_000m).Verifier(HttpStatusCode.OK);
        var partielle = await voyageur.Client.Reservation(demande.Id);
        Assert.Equal("EnAttente", partielle.Statut);
        Assert.Equal(120_000m, partielle.MontantPaye);
        Assert.Null(partielle.PayerAvant);

        await admin.Client.PayerReservation(demande.Id, 200_001m).Erreur(HttpStatusCode.BadRequest);
        await bailleur.Client.PayerReservation(demande.Id, 200_000m).Verifier(HttpStatusCode.OK);
        var confirmee = await voyageur.Client.Reservation(demande.Id);
        Assert.Equal("Confirmee", confirmee.Statut);
        Assert.Equal(320_000m, confirmee.MontantPaye);
        // Adresse exacte communiquée une fois le séjour confirmé.
        Assert.NotNull(confirmee.AdresseComplete);

        await admin.Client.PayerReservation(demande.Id, 1m).Erreur(HttpStatusCode.Conflict);
        var paiements = await voyageur.Client.GetAsync($"/reservation/{demande.Id}/paiements").Attendre<List<PaiementReservationDto>>();
        Assert.Equal(2, paiements.Count);
    }

    [Fact]
    public async Task Demande_refusee_par_l_hote_avec_motif()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: false);
        var demande = await voyageur.ReserverOk(logement.Id, 10, 3);

        var refusee = await bailleur.Client.PostJson($"/reservation/{demande.Id}/refuser", new { motif = "Travaux prévus" })
            .Attendre<ReservationResponseDto>();
        Assert.Equal("Refusee", refusee.Statut);
        Assert.Equal("Travaux prévus", refusee.MotifRefus);
        await admin.Client.PayerReservation(demande.Id, 100_000m).Erreur(HttpStatusCode.BadRequest);
        await voyageur.Client.PostJson($"/reservation/{demande.Id}/annuler").Erreur(HttpStatusCode.Conflict);

        // Les dates sont libérées.
        await voyageur.ReserverOk(logement.Id, 10, 3);
    }

    [Fact]
    public async Task Deux_paiements_simultanes_du_solde_un_seul_est_accepte()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 20, 3);
        Assert.Equal("EnAttente", reservation.Statut);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal)));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(reponses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        var relue = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal(reservation.MontantTotal, relue.MontantPaye);
        Assert.Equal("Confirmee", relue.Statut);
    }

    [Fact]
    public async Task Deux_demandes_simultanees_sur_les_memes_dates_une_seule_aboutit()
    {
        var bailleur = await f.BailleurAsync();
        var voyageurs = new[] { await f.LocataireAsync(), await f.LocataireAsync(), await f.LocataireAsync() };
        var logement = await bailleur.SejourCourtAsync();

        var reponses = await Task.WhenAll(voyageurs.Select(v => v.Reserver(logement.Id, 30, 3)));

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(reponses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Reservation_instantanee_impayee_annulee_apres_24_h()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var autre = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 15, 3);
        Assert.Equal("EnAttente", reservation.Statut);
        Assert.NotNull(reservation.PayerAvant);
        Assert.InRange(reservation.PayerAvant!.Value, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));

        // 23 h après : toujours en attente.
        await ReculerCreation(reservation.Id, TimeSpan.FromHours(23));
        Assert.Equal("EnAttente", (await voyageur.Client.Reservation(reservation.Id)).Statut);

        await ReculerCreation(reservation.Id, TimeSpan.FromHours(25));
        var annulee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Annulee", annulee.Statut);
        Assert.Equal("Systeme", annulee.AnnuleePar);
        Assert.Equal("Paiement non reçu sous 24 h", annulee.MotifAnnulation);
        Assert.Equal(0m, annulee.MontantARembourser);
        Assert.Null(annulee.PayerAvant);
        Assert.Contains(f.Smtp.EmailsPour(voyageur.Email), e => e.Sujet.Contains($"n° {reservation.Id:D6}") && e.Sujet.Contains("annul", StringComparison.OrdinalIgnoreCase));

        // Les dates sont de nouveau réservables.
        await autre.ReserverOk(logement.Id, 15, 3);
    }

    [Fact]
    public async Task Demande_acceptee_impayee_annulee_24_h_apres_l_acceptation_sauf_paiement_partiel()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: false);

        var sansPaiement = await voyageur.ReserverOk(logement.Id, 10, 2);
        var avecAcompte = await voyageur.ReserverOk(logement.Id, 20, 2);
        foreach (var r in new[] { sansPaiement, avecAcompte })
        {
            await bailleur.Client.PostJson($"/reservation/{r.Id}/accepter").Verifier(HttpStatusCode.OK);
        }
        await admin.Client.PayerReservation(avecAcompte.Id, 50_000m).Verifier(HttpStatusCode.OK);

        // Demande créée il y a 3 jours mais acceptée il y a 2 h : le délai court depuis l'acceptation.
        await f.ModifierBaseAsync(async db =>
        {
            var r = await db.Reservations.SingleAsync(x => x.Id == sansPaiement.Id);
            r.DateCreation = DateTime.UtcNow.AddDays(-3);
            r.DateReponseHote = DateTime.UtcNow.AddHours(-2);
        });
        Assert.Equal("EnAttente", (await voyageur.Client.Reservation(sansPaiement.Id)).Statut);

        await f.ModifierBaseAsync(async db =>
        {
            foreach (var r in await db.Reservations.Where(x => x.Id == sansPaiement.Id || x.Id == avecAcompte.Id).ToListAsync())
            {
                r.DateReponseHote = DateTime.UtcNow.AddHours(-25);
            }
        });
        var reservations = await voyageur.Client.GetAsync("/reservation/lister").Attendre<List<ReservationResponseDto>>();
        Assert.Equal("Annulee", reservations.Single(r => r.Id == sansPaiement.Id).Statut);
        Assert.Equal("EnAttente", reservations.Single(r => r.Id == avecAcompte.Id).Statut);
    }

    [Fact]
    public async Task Demande_sans_reponse_refusee_apres_48_h_par_les_rappels()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: false);
        var recente = await voyageur.ReserverOk(logement.Id, 10, 2);
        var ancienne = await voyageur.ReserverOk(logement.Id, 20, 2);
        await ReculerCreation(recente.Id, TimeSpan.FromHours(47));
        await ReculerCreation(ancienne.Id, TimeSpan.FromHours(49));

        var resultat = await admin.Client.PostJson("/rappels/executer").Attendre<RappelsResultatDto>();

        Assert.True(resultat.DemandesReservationExpirees >= 1);
        Assert.Equal("DemandeEnvoyee", (await voyageur.Client.Reservation(recente.Id)).Statut);
        var refusee = await voyageur.Client.Reservation(ancienne.Id);
        Assert.Equal("Refusee", refusee.Statut);
        Assert.Equal("Sans réponse de l'hôte sous 48 h", refusee.MotifRefus);
        await voyageur.Client.PostJson("/rappels/executer").Verifier(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sejour_confirme_termine_le_jour_du_depart_avec_invitation_a_laisser_un_avis()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 5, 3);
        var nonPayee = await voyageur.ReserverOk(logement.Id, 20, 3);
        await admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal).Verifier(HttpStatusCode.OK);

        // Séjour arrivé il y a 3 jours, départ aujourd'hui.
        await f.ModifierBaseAsync(async db =>
        {
            var r = await db.Reservations.SingleAsync(x => x.Id == reservation.Id);
            r.DateArrivee = Api.Aujourdhui.AddDays(-3);
            r.DateDepart = Api.Aujourdhui;
        });

        var terminee = await voyageur.Client.Reservation(reservation.Id);
        Assert.Equal("Terminee", terminee.Statut);
        Assert.Equal(DateOnly.FromDateTime(Api.Aujourdhui.AddDays(14)), terminee.AvisPossibleJusquau);
        Assert.Contains(f.Smtp.EmailsPour(voyageur.Email), e => e.Sujet.Contains("Séjour terminé") && e.Sujet.Contains($"{reservation.Id:D6}"));
        Assert.Contains(f.Smtp.EmailsPour(bailleur.Email), e => e.Sujet.Contains("Séjour terminé") && e.Sujet.Contains($"{reservation.Id:D6}"));
        Assert.Equal("EnAttente", (await voyageur.Client.Reservation(nonPayee.Id)).Statut);

        // Une seule fois : relire ne renvoie pas d'email.
        var avant = f.Smtp.EmailsPour(voyageur.Email).Count(e => e.Sujet.Contains("Séjour terminé"));
        await voyageur.Client.GetAsync("/reservation/lister").Verifier(HttpStatusCode.OK);
        Assert.Equal(avant, f.Smtp.EmailsPour(voyageur.Email).Count(e => e.Sujet.Contains("Séjour terminé")));

        // Un séjour terminé ne s'annule plus et accepte un avis du voyageur.
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Erreur(HttpStatusCode.Conflict);
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/avis", new { note = 5, commentaire = "Très bon séjour, merci !" })
            .Verifier(HttpStatusCode.Created);
    }

    private Task ReculerCreation(int reservationId, TimeSpan duree) =>
        f.ModifierBaseAsync(async db =>
        {
            var r = await db.Reservations.SingleAsync(x => x.Id == reservationId);
            r.DateCreation = DateTime.UtcNow - duree;
        });
}
