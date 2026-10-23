namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Étape 3 : pagination des listes, cache et compression, santé, corrélation et métriques.</summary>
public class PaginationPerformancesTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Annonces_paginees_tri_stable_et_tailles_plafonnees()
    {
        var bailleur = await f.BailleurAsync();
        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await bailleur.SejourCourtAsync()).Id);
        }
        var anonyme = f.CreateClient();

        var page1 = await anonyme.GetAsync($"/appartement/lister?bailleurId={bailleur.Id}&taille=2").Attendre<PagedResultDto<AppartementResponseDto>>();
        Assert.Equal((1, 2, 3, 2), (page1.PageActuelle, page1.TaillePage, page1.TotalElements, page1.TotalPages));
        // Les plus récentes d'abord (n° décroissant).
        Assert.Equal(ids.OrderDescending().Take(2), page1.Donnees.Select(a => a.Id));
        var page2 = await anonyme.GetAsync($"/appartement/lister?bailleurId={bailleur.Id}&taille=2&page=2").Attendre<PagedResultDto<AppartementResponseDto>>();
        Assert.Equal([ids.Min()], page2.Donnees.Select(a => a.Id));
        Assert.Empty((await anonyme.GetAsync($"/appartement/lister?bailleurId={bailleur.Id}&taille=2&page=9").Attendre<PagedResultDto<AppartementResponseDto>>()).Donnees);

        // Taille plafonnée à 100, et à 200 avec les 4 bornes de la carte ; valeurs absurdes → défauts.
        Assert.Equal(100, (await anonyme.GetAsync("/appartement/lister?taille=5000").Attendre<PagedResultDto<AppartementResponseDto>>()).TaillePage);
        Assert.Equal(200, (await anonyme.GetAsync("/appartement/lister?taille=5000&nord=10&sud=9&est=-13&ouest=-14")
            .Attendre<PagedResultDto<AppartementResponseDto>>()).TaillePage);
        var defauts = await anonyme.GetAsync("/appartement/lister?taille=0&page=-3").Attendre<PagedResultDto<AppartementResponseDto>>();
        Assert.Equal((1, 20), (defauts.PageActuelle, defauts.TaillePage));

        // Recherche par n° d'annonce (référence sur 6 chiffres) pour le propriétaire.
        var parReference = await bailleur.Client.GetAsync($"/appartement/lister?q={ids[0]:D6}").AttendreDonnees<AppartementResponseDto>();
        Assert.Equal([ids[0]], parReference.Select(a => a.Id));
    }

    [Fact]
    public async Task Disponibilite_sur_des_dates_calculee_en_sql()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var libre = await bailleur.SejourCourtAsync();
        var occupe = await bailleur.SejourCourtAsync();
        var troisNuitsMin = await bailleur.SejourCourtAsync(nuitsMin: 3);
        await voyageur.ReserverOk(occupe.Id, 30, 4);

        var trouves = await f.CreateClient().GetAsync($"/appartement/lister?bailleurId={bailleur.Id}&typeOffre=SejourCourt&arrivee={Api.Jour(31)}&depart={Api.Jour(33)}")
            .AttendreDonnees<AppartementResponseDto>();
        Assert.Equal([libre.Id], trouves.Select(a => a.Id));
        Assert.DoesNotContain(trouves, a => a.Id == occupe.Id || a.Id == troisNuitsMin.Id);
    }

    [Fact]
    public async Task Reservations_par_onglet_avec_compteurs_et_recherche_par_reference()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var instantane = await bailleur.SejourCourtAsync();
        var surDemande = await bailleur.SejourCourtAsync(instantanee: false);
        var demande = await voyageur.ReserverOk(surDemande.Id, 10, 2);
        var aVenir = await voyageur.ReserverOk(instantane.Id, 20, 2);

        async Task<PagedResultDto<ReservationResponseDto>> Lister(Compte compte, string parametres) =>
            await compte.Client.GetAsync($"/reservation/lister?{parametres}").Attendre<PagedResultDto<ReservationResponseDto>>();

        // Hôte : la demande est dans « Demandes », pas dans « À venir » ; compteur d'onglet avec taille=1.
        Assert.Equal([demande.Id], (await Lister(bailleur, "filtre=Demandes")).Donnees.Select(r => r.Id));
        Assert.Equal([aVenir.Id], (await Lister(bailleur, "filtre=AVenir")).Donnees.Select(r => r.Id));
        Assert.Equal(2, (await Lister(bailleur, "taille=1")).TotalElements);
        // Voyageur : « À venir » contient aussi sa demande, par date d'arrivée.
        Assert.Equal([demande.Id, aVenir.Id], (await Lister(voyageur, "filtre=avenir")).Donnees.Select(r => r.Id));
        Assert.Empty((await Lister(voyageur, "filtre=AEvaluer")).Donnees);
        Assert.Equal([aVenir.Id], (await Lister(voyageur, $"q={aVenir.Id:D6}")).Donnees.Select(r => r.Id));
        await voyageur.Client.GetAsync("/reservation/lister?filtre=Inconnu").Erreur(HttpStatusCode.BadRequest);
        await voyageur.Client.GetAsync("/reservation/lister?statut=Perdue").Erreur(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fil_de_messages_pagine_les_plus_recents_d_abord_affiches_dans_l_ordre()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);
        var url = $"/reservation/{reservation.Id}/messages";
        var envoyes = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            envoyes.Add((await voyageur.Client.PostJson(url, new { contenu = $"Message {i}" }).Attendre<MessageDto>(HttpStatusCode.Created)).Id);
        }
        Assert.Equal(5, (await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);
        Assert.Equal(5, (await bailleur.Client.GetAsync("/messages/non-lus?filtre=AVenir").Attendre<MessagesNonLusDto>()).Total);
        Assert.Equal(0, (await bailleur.Client.GetAsync("/messages/non-lus?filtre=Demandes").Attendre<MessagesNonLusDto>()).Total);

        // Page 1 = les 2 plus récents, en ordre chronologique ; ouvrir le fil marque TOUT comme lu.
        var page1 = await bailleur.Client.GetAsync($"{url}?taille=2").Attendre<PagedResultDto<MessageDto>>();
        Assert.Equal(envoyes.TakeLast(2), page1.Donnees.Select(m => m.Id));
        Assert.Equal((5, 3), (page1.TotalElements, page1.TotalPages));
        Assert.Equal(0, (await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);
        var page2 = await bailleur.Client.GetAsync($"{url}?taille=2&page=2").AttendreDonnees<MessageDto>();
        Assert.Equal(envoyes.Skip(1).Take(2), page2.Select(m => m.Id));
        // Curseur : les messages antérieurs au plus ancien affiché.
        var avant = await bailleur.Client.GetAsync($"{url}?taille=30&avant={envoyes[2]}").Attendre<PagedResultDto<MessageDto>>();
        Assert.Equal(envoyes.Take(2), avant.Donnees.Select(m => m.Id));
        Assert.Equal(2, avant.TotalElements);
    }

    [Fact]
    public async Task Listes_paginees_favoris_avis_identites_candidatures()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var logements = new[] { await bailleur.SejourCourtAsync(), await bailleur.SejourCourtAsync(), await bailleur.LongueDureeAsync() };
        foreach (var logement in logements)
        {
            await locataire.Client.PostJson($"/favoris/{logement.Id}").Verifier(HttpStatusCode.NoContent);
        }
        var favoris = await locataire.Client.GetAsync("/favoris?taille=2").Attendre<PagedResultDto<AppartementResponseDto>>();
        Assert.Equal((2, 3), (favoris.Donnees.Count, favoris.TotalElements));
        Assert.Equal(logements[2].Id, favoris.Donnees[0].Id); // le plus récent d'abord

        var avis = await f.CreateClient().GetAsync($"/appartement/{logements[0].Id}/avis?taille=5").Attendre<ListeAvisDto>();
        Assert.Equal((0, 1, 5, 0), (avis.Nombre, avis.PageActuelle, avis.TaillePage, avis.TotalPages));

        var identites = await admin.Client.GetAsync("/admin/identites?statut=EnAttente&taille=1").Attendre<PagedResultDto<PieceIdentiteDto>>();
        Assert.Equal((1, 1), (identites.PageActuelle, identites.TaillePage));
        await admin.Client.GetAsync("/admin/identites?statut=Perdue").Erreur(HttpStatusCode.BadRequest);

        // Candidatures : page et taille facultatives, filtres statut / appartementId.
        await locataire.Client.PostJson("/candidature/creer", new { appartementId = logements[2].Id, apportInitial = 2_000_000m, situationProfessionnelle = "CDI" })
            .Verifier(HttpStatusCode.Created);
        var dossiers = await bailleur.Client.GetAsync($"/candidature/lister?statut=EnAttente&appartementId={logements[2].Id}")
            .Attendre<PagedResultDto<CandidatureResponseDto>>();
        Assert.Equal((1, 20), (dossiers.TotalElements, dossiers.TaillePage));
        await bailleur.Client.GetAsync("/candidature/lister?statut=Perdue").Erreur(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Liste_publique_en_cache_videe_par_une_modification_et_jamais_pour_un_utilisateur_connecte()
    {
        var bailleur = await f.BailleurAsync();
        var premier = await bailleur.SejourCourtAsync();
        var anonyme = f.CreateClient();
        var url = $"/appartement/lister?bailleurId={bailleur.Id}";

        Assert.Single(await anonyme.GetAsync(url).AttendreDonnees<AppartementResponseDto>());
        var deuxieme = await anonyme.GetAsync(url);
        Assert.True(deuxieme.Headers.Age is not null, "la seconde lecture anonyme doit venir du cache de sortie");

        // Nouvelle annonce : le cache est vidé avant la réponse, la relecture la voit tout de suite.
        var nouvelle = await bailleur.SejourCourtAsync();
        Assert.Equal([nouvelle.Id, premier.Id], (await anonyme.GetAsync(url).AttendreDonnees<AppartementResponseDto>()).Select(a => a.Id));

        // Avec un jeton : jamais servi depuis le cache (détails réservés au propriétaire).
        await bailleur.Client.GetAsync(url);
        var connecte = await bailleur.Client.GetAsync(url);
        Assert.Null(connecte.Headers.Age);
    }

    [Fact]
    public async Task Reponses_json_compressees_et_photos_publiques_en_cache_navigateur()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync();
        var anonyme = f.CreateClient();

        var requete = new HttpRequestMessage(HttpMethod.Get, "/appartement/lister?taille=50");
        requete.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var compressee = await anonyme.SendAsync(requete);
        Assert.Equal(HttpStatusCode.OK, compressee.StatusCode);
        Assert.Contains("br", compressee.Content.Headers.ContentEncoding);

        var photo = await bailleur.Client.PostAsync($"/appartement/{logement.Id}/photos", Api.Formulaire("salon.png", Api.Png))
            .Attendre<PhotoAppartementDto>(HttpStatusCode.Created);
        var publique = await anonyme.GetAsync($"/{photo.Url}");
        Assert.Equal(HttpStatusCode.OK, publique.StatusCode);
        Assert.Equal("public, max-age=86400, immutable", publique.Headers.CacheControl?.ToString());
        var etag = publique.Headers.ETag ?? throw new Xunit.Sdk.XunitException("ETag manquant");
        var revalidation = new HttpRequestMessage(HttpMethod.Get, $"/{photo.Url}");
        revalidation.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await anonyme.SendAsync(revalidation)).StatusCode);
        var miniature = await anonyme.GetAsync($"/{photo.UrlMiniature}");
        Assert.NotEqual(etag, miniature.Headers.ETag);

        // Annonce supprimée : 404 pour le public, servie au propriétaire sans cache partagé.
        await bailleur.Client.DeleteAsync($"/appartement/{logement.Id}").Verifier(HttpStatusCode.NoContent);
        await anonyme.GetAsync($"/{photo.Url}").Verifier(HttpStatusCode.NotFound);
        var protegee = await bailleur.Client.GetAsync($"/{photo.Url}").Verifier(HttpStatusCode.OK);
        Assert.Equal("no-store, private", string.Join(", ", (protegee.Headers.CacheControl?.ToString() ?? "").Split(", ").Order()));
        Assert.Null(protegee.Headers.ETag);
    }

    [Fact]
    public async Task Sante_et_identifiant_de_correlation()
    {
        var anonyme = f.CreateClient();
        var vivant = await anonyme.GetAsync("/sante/vivant").Attendre<JsonElement>();
        Assert.Equal("Ok", vivant.GetProperty("statut").GetString());
        var pret = await anonyme.GetAsync("/sante/pret").Attendre<JsonElement>();
        Assert.Equal("Ok", pret.GetProperty("statut").GetString());
        Assert.Equal("Ok", pret.GetProperty("verifications").GetProperty("postgresql").GetProperty("statut").GetString());
        Assert.DoesNotContain("Password", pret.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var requete = new HttpRequestMessage(HttpMethod.Get, "/sante/vivant");
        requete.Headers.Add("X-Correlation-Id", "test-correlation-42");
        var reprise = await anonyme.SendAsync(requete);
        Assert.Equal("test-correlation-42", reprise.Headers.GetValues("X-Correlation-Id").Single());
        // Identifiant absent ou douteux (espaces, trop long) : un nouveau est généré.
        var douteux = new HttpRequestMessage(HttpMethod.Get, "/sante/vivant");
        douteux.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script> alert </script>");
        var genere = (await anonyme.SendAsync(douteux)).Headers.GetValues("X-Correlation-Id").Single();
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", genere);
        Assert.False(string.IsNullOrEmpty((await anonyme.GetAsync("/appartement/villes")).Headers.GetValues("X-Correlation-Id").Single()));
    }

    [Fact]
    public async Task Metriques_protegees_par_jeton()
    {
        await using var avecJeton = ImmoPlusFactory.Creer(new Dictionary<string, string?> { ["Metriques:Jeton"] = "jeton-des-metriques" });
        var client = avecJeton.CreateClient();
        await client.GetAsync("/appartement/villes").Verifier(HttpStatusCode.OK);

        await client.GetAsync("/metriques").Verifier(HttpStatusCode.NotFound);
        var mauvais = new HttpRequestMessage(HttpMethod.Get, "/metriques");
        mauvais.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "autre");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(mauvais)).StatusCode);

        var bon = new HttpRequestMessage(HttpMethod.Get, "/metriques");
        bon.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "jeton-des-metriques");
        var reponse = await client.SendAsync(bon);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var texte = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("http_server_request_duration_seconds_bucket", texte);
        Assert.Contains("http_route=\"/appartement/villes\"", texte);
        Assert.Contains("immoplus_boite_envoi_en_attente", texte);
    }
}
