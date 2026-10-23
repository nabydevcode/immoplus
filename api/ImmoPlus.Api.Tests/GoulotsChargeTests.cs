namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Text.Json;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Middleware;
using ImmoPlus.Api.Models.Enums;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

/// <summary>
/// Corrections issues des tests de charge : base saturée → 503, identifiant de corrélation propre à chaque réponse même
/// servie par le cache, non-lus selon le rôle, maintenance planifiée par lots (plus pendant les lectures), photos de liste.
/// </summary>
public class GoulotsChargeTests : IAsyncLifetime
{
    private ImmoPlusFactory f = null!;

    public Task InitializeAsync()
    {
        // Lots de 1 et un seul lot par passage : on voit l'arriéré se résorber passage après passage.
        f = ImmoPlusFactory.Creer(new Dictionary<string, string?>
        {
            ["Maintenance:TailleLot"] = "1",
            ["Maintenance:LotsMaxParPassage"] = "1"
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await f.DisposeAsync();

    // --- 503 ---

    private static async Task<(int Statut, string? RetryAfter, string Message)> PasserAsync(Exception exception)
    {
        var contexte = new DefaultHttpContext();
        contexte.Response.Body = new MemoryStream();
        var middleware = new MiddlewareException(_ => throw exception, NullLogger<MiddlewareException>.Instance);
        await middleware.InvokeAsync(contexte);
        contexte.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(contexte.Response.Body);
        return (contexte.Response.StatusCode, contexte.Response.Headers.RetryAfter.FirstOrDefault(), json.RootElement.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Base_saturee_503_avec_Retry_After_et_message_generique_les_400_metier_restent()
    {
        // Vraie erreur de pool Npgsql épuisé (1 connexion, attente 1 s) sur la base de test.
        string chaine;
        using (var scope = f.Services.CreateScope())
        {
            chaine = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        }
        var petitPool = new NpgsqlConnectionStringBuilder(chaine) { MaxPoolSize = 1, Timeout = 1, ApplicationName = "test-pool-epuise" }.ConnectionString;
        await using var occupee = new NpgsqlConnection(petitPool);
        await occupee.OpenAsync();
        await using var deuxieme = new NpgsqlConnection(petitPool);
        var poolEpuise = await Assert.ThrowsAnyAsync<NpgsqlException>(() => deuxieme.OpenAsync());

        var cas = new Exception[]
        {
            poolEpuise,
            // Ce qu'EF Core renvoie quand PostgreSQL refuse la connexion (« too many clients ») : avant, un 400 au message technique.
            new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
                new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300")),
            new DbUpdateException("Erreur", new NpgsqlException("Exception while reading from stream", new TimeoutException())),
            new TimeoutException("Délai dépassé")
        };
        foreach (var exception in cas)
        {
            var (statut, retryAfter, message) = await PasserAsync(exception);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, statut);
            Assert.Equal("5", retryAfter);
            Assert.Equal("Service momentanément indisponible, réessayez dans quelques instants", message);
        }

        // Erreur métier : toujours 400 avec son message ; violation de contrainte non gérée : erreur interne (500), pas 503.
        var metier = await PasserAsync(new InvalidOperationException("La date de départ doit être après la date d'arrivée"));
        Assert.Equal((400, "La date de départ doit être après la date d'arrivée"), (metier.Statut, metier.Message));
        Assert.Null(metier.RetryAfter);
        var contrainte = await PasserAsync(new DbUpdateException("Erreur", new PostgresException("duplicate key", "ERROR", "ERROR", "23505")));
        Assert.Equal(500, contrainte.Statut);

        // Et de bout en bout, une 400 métier reste une 400.
        await f.CreateClient().GetAsync("/appartement/lister?arrivee=2026-12-10").Erreur(HttpStatusCode.BadRequest);
    }

    // --- Cache de sortie et corrélation ---

    [Fact]
    public async Task Reponse_servie_par_le_cache_avec_son_propre_identifiant_de_correlation()
    {
        var client = f.CreateClient();
        async Task<HttpResponseMessage> Lire(string id)
        {
            var requete = new HttpRequestMessage(HttpMethod.Get, "/appartement/villes?typeOffre=SejourCourt");
            requete.Headers.Add("X-Correlation-Id", id);
            return await client.SendAsync(requete);
        }
        var premiere = await Lire("premiere-requete");
        var seconde = await Lire("seconde-requete");
        Assert.Equal("premiere-requete", premiere.Headers.GetValues("X-Correlation-Id").Single());
        // Servie par le cache de sortie (en-tête Age), mais avec l'identifiant de la seconde requête.
        Assert.True(seconde.Headers.Age is not null, "La seconde réponse devait venir du cache de sortie");
        Assert.Equal("seconde-requete", seconde.Headers.GetValues("X-Correlation-Id").Single());
        // Sans identifiant fourni : un identifiant généré, différent de ceux mis en cache.
        var troisieme = await client.GetAsync("/appartement/villes?typeOffre=SejourCourt");
        var genere = troisieme.Headers.GetValues("X-Correlation-Id").Single();
        Assert.DoesNotContain(genere, new[] { "premiere-requete", "seconde-requete" });
    }

    // --- Non-lus selon le rôle ---

    [Fact]
    public async Task Non_lus_comptes_selon_le_role_et_par_onglet()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var autreVoyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(instantanee: false);
        var demande = await voyageur.ReserverOk(logement.Id, 10, 2);
        var autre = await autreVoyageur.ReserverOk(logement.Id, 30, 2);

        await voyageur.Client.PostJson($"/reservation/{demande.Id}/messages", new { contenu = "Bonjour, l'arrivée est possible à 15 h ?" }).Verifier(HttpStatusCode.Created);
        await voyageur.Client.PostJson($"/reservation/{demande.Id}/messages", new { contenu = "Merci d'avance" }).Verifier(HttpStatusCode.Created);
        await autreVoyageur.Client.PostJson($"/reservation/{autre.Id}/messages", new { contenu = "Bonjour" }).Verifier(HttpStatusCode.Created);
        await bailleur.Client.PostJson($"/reservation/{demande.Id}/messages", new { contenu = "Oui, sans problème" }).Verifier(HttpStatusCode.Created);

        var hote = await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>();
        Assert.Equal(3, hote.Total);
        Assert.Equal([(demande.Id, 2), (autre.Id, 1)], hote.ParReservation.Select(p => (p.ReservationId, p.Nombre)).OrderBy(p => p.Item1 == autre.Id));
        var deVoyageur = await voyageur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>();
        Assert.Equal(1, deVoyageur.Total);
        Assert.Equal(demande.Id, deVoyageur.ParReservation.Single().ReservationId);
        Assert.Equal(0, (await autreVoyageur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);

        // Onglets : les deux demandes sont dans « Demandes » de l'hôte, aucune dans « Passees » ; le paramètre filtre= reste validé.
        Assert.Equal(3, (await bailleur.Client.GetAsync("/messages/non-lus?filtre=Demandes").Attendre<MessagesNonLusDto>()).Total);
        Assert.Equal(0, (await bailleur.Client.GetAsync("/messages/non-lus?filtre=Passees").Attendre<MessagesNonLusDto>()).Total);
        Assert.Equal(1, (await voyageur.Client.GetAsync("/messages/non-lus?filtre=AVenir").Attendre<MessagesNonLusDto>()).Total);
        await voyageur.Client.GetAsync("/messages/non-lus?filtre=Inconnu").Erreur(HttpStatusCode.BadRequest);

        // Tableau de bord de l'hôte : même compte (rôle hôte).
        var tableau = await bailleur.Client.GetAsync("/bailleur/tableau-de-bord").Attendre<JsonElement>();
        Assert.Equal(3, tableau.GetProperty("messagesNonLus").GetInt32());

        // Lecture du fil par l'hôte : ses non-lus de cette conversation tombent à zéro, pas ceux du voyageur.
        await bailleur.Client.GetAsync($"/reservation/{demande.Id}/messages").Verifier(HttpStatusCode.OK);
        Assert.Equal(1, (await bailleur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);
        Assert.Equal(1, (await voyageur.Client.GetAsync("/messages/non-lus").Attendre<MessagesNonLusDto>()).Total);
    }

    // --- Maintenance planifiée ---

    private async Task<MaintenanceServices.Resultat?> PassageAsync()
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MaintenanceServices>().ExecuterSiDuAsync(TimeSpan.FromMinutes(2));
    }

    private Task ReculerDernierPassageAsync() => f.ModifierBaseAsync(async db =>
    {
        var execution = await db.ExecutionsPlanifiees.SingleAsync(e => e.Nom == MaintenanceServices.NomExecution);
        execution.DerniereExecution = DateTime.UtcNow.AddMinutes(-5);
    });

    [Fact]
    public async Task Maintenance_planifiee_par_lots_et_plus_pendant_la_lecture_admin()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var sejours = new List<ReservationResponseDto>();
        for (var i = 0; i < 3; i++)
        {
            var r = await voyageur.ReserverOk(logement.Id, 5 + i * 5, 2);
            await admin.Client.PayerReservation(r.Id, r.MontantTotal).Verifier(HttpStatusCode.OK);
            sejours.Add(r);
        }
        var impayee = await voyageur.ReserverOk(logement.Id, 40, 2);
        // Trois séjours partis et une réservation impayée depuis 25 h.
        await f.ModifierBaseAsync(async db =>
        {
            foreach (var r in await db.Reservations.Where(x => x.AppartementId == logement.Id).ToListAsync())
            {
                if (r.Id == impayee.Id)
                {
                    r.DateCreation = DateTime.UtcNow.AddHours(-25);
                }
                else
                {
                    r.DateArrivee = Api.Aujourdhui.AddDays(-5);
                    r.DateDepart = Api.Aujourdhui.AddDays(-1);
                }
            }
        });
        async Task<StatutReservation> Statut(int id) =>
            await f.LireBaseAsync(db => db.Reservations.Where(r => r.Id == id).Select(r => r.Statut).SingleAsync());

        // Liste admin d'une page qui n'affiche PAS ces réservations : rien n'est traité (plus de traitement de toute la base).
        await admin.Client.GetAsync("/reservation/lister?statut=Annulee").Verifier(HttpStatusCode.OK);
        Assert.Equal(StatutReservation.Confirmee, await Statut(sejours[2].Id));
        Assert.Equal(StatutReservation.EnAttente, await Statut(impayee.Id));

        // Recherche publique par dates : l'impayée expirée ne bloque déjà plus le logement, sans être annulée par la lecture.
        var libres = await f.CreateClient().GetAsync($"/appartement/lister?typeOffre=SejourCourt&arrivee={Api.Jour(40)}&depart={Api.Jour(42)}")
            .AttendreDonnees<AppartementResponseDto>();
        Assert.Contains(libres, a => a.Id == logement.Id);
        Assert.Equal(StatutReservation.EnAttente, await Statut(impayee.Id));

        // Passage 1 (lot de 1) : l'impayée et le premier séjour seulement.
        var premier = await PassageAsync();
        Assert.NotNull(premier);
        Assert.Equal((1, 1), (premier!.ImpayeesAnnulees, premier.SejoursTermines));
        Assert.Equal(StatutReservation.Annulee, await Statut(impayee.Id));
        Assert.Equal(StatutReservation.Terminee, await Statut(sejours[0].Id));
        Assert.Equal(StatutReservation.Confirmee, await Statut(sejours[1].Id));
        // Trop tôt (et une seule instance à la fois) : rien.
        Assert.Null(await PassageAsync());

        // Liste admin qui affiche les réservations restantes : traitement ciblé des lignes affichées, statut à jour dans la réponse.
        var page = await admin.Client.GetAsync($"/reservation/lister?q={sejours[1].Id:D6}").AttendreDonnees<ReservationResponseDto>();
        Assert.Equal("Terminee", page.Single(r => r.Id == sejours[1].Id).Statut);
        Assert.Equal(StatutReservation.Confirmee, await Statut(sejours[2].Id));

        // Passage suivant : le dernier séjour.
        await ReculerDernierPassageAsync();
        Assert.Equal(1, (await PassageAsync())!.SejoursTermines);
        Assert.Equal(StatutReservation.Terminee, await Statut(sejours[2].Id));
        await ReculerDernierPassageAsync();
        Assert.Equal(0, (await PassageAsync())!.Total);

        // Emails une seule fois : un « Séjour terminé » par séjour pour le voyageur, une annulation pour l'impayée.
        var emails = await f.EmailsPourAsync(voyageur.Email);
        foreach (var r in sejours)
        {
            Assert.Single(emails, e => e.Sujet.Contains("Séjour terminé") && e.Sujet.Contains($"{r.Id:D6}"));
        }
        Assert.Single(emails, e => e.Sujet.Contains($"{impayee.Id:D6}") && e.Sujet.Contains("annul", StringComparison.OrdinalIgnoreCase));
    }

    // --- Liste des annonces : couverture seulement pour le public ---

    [Fact]
    public async Task Liste_publique_avec_la_couverture_seulement_le_proprietaire_voit_toutes_ses_photos()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync();
        foreach (var (est360, libelle) in new[] { (false, "Salon"), (false, "Chambre"), (true, "Visite"), (true, "Terrasse") })
        {
            await bailleur.Client.PostAsync($"/appartement/{logement.Id}/photos",
                Api.Formulaire("photo.png", Api.Png, ("Est360", est360.ToString()), ("Libelle", libelle))).Verifier(HttpStatusCode.Created);
        }
        var fiche = await f.CreateClient().GetAsync($"/appartement/{logement.Id}").Attendre<AppartementResponseDto>();
        Assert.Equal(4, fiche.Photos.Count);

        var publique = (await f.CreateClient().GetAsync($"/appartement/lister?bailleurId={bailleur.Id}").AttendreDonnees<AppartementResponseDto>()).Single();
        // Couverture (première photo classique) + premier panorama (badge 360°), dans l'ordre de la fiche.
        Assert.Equal(fiche.Photos.Where(p => !p.Est360).Take(1).Concat(fiche.Photos.Where(p => p.Est360).Take(1)).Select(p => p.Id),
            publique.Photos.Select(p => p.Id));
        Assert.Equal(logement.BailleurNom, publique.BailleurNom);

        var proprietaire = (await bailleur.Client.GetAsync($"/appartement/lister?bailleurId={bailleur.Id}").AttendreDonnees<AppartementResponseDto>()).Single();
        Assert.Equal(fiche.Photos.Select(p => p.Id), proprietaire.Photos.Select(p => p.Id));
    }

    // --- Préfixe Redis ---

    [Fact]
    public async Task Deux_environnements_sur_le_meme_Redis_avec_des_prefixes_differents_ne_partagent_rien()
    {
        var redis = await ServeursAnnexesTests.RedisAsync() + $",defaultDatabase={Random.Shared.Next(1, 16)}";
        await using var a = ImmoPlusFactory.Creer(new Dictionary<string, string?> { ["Redis:Connexion"] = redis, ["Redis:Prefixe"] = "env-a" });
        await using var b = ImmoPlusFactory.Creer(new Dictionary<string, string?> { ["Redis:Connexion"] = redis, ["Redis:Prefixe"] = "env-b" }, memeBaseQue: a);
        var locataire = await a.LocataireAsync();
        for (var i = 0; i < 5; i++)
        {
            await a.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = "mauvais" }).Verifier(HttpStatusCode.NotFound);
        }
        // Bloqué dans l'environnement A seulement : B a ses propres compteurs.
        await a.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = Api.MotDePasse }).Erreur(HttpStatusCode.TooManyRequests);
        await b.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = Api.MotDePasse }).Verifier(HttpStatusCode.OK);
    }
}
