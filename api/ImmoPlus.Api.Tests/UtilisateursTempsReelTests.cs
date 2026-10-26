namespace ImmoPlus.Api.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>Page admin « Utilisateurs » en direct : chaque changement d'état d'un compte est poussé aux seuls admins connectés.</summary>
public class UtilisateursTempsReelTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    /// <summary>Client du hub qui accumule les événements « utilisateurChange » reçus.</summary>
    private sealed class Ecouteur : IAsyncDisposable
    {
        private readonly HubConnection _hub;
        public ConcurrentQueue<JsonElement> Recus { get; } = new();

        private Ecouteur(HubConnection hub) => _hub = hub;

        public static async Task<Ecouteur> ConnecterAsync(ImmoPlusFactory f, string jeton)
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(f.Server.BaseAddress, "hubs/messages"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult<string?>(jeton);
                    options.Transports = HttpTransportType.LongPolling;
                })
                .Build();
            var ecouteur = new Ecouteur(hub);
            hub.On<JsonElement>("utilisateurChange", e => ecouteur.Recus.Enqueue(e));
            await hub.StartAsync();
            return ecouteur;
        }

        public IEnumerable<JsonElement> Pour(int utilisateurId) =>
            Recus.Where(e => e.GetProperty("utilisateurId").GetInt32() == utilisateurId);

        public async Task<JsonElement> AttendreAsync(int utilisateurId, string type)
        {
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < limite)
            {
                var trouve = Pour(utilisateurId).Where(e => e.GetProperty("type").GetString() == type).ToList();
                if (trouve.Count > 0)
                {
                    return Assert.Single(trouve);
                }
                await Task.Delay(50);
            }
            throw new TimeoutException($"Événement {type} non reçu pour l'utilisateur {utilisateurId}");
        }

        public ValueTask DisposeAsync() => _hub.DisposeAsync();
    }

    [Fact]
    public async Task Inscription_confirmation_et_validation_poussees_aux_seuls_admins()
    {
        var admin = await f.AdminAsync();
        var autre = await f.LocataireAsync();
        await using var surAdmin = await Ecouteur.ConnecterAsync(f, admin.Jeton);
        await using var surAutre = await Ecouteur.ConnecterAsync(f, autre.Jeton);

        var email = Donnees.NouvelEmail("temps-reel");
        var cree = await f.CreateClient().PostJson("/utilisateur/inscription", new
        {
            nom = "Mariama Direct", telephone = "622000000", email, motDePasse = Api.MotDePasse, role = "Bailleur"
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);

        var inscrit = await surAdmin.AttendreAsync(cree.Id, "inscrit");
        Assert.Equal("EnAttente", inscrit.GetProperty("statut").GetString());
        Assert.False(inscrit.GetProperty("emailConfirme").GetBoolean());
        // Aucune donnée personnelle dans l'événement : le front relit la liste.
        Assert.Equal(["utilisateurId", "type", "statut", "emailConfirme"], inscrit.EnumerateObject().Select(p => p.Name));

        // Action refusée (email non confirmé) et renvoi du lien : aucun changement d'état, aucun événement.
        await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Erreur(HttpStatusCode.Conflict);
        await f.CreateClient().PostJson("/utilisateur/renvoyer-confirmation", new { email }).Verifier(HttpStatusCode.OK);

        await f.CreateClient().PostJson("/utilisateur/confirmer-email", new { jeton = await f.JetonConfirmationAsync(email) })
            .Verifier(HttpStatusCode.OK);
        var confirme = await surAdmin.AttendreAsync(cree.Id, "emailConfirme");
        Assert.Equal("EnAttente", confirme.GetProperty("statut").GetString());
        Assert.True(confirme.GetProperty("emailConfirme").GetBoolean());

        await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Verifier(HttpStatusCode.OK);
        var valide = await surAdmin.AttendreAsync(cree.Id, "valide");
        Assert.Equal("Valide", valide.GetProperty("statut").GetString());

        await admin.Client.PostJson($"/utilisateur/{cree.Id}/desactiver", new { motif = "Test temps réel" }).Verifier(HttpStatusCode.OK);
        Assert.Equal("Desactive", (await surAdmin.AttendreAsync(cree.Id, "desactive")).GetProperty("statut").GetString());
        await admin.Client.PostJson($"/utilisateur/{cree.Id}/reactiver").Verifier(HttpStatusCode.OK);
        Assert.Equal("Valide", (await surAdmin.AttendreAsync(cree.Id, "reactive")).GetProperty("statut").GetString());

        var compte = await f.ConnecterAsync(email, Api.MotDePasse);
        await compte.Client.PutJson("/utilisateur/moi", new { nom = "Mariama Directe", telephone = "622000001", email })
            .Verifier(HttpStatusCode.OK);
        await surAdmin.AttendreAsync(cree.Id, "profilModifie");

        // Un événement par changement effectif, dans l'ordre ; rien pour le refus ni pour le renvoi du lien.
        Assert.Equal(["inscrit", "emailConfirme", "valide", "desactive", "reactive", "profilModifie"],
            surAdmin.Pour(cree.Id).Select(e => e.GetProperty("type").GetString()));
        // Le non-admin, connecté au même hub, ne reçoit rien (laisse le temps à un éventuel envoi égaré d'arriver).
        await Task.Delay(500);
        Assert.Empty(surAutre.Recus);
    }

    [Fact]
    public async Task Creation_d_admin_poussee_aux_admins()
    {
        var admin = await f.AdminAsync();
        await using var surAdmin = await Ecouteur.ConnecterAsync(f, admin.Jeton);
        var cree = await admin.Client.PostJson("/utilisateur/admin", new
        {
            nom = "Admin Direct", telephone = "624000000", email = Donnees.NouvelEmail("admin-direct"), motDePasse = Api.MotDePasse
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        var evenement = await surAdmin.AttendreAsync(cree.Id, "adminCree");
        Assert.Equal("Valide", evenement.GetProperty("statut").GetString());
        Assert.True(evenement.GetProperty("emailConfirme").GetBoolean());
    }
}
