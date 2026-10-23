namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Signalements : anti-abus (5 par heure, pas de doublon ouvert), email aux admins, traitement et actions de l'admin.</summary>
public class SignalementTests
{
    private static object Signaler(string cible, int cibleId, string motif = "Arnaque", string? email = null, string? texte = "Annonce suspecte") =>
        new { cible, cibleId, motif, texte, email };

    [Fact]
    public async Task Anonyme_email_obligatoire_doublon_refuse_et_admins_prevenus()
    {
        await using var f = new ImmoPlusFactory();
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync();
        var anonyme = f.CreateClient();

        await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id)).Erreur(HttpStatusCode.BadRequest);
        await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id, motif: "Inconnu", email: "visiteur@exemple.test"))
            .Erreur(HttpStatusCode.BadRequest);
        await anonyme.PostJson("/signalements", Signaler("Appartement", 999_999, email: "visiteur@exemple.test")).Erreur(HttpStatusCode.NotFound);
        var cree = await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id, email: "visiteur@exemple.test"))
            .Attendre<SignalementDto>(HttpStatusCode.Created);
        Assert.Equal("Nouveau", cree.Statut);
        Assert.Contains(await f.EmailsPourAsync(admin.Email), e => e.Sujet.Contains($"[Signalement n° {cree.Id:D6}]"));

        // Même auteur, même cible, signalement encore ouvert : 409 (email comparé sans la casse).
        await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id, email: "VISITEUR@exemple.test")).Erreur(HttpStatusCode.Conflict);

        // Anti-abus par adresse IP pour les visiteurs : 5 par heure au total.
        for (var i = 0; i < 4; i++)
        {
            await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id, email: $"autre{i}@exemple.test")).Verifier(HttpStatusCode.Created);
        }
        await anonyme.PostJson("/signalements", Signaler("Appartement", logement.Id, email: "encore@exemple.test")).Erreur(HttpStatusCode.TooManyRequests);

        // Réservé à l'admin.
        await bailleur.Client.GetAsync("/admin/signalements").Verifier(HttpStatusCode.Forbidden);
        var liste = await admin.Client.GetAsync("/admin/signalements?statut=Nouveau&cible=Appartement").Attendre<PagedResultDto<SignalementDto>>();
        Assert.Equal(5, liste.TotalElements);
        Assert.All(liste.Donnees, s => Assert.Equal(logement.Id, s.AppartementId));
        Assert.Equal(5, (await admin.Client.GetAsync("/admin/compteurs").Attendre<CompteursAdminDto>()).SignalementsNouveaux);
    }

    [Fact]
    public async Task Utilisateur_connecte_limite_a_cinq_par_heure_et_messages_reserves_aux_participants()
    {
        await using var f = new ImmoPlusFactory();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var intrus = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        var message = await bailleur.Client.PostJson($"/reservation/{reservation.Id}/messages", new { contenu = "Message déplacé de l'hôte" })
            .Attendre<MessageDto>(HttpStatusCode.Created);

        await intrus.Client.PostJson("/signalements", Signaler("Message", message.Id, "ComportementAbusif")).Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostJson("/signalements", Signaler("Message", message.Id, "ComportementAbusif")).Erreur(HttpStatusCode.BadRequest);
        var signale = await voyageur.Client.PostJson("/signalements", Signaler("Message", message.Id, "ComportementAbusif"))
            .Attendre<SignalementDto>(HttpStatusCode.Created);
        Assert.Equal(voyageur.Id, signale.AuteurId);
        Assert.Equal(reservation.Id, signale.ReservationId);
        await voyageur.Client.PostJson("/signalements", Signaler("Autre", 1)).Erreur(HttpStatusCode.BadRequest);
        await voyageur.Client.PostJson("/signalements", Signaler("Utilisateur", voyageur.Id, "ComportementAbusif")).Erreur(HttpStatusCode.BadRequest);
        await voyageur.Client.PostJson("/signalements", Signaler("Appartement", logement.Id, "Autre", texte: "")).Erreur(HttpStatusCode.BadRequest);

        // 4 autres cibles distinctes, puis la 6e est refusée (compteur par utilisateur).
        await voyageur.Client.PostJson("/signalements", Signaler("Appartement", logement.Id)).Verifier(HttpStatusCode.Created);
        await voyageur.Client.PostJson("/signalements", Signaler("Utilisateur", bailleur.Id, "ComportementAbusif")).Verifier(HttpStatusCode.Created);
        for (var i = 0; i < 2; i++)
        {
            var autre = await bailleur.SejourCourtAsync();
            await voyageur.Client.PostJson("/signalements", Signaler("Appartement", autre.Id, "Doublon")).Verifier(HttpStatusCode.Created);
        }
        var dernier = await bailleur.SejourCourtAsync();
        await voyageur.Client.PostJson("/signalements", Signaler("Appartement", dernier.Id)).Erreur(HttpStatusCode.TooManyRequests);
        // Un autre utilisateur n'est pas concerné par ce compteur.
        await intrus.Client.PostJson("/signalements", Signaler("Appartement", dernier.Id)).Verifier(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Traitement_masquer_un_avis_et_desactiver_une_annonce()
    {
        await using var f = new ImmoPlusFactory();
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var annonce = await bailleur.SejourCourtAsync();

        // Avis inséré directement sur un séjour terminé (la création par l'API est couverte ailleurs).
        var avisId = 0;
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        await f.ModifierBaseAsync(async db =>
        {
            var avis = new Avis
            {
                ReservationId = reservation.Id, AuteurId = voyageur.Id, CibleId = bailleur.Id, AppartementId = logement.Id,
                Sens = Models.Enums.SensAvis.VoyageurVersLogement, Note = 1, Commentaire = "Contenu injurieux à masquer"
            };
            db.Avis.Add(avis);
            await db.SaveChangesAsync();
            avisId = avis.Id;
        });
        Assert.Equal(1, (await f.CreateClient().GetAsync($"/appartement/{logement.Id}/avis").Attendre<ListeAvisDto>()).Nombre);

        var surAvis = await bailleur.Client.PostJson("/signalements", Signaler("Avis", avisId, "ContenuInapproprie")).Attendre<SignalementDto>(HttpStatusCode.Created);
        var surAnnonce = await voyageur.Client.PostJson("/signalements", Signaler("Appartement", annonce.Id)).Attendre<SignalementDto>(HttpStatusCode.Created);

        await bailleur.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter", new { statut = "Traite", action = "masquerAvis" })
            .Verifier(HttpStatusCode.Forbidden);
        // Action incohérente avec la cible, ou action sur un rejet : 400, rien n'est enregistré.
        await admin.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter", new { statut = "Traite", action = "desactiverAnnonce" })
            .Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter", new { statut = "Rejete", action = "masquerAvis" })
            .Erreur(HttpStatusCode.BadRequest);
        var enCours = await admin.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter", new { statut = "EnCours", decision = "Vérification" })
            .Attendre<SignalementDto>();
        Assert.Equal("EnCours", enCours.Statut);
        var traite = await admin.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter",
            new { statut = "Traite", decision = "Propos injurieux", action = "masquerAvis" }).Attendre<SignalementDto>();
        Assert.Equal("Traite", traite.Statut);
        Assert.Equal("masquerAvis", traite.ActionAppliquee);
        Assert.Equal(admin.Id, traite.TraiteParId);
        var publics = await f.CreateClient().GetAsync($"/appartement/{logement.Id}/avis").Attendre<ListeAvisDto>();
        Assert.Equal(0, publics.Nombre);
        Assert.Null(publics.Moyenne);
        await admin.Client.PostJson($"/admin/signalements/{surAvis.Id}/traiter", new { statut = "Rejete" }).Erreur(HttpStatusCode.Conflict);

        await admin.Client.PostJson($"/admin/signalements/{surAnnonce.Id}/traiter", new { statut = "Traite", action = "desactiverAnnonce" })
            .Verifier(HttpStatusCode.OK);
        await f.CreateClient().GetAsync($"/appartement/{annonce.Id}").Verifier(HttpStatusCode.NotFound);

        // Les actions sont tracées dans le journal.
        var journal = await admin.Client.GetAsync($"/admin/journal?cible=Signalement&cibleId={surAnnonce.Id}").Attendre<PagedResultDto<JournalActionDto>>();
        Assert.Contains(journal.Donnees, j => j.Action == "signalement.traiter" && j.Details.Contains("désactivée"));
    }
}
