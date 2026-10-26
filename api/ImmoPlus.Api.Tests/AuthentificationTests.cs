namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Inscription, validation par l'admin, connexion, jeton et désactivation.</summary>
public class AuthentificationTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    [Fact]
    public async Task Inscription_compte_en_attente_puis_connexion_apres_confirmation_et_validation()
    {
        var admin = await f.AdminAsync();
        var email = Donnees.NouvelEmail("locataire");
        var anonyme = f.CreateClient();

        var cree = await anonyme.PostJson("/utilisateur/inscription", new
        {
            nom = "Mariama Test", telephone = "622000000", email, motDePasse = Api.MotDePasse, role = "Locataire"
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        Assert.Equal("EnAttente", cree.Statut);
        Assert.Null(cree.Token);

        var nonConfirme = await anonyme.PostJson("/utilisateur/connexion", new { email, motDePasse = Api.MotDePasse })
            .Erreur(HttpStatusCode.Conflict);
        Assert.Contains("Confirmez d'abord votre adresse email", nonConfirme);
        await anonyme.PostJson("/utilisateur/confirmer-email", new { jeton = await f.JetonConfirmationAsync(email) }).Verifier(HttpStatusCode.OK);

        var attente = await anonyme.PostJson("/utilisateur/connexion", new { email, motDePasse = Api.MotDePasse })
            .Erreur(HttpStatusCode.Conflict);
        Assert.Contains("attente de validation", attente);

        await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Verifier(HttpStatusCode.OK);

        // Email insensible à la casse.
        var connecte = await anonyme.PostJson("/utilisateur/connexion", new { email = email.ToUpperInvariant(), motDePasse = Api.MotDePasse })
            .Attendre<UtilisateurResponseDto>();
        Assert.False(string.IsNullOrEmpty(connecte.Token));

        var moi = await f.CreateClient().AvecJeton(connecte.Token!).GetAsync("/utilisateur/moi").Attendre<UtilisateurResponseDto>();
        Assert.Equal(cree.Id, moi.Id);
        Assert.Equal("Locataire", moi.Role);
    }

    [Fact]
    public async Task Inscription_refuse_les_saisies_invalides_et_les_doublons()
    {
        await f.AdminAsync();
        var anonyme = f.CreateClient();
        var email = Donnees.NouvelEmail("doublon");
        object Inscription(string mail, string mdp = Api.MotDePasse, string role = "Locataire") =>
            new { nom = "Test Doublon", telephone = "623000000", email = mail, motDePasse = mdp, role };

        await anonyme.PostJson("/utilisateur/inscription", Inscription(email)).Verifier(HttpStatusCode.Created);
        await anonyme.PostJson("/utilisateur/inscription", Inscription(email.ToUpperInvariant())).Erreur(HttpStatusCode.Conflict);
        await anonyme.PostJson("/utilisateur/inscription", Inscription(Donnees.NouvelEmail("court"), "12345")).Erreur(HttpStatusCode.BadRequest);
        await anonyme.PostJson("/utilisateur/inscription", Inscription("pas-un-email")).Erreur(HttpStatusCode.BadRequest);
        await anonyme.PostJson("/utilisateur/inscription", Inscription(Donnees.NouvelEmail("role"), role: "Proprietaire")).Erreur(HttpStatusCode.BadRequest);

        // Un admin actif existe : le rôle Admin n'est plus proposé à l'inscription publique.
        var message = await anonyme.PostJson("/utilisateur/inscription", Inscription(Donnees.NouvelEmail("faux-admin"), role: "Admin"))
            .Erreur(HttpStatusCode.BadRequest);
        Assert.Contains("administrateur", message);
    }

    [Fact]
    public async Task Seul_un_admin_cree_un_admin()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        object Corps() => new { nom = "Nouvel Admin", telephone = "624000000", email = Donnees.NouvelEmail("admin2"), motDePasse = Api.MotDePasse };

        await locataire.Client.PostJson("/utilisateur/admin", Corps()).Verifier(HttpStatusCode.Forbidden);
        await f.CreateClient().PostJson("/utilisateur/admin", Corps()).Verifier(HttpStatusCode.Unauthorized);
        var cree = await admin.Client.PostJson("/utilisateur/admin", Corps()).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        Assert.Equal("Admin", cree.Role);
        Assert.Equal("Valide", cree.Statut);
    }

    [Fact]
    public async Task Mauvais_mot_de_passe_et_routes_protegees()
    {
        var locataire = await f.LocataireAsync();
        var message = await f.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = "mauvais" })
            .Erreur(HttpStatusCode.NotFound);
        Assert.Equal("Email ou mot de passe incorrect", message);

        await f.CreateClient().GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.Unauthorized);
        await f.CreateClient().AvecJeton("jeton.invalide.signature").GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.Unauthorized);
        await locataire.Client.GetAsync("/utilisateur/lister").Verifier(HttpStatusCode.Forbidden);
        await locataire.Client.GetAsync("/admin/tableau-de-bord").Verifier(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Compte_desactive_bloque_immediatement_le_jeton_et_la_connexion()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        await locataire.Client.GetAsync("/utilisateur/moi").Verifier(HttpStatusCode.OK);

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Test de désactivation" })
            .Verifier(HttpStatusCode.OK);

        var message = await locataire.Client.GetAsync("/utilisateur/moi").Erreur(HttpStatusCode.Unauthorized);
        Assert.Contains("désactivé", message);
        await f.CreateClient().PostJson("/utilisateur/connexion", new { email = locataire.Email, motDePasse = Api.MotDePasse })
            .Erreur(HttpStatusCode.Conflict);

        // L'admin ne peut pas se désactiver lui-même.
        await admin.Client.PostJson($"/utilisateur/{admin.Id}/desactiver", new { }).Erreur(HttpStatusCode.Conflict);

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/reactiver").Verifier(HttpStatusCode.OK);
        await f.ConnecterAsync(locataire.Email, Api.MotDePasse);
    }
}
