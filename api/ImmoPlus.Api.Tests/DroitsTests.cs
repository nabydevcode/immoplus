namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>Qui voit et fait quoi : voyageur / locataire, hôte / bailleur, admin, autre utilisateur et visiteur anonyme.</summary>
public class DroitsTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Reservation_visible_du_voyageur_de_l_hote_et_de_l_admin_seulement()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var autreLocataire = await f.LocataireAsync();
        var autreBailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);

        await voyageur.Client.GetAsync($"/reservation/{reservation.Id}").Verifier(HttpStatusCode.OK);
        await bailleur.Client.GetAsync($"/reservation/{reservation.Id}").Verifier(HttpStatusCode.OK);
        await admin.Client.GetAsync($"/reservation/{reservation.Id}").Verifier(HttpStatusCode.OK);
        await autreLocataire.Client.GetAsync($"/reservation/{reservation.Id}").Erreur(HttpStatusCode.Conflict);
        await autreBailleur.Client.GetAsync($"/reservation/{reservation.Id}").Erreur(HttpStatusCode.Conflict);
        await f.CreateClient().GetAsync($"/reservation/{reservation.Id}").Verifier(HttpStatusCode.Unauthorized);
        await autreLocataire.Client.GetAsync($"/reservation/{reservation.Id}/paiements").Erreur(HttpStatusCode.Conflict);

        Assert.DoesNotContain(await autreLocataire.Client.GetAsync("/reservation/lister").AttendreDonnees<ReservationResponseDto>(),
            r => r.Id == reservation.Id);
        Assert.DoesNotContain(await autreBailleur.Client.GetAsync("/reservation/lister").AttendreDonnees<ReservationResponseDto>(),
            r => r.Id == reservation.Id);
        Assert.Contains(await bailleur.Client.GetAsync("/reservation/lister").AttendreDonnees<ReservationResponseDto>(),
            r => r.Id == reservation.Id);

        // Avant confirmation, le voyageur ne voit que le quartier et la ville ; l'hôte voit l'adresse exacte.
        Assert.DoesNotContain("rue des Tests", (await voyageur.Client.Reservation(reservation.Id)).AppartementAdresse);
        Assert.Contains("rue des Tests", (await bailleur.Client.Reservation(reservation.Id)).AppartementAdresse);

        // Rôles : un bailleur ne réserve pas, un locataire n'enregistre pas de paiement.
        await bailleur.Reserver(logement.Id, 20, 2).Verifier(HttpStatusCode.Forbidden);
        await voyageur.Client.PayerReservation(reservation.Id, 1000m).Verifier(HttpStatusCode.Forbidden);
        await autreBailleur.Client.PayerReservation(reservation.Id, 1000m).Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Messagerie_reservee_au_voyageur_et_a_l_hote_admin_en_lecture()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var intrus = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        var url = $"/reservation/{reservation.Id}/messages";

        var message = await voyageur.Client.PostJson(url, new { contenu = "Bonjour, à quelle heure puis-je arriver ?" })
            .Attendre<MessageDto>(HttpStatusCode.Created);
        Assert.True(message.DeMoi);
        await admin.Client.PostJson(url, new { contenu = "Message de l'admin" }).Erreur(HttpStatusCode.Conflict);
        await intrus.Client.PostJson(url, new { contenu = "Intrusion" }).Erreur(HttpStatusCode.Conflict);
        await intrus.Client.GetAsync(url).Erreur(HttpStatusCode.Conflict);
        await voyageur.Client.PostJson(url, new { contenu = "   " }).Erreur(HttpStatusCode.BadRequest);

        // Pas de coordonnées avant confirmation.
        var refus = await voyageur.Client.PostJson(url, new { contenu = "Appelez-moi au 622 12 34 56" }).Erreur(HttpStatusCode.BadRequest);
        Assert.Equal("Les coordonnées sont partagées après confirmation de la réservation.", refus);
        await bailleur.Client.PostJson(url, new { contenu = "Écrivez à hote@exemple.com" }).Erreur(HttpStatusCode.BadRequest);

        // L'admin lit sans marquer comme lu.
        var lusParAdmin = await admin.Client.GetAsync(url).AttendreDonnees<MessageDto>();
        Assert.Single(lusParAdmin);
        var nonLus = await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>();
        Assert.Equal(1, nonLus.Total);
        Assert.Contains(nonLus.ParReservation, n => n.ReservationId == reservation.Id && n.Nombre == 1);

        var lusParHote = await bailleur.Client.GetAsync(url).AttendreDonnees<MessageDto>();
        Assert.False(lusParHote[0].DeMoi);
        Assert.Equal(0, (await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);
        Assert.NotNull((await voyageur.Client.GetAsync(url).AttendreDonnees<MessageDto>())[0].DateLecture);

        await bailleur.Client.PostJson(url, new { contenu = "Arrivée à partir de 14 h." }).Verifier(HttpStatusCode.Created);
        Assert.Contains(await f.EmailsPourAsync(bailleur.Email), e => e.Sujet.Contains($"{reservation.Id:D6}") && e.Sujet.Contains("message", StringComparison.OrdinalIgnoreCase));

        // Une fois le séjour payé, les coordonnées passent.
        await admin.Client.PayerReservation(reservation.Id, reservation.MontantTotal).Verifier(HttpStatusCode.OK);
        await voyageur.Client.PostJson(url, new { contenu = "Mon numéro : 622 12 34 56" }).Verifier(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Conversation_close_30_jours_apres_l_annulation()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        var url = $"/reservation/{reservation.Id}/messages";
        await voyageur.Client.PostJson($"/reservation/{reservation.Id}/annuler").Verifier(HttpStatusCode.NoContent);

        await voyageur.Client.PostJson(url, new { contenu = "Désolé pour l'annulation." }).Verifier(HttpStatusCode.Created);
        await f.ModifierBaseAsync(async db =>
            (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).DateAnnulation = DateTime.UtcNow.AddDays(-31));
        await bailleur.Client.PostJson(url, new { contenu = "Pas de souci." }).Erreur(HttpStatusCode.Conflict);
        // Le fil reste lisible.
        Assert.Single(await bailleur.Client.GetAsync(url).AttendreDonnees<MessageDto>());
    }

    [Fact]
    public async Task Documents_du_dossier_visibles_du_candidat_du_bailleur_concerne_et_de_l_admin()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var autreBailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var intrus = await f.LocataireAsync();
        var logement = await bailleur.LongueDureeAsync();
        var candidature = await locataire.Client.PostJson("/candidature/creer", new
        {
            appartementId = logement.Id, apportInitial = 6_000_000m, situationProfessionnelle = "CDI"
        }).Attendre<CandidatureResponseDto>(HttpStatusCode.Created);
        var url = $"/candidature/{candidature.Id}/documents";

        var document = await locataire.Client.PostAsync(url, Api.Formulaire("piece.pdf", Api.Pdf,
            ("proprietaire", "Locataire"), ("typeDocument", "PieceIdentite"))).Attendre<DocumentResponseDto>(HttpStatusCode.Created);
        await intrus.Client.PostAsync(url, Api.Formulaire("piece.pdf", Api.Pdf, ("proprietaire", "Locataire"), ("typeDocument", "Autre")))
            .Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostAsync(url, Api.Formulaire("piece.pdf", Api.Pdf, ("proprietaire", "Locataire"), ("typeDocument", "Autre")))
            .Verifier(HttpStatusCode.Forbidden);
        // Un faux PDF (contenu HTML) est refusé.
        await locataire.Client.PostAsync(url, Api.Formulaire("faux.pdf", "<html>piège</html>"u8.ToArray(),
            ("proprietaire", "Locataire"), ("typeDocument", "Autre"))).Erreur(HttpStatusCode.BadRequest);

        var telecharger = $"/document/{document.Id}/telecharger";
        foreach (var autorise in new[] { locataire, bailleur, admin })
        {
            var reponse = await autorise.Client.GetAsync(telecharger).Verifier(HttpStatusCode.OK);
            Assert.Equal(Api.Pdf, await reponse.Content.ReadAsByteArrayAsync());
        }
        await intrus.Client.GetAsync(telecharger).Erreur(HttpStatusCode.Conflict);
        await autreBailleur.Client.GetAsync(telecharger).Erreur(HttpStatusCode.Conflict);
        await f.CreateClient().GetAsync(telecharger).Verifier(HttpStatusCode.Unauthorized);
        await intrus.Client.GetAsync(url).Erreur(HttpStatusCode.Conflict);
        await autreBailleur.Client.GetAsync($"/candidature/{candidature.Id}").Erreur(HttpStatusCode.Conflict);
        Assert.Single(await bailleur.Client.GetAsync(url).Attendre<List<DocumentResponseDto>>());
    }

    [Fact]
    public async Task Photos_d_une_annonce_supprimee_ou_d_un_hote_desactive_protegees()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var autreBailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync();

        await autreBailleur.Client.PostAsync($"/appartement/{logement.Id}/photos", Api.Formulaire("salon.png", Api.Png))
            .Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostAsync($"/appartement/{logement.Id}/photos", Api.Formulaire("faux.png", "GIF89a-pas-un-png"u8.ToArray()))
            .Erreur(HttpStatusCode.BadRequest);
        var photo = await bailleur.Client.PostAsync($"/appartement/{logement.Id}/photos", Api.Formulaire("salon.png", Api.Png))
            .Attendre<PhotoAppartementDto>(HttpStatusCode.Created);
        var url = $"/appartement/photo/{photo.Id}";
        var reponse = await f.CreateClient().GetAsync(url).Verifier(HttpStatusCode.OK);
        Assert.Equal("nosniff", reponse.Headers.GetValues("X-Content-Type-Options").Single());

        // Annonce supprimée : 404 pour le public et les autres comptes, servie au propriétaire et à l'admin.
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Verifier(HttpStatusCode.NoContent);
        await f.CreateClient().GetAsync(url).Verifier(HttpStatusCode.NotFound);
        await autreBailleur.Client.GetAsync(url).Verifier(HttpStatusCode.NotFound);
        await bailleur.Client.GetAsync(url).Verifier(HttpStatusCode.OK);
        await admin.Client.GetAsync(url).Verifier(HttpStatusCode.OK);
        Assert.True((await bailleur.Client.GetAsync($"/appartement/{logement.Id}").Attendre<AppartementResponseDto>()).PhotosProtegees);

        // Réactivée : de nouveau publique. Hôte désactivé : de nouveau protégée.
        await bailleur.Client.PostJson($"/appartement/{logement.Id}/reactiver").Verifier(HttpStatusCode.OK);
        await f.CreateClient().GetAsync(url).Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson($"/utilisateur/{bailleur.Id}/desactiver", new { motif = "Test" }).Verifier(HttpStatusCode.OK);
        await f.CreateClient().GetAsync(url).Verifier(HttpStatusCode.NotFound);
        await admin.Client.GetAsync(url).Verifier(HttpStatusCode.OK);
        Assert.True((await admin.Client.GetAsync($"/appartement/{logement.Id}").Attendre<AppartementResponseDto>()).PhotosProtegees);
    }

    [Fact]
    public async Task Annonce_modifiable_par_son_bailleur_et_l_admin_seulement()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var autreBailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        object Modification(decimal prix) => new
        {
            adresse = "12 rue des Tests", disponible = true, prixParNuit = prix, dureeMaxMois = 2, ville = "Conakry", quartier = "Kipé"
        };

        await autreBailleur.Client.PutJson($"/appartement/{logement.Id}", Modification(1)).Erreur(HttpStatusCode.Conflict);
        await locataire.Client.PutJson($"/appartement/{logement.Id}", Modification(1)).Verifier(HttpStatusCode.Forbidden);
        await autreBailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Erreur(HttpStatusCode.Conflict);
        Assert.Equal(120_000m, (await bailleur.Client.PutJson($"/appartement/{logement.Id}", Modification(120_000)).Attendre<AppartementResponseDto>()).PrixParNuit);
        Assert.Equal(130_000m, (await admin.Client.PutJson($"/appartement/{logement.Id}", Modification(130_000)).Attendre<AppartementResponseDto>()).PrixParNuit);

        // Un bailleur publie toujours pour son propre compte, même s'il indique un autre bailleur.
        var publie = await bailleur.Client.PostJson("/appartement/ajouter", new
        {
            bailleurId = autreBailleur.Id, adresse = "1 rue Usurpée", typeOffre = "SejourCourt", prixParNuit = 1000m
        }).Attendre<AppartementResponseDto>(HttpStatusCode.Created);
        Assert.Equal(bailleur.Id, publie.BailleurId);
        await locataire.Client.PostJson("/appartement/ajouter", new { adresse = "x", typeOffre = "SejourCourt", prixParNuit = 1000m })
            .Verifier(HttpStatusCode.Forbidden);
    }
}
