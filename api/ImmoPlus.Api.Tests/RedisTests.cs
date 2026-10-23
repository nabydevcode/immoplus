namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Text.Json;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Deux instances de l'API sur la même base et le même Redis (jetable) : temps réel, limitation des connexions et
/// cache du statut des comptes partagés.
/// </summary>
public class RedisTests : IAsyncLifetime
{
    private ImmoPlusFactory _a = null!;
    private ImmoPlusFactory _b = null!;

    public async Task InitializeAsync()
    {
        // Base Redis à part pour cette classe : compteurs (dont celui de l'adresse IP du serveur de test) indépendants.
        var reglages = new Dictionary<string, string?>
        {
            ["Redis:Connexion"] = await ServeursAnnexesTests.RedisAsync() + $",defaultDatabase={Random.Shared.Next(1, 16)}"
        };
        _a = ImmoPlusFactory.Creer(reglages);
        _b = ImmoPlusFactory.Creer(reglages, memeBaseQue: _a);
    }

    public async Task DisposeAsync()
    {
        await _b.DisposeAsync();
        await _a.DisposeAsync();
    }

    private static Task<HttpResponseMessage> Connexion(ImmoPlusFactory f, string email, string motDePasse) =>
        f.CreateClient().PostJson("/utilisateur/connexion", new { email, motDePasse });

    [Fact]
    public async Task Les_echecs_de_connexion_sont_comptes_sur_toutes_les_instances()
    {
        var locataire = await _a.LocataireAsync();
        for (var i = 0; i < 3; i++)
        {
            await Connexion(_a, locataire.Email, "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        for (var i = 0; i < 2; i++)
        {
            await Connexion(_b, locataire.Email, "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        // 5 échecs au total (3 + 2) : bloqué sur les deux instances, même avec le bon mot de passe.
        await Connexion(_a, locataire.Email, Api.MotDePasse).Erreur(HttpStatusCode.TooManyRequests);
        await Connexion(_b, locataire.Email, Api.MotDePasse).Erreur(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Mot_de_passe_oublie_cinq_emails_au_plus_toutes_instances_confondues()
    {
        var locataire = await _a.LocataireAsync();
        for (var i = 0; i < 4; i++)
        {
            await _a.CreateClient().PostJson("/utilisateur/mot-de-passe-oublie", new { email = locataire.Email }).Verifier(HttpStatusCode.OK);
            await _b.CreateClient().PostJson("/utilisateur/mot-de-passe-oublie", new { email = locataire.Email }).Verifier(HttpStatusCode.OK);
        }
        // Les deux instances dépilent la même boîte d'envoi.
        await Task.WhenAll(_a.EnvoyerMessagesAsync(), _b.EnvoyerMessagesAsync());
        var recus = _a.Smtp.EmailsPour(locataire.Email).Concat(_b.Smtp.EmailsPour(locataire.Email)).Count(e => e.Sujet.Contains("Réinitialisation"));
        Assert.Equal(5, recus);
    }

    [Fact]
    public async Task Compte_desactive_sur_une_instance_coupe_aussitot_sur_l_autre()
    {
        var admin = await _a.AdminAsync();
        var locataire = await _a.LocataireAsync();
        var surB = _b.CreateClient().AvecJeton(locataire.Jeton);

        // Statut mis en cache par B…
        await surB.GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.OK);
        // …désactivation par A : B refuse immédiatement (cache effacé), sans attendre l'expiration de 30 s.
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Test multi-instances" }).Verifier(HttpStatusCode.OK);
        Assert.Contains("désactivé", await surB.GetAsync("/utilisateur/moi").Erreur(HttpStatusCode.Unauthorized));

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/reactiver").Verifier(HttpStatusCode.OK);
        await surB.GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Message_envoye_via_une_instance_recu_en_temps_reel_sur_l_autre()
    {
        var bailleur = await _a.BailleurAsync();
        var voyageur = await _a.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 10, 2);

        // Le voyageur est connecté au hub de l'instance B ; l'hôte écrit via l'instance A.
        var recu = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_b.Server.BaseAddress, "hubs/messages"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _b.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(voyageur.Jeton);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        hub.On<JsonElement>("nouveauMessage", message => recu.TrySetResult(message));
        await hub.StartAsync();

        await bailleur.Client.PostJson($"/reservation/{reservation.Id}/messages", new { contenu = "Bienvenue, à bientôt !" })
            .Verifier(HttpStatusCode.Created);

        var message = await recu.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("Bienvenue, à bientôt !", message.GetProperty("contenu").GetString());
        Assert.Equal(reservation.Id, message.GetProperty("reservationId").GetInt32());
    }
}
