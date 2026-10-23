namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Text.Json;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Comportements propres au passage à PostgreSQL : casse, accents, unicité des emails, format des dates.</summary>
public class PostgresTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Recherche_de_logements_sans_accents_ni_casse()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync(); // Conakry, Kipé, wifi + climatisation

        async Task<List<int>> Chercher(string filtres) =>
            (await f.CreateClient().GetAsync($"/appartement/lister?bailleurId={bailleur.Id}&{filtres}")
                .AttendreDonnees<AppartementResponseDto>()).Select(a => a.Id).ToList();

        Assert.Contains(logement.Id, await Chercher("quartier=kipe"));
        Assert.Contains(logement.Id, await Chercher("quartier=KIP%C3%89&ville=conak"));
        Assert.Contains(logement.Id, await Chercher("equipements=wifi,climatisation"));
        Assert.Empty(await Chercher("quartier=ratoma"));
        Assert.Empty(await Chercher("equipements=parking"));
        // Les jokers SQL saisis sont cherchés tels quels.
        Assert.Empty(await Chercher("quartier=%25"));
    }

    [Fact]
    public async Task Recherche_admin_des_utilisateurs_sans_accents_ni_casse()
    {
        var admin = await f.AdminAsync();
        var compte = await f.CompteAsync("Locataire", "Éloïse Diallo");

        var trouves = await admin.Client.GetAsync("/utilisateur/lister?q=eloise%20diallo").Attendre<PagedResultDto<UtilisateurResponseDto>>();
        Assert.Contains(trouves.Donnees, u => u.Id == compte.Id);
        var parEmail = await admin.Client.GetAsync($"/utilisateur/lister?q={compte.Email.ToUpperInvariant()[..10]}")
            .Attendre<PagedResultDto<UtilisateurResponseDto>>();
        Assert.Contains(parEmail.Donnees, u => u.Id == compte.Id);
    }

    [Fact]
    public async Task Email_unique_sans_tenir_compte_de_la_casse()
    {
        var compte = await f.LocataireAsync();
        var majuscules = compte.Email.ToUpperInvariant();

        await f.CreateClient().PostJson("/utilisateur/inscription", new
        {
            nom = "Doublon", telephone = "622000000", email = majuscules, motDePasse = Api.MotDePasse, role = "Locataire"
        }).Verifier(HttpStatusCode.Conflict);
        // Connexion avec l'email saisi dans une autre casse.
        await f.ConnecterAsync(majuscules, Api.MotDePasse);

        // Inscriptions simultanées avec le même email : l'index unique lower(Email) n'en garde qu'une, l'autre reçoit 409.
        var email = Donnees.NouvelEmail("double");
        var reponses = await Task.WhenAll(new[] { email, email.ToUpperInvariant(), email }.Select(e =>
            f.CreateClient().PostJson("/utilisateur/inscription", new
            {
                nom = "Double clic", telephone = "622000001", email = e, motDePasse = Api.MotDePasse, role = "Locataire"
            })));
        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(reponses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Dates_jour_sans_fuseau_et_horodatages_en_utc()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync();
        var reservation = await voyageur.ReserverOk(logement.Id, 20, 3);

        var json = await (await voyageur.Client.GetAsync($"/reservation/{reservation.Id}")).Content.ReadAsStringAsync();
        var racine = JsonDocument.Parse(json).RootElement;
        // Jour : stocké et relu sans décalage, sérialisé sans « Z ».
        Assert.Equal($"{Api.Jour(20)}T00:00:00", racine.GetProperty("dateArrivee").GetString());
        Assert.Equal($"{Api.Jour(23)}T00:00:00", racine.GetProperty("dateDepart").GetString());
        // Horodatage : UTC avec « Z », à quelques secondes de maintenant.
        var creation = racine.GetProperty("dateCreation").GetString()!;
        Assert.EndsWith("Z", creation);
        var ecart = DateTime.UtcNow - DateTime.Parse(creation, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.InRange(ecart, TimeSpan.FromSeconds(-5), TimeSpan.FromMinutes(1));
    }
}
