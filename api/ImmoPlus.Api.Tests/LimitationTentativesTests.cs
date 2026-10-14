namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Text.RegularExpressions;
using ImmoPlus.Api.Tests.Outils;

/// <summary>
/// Limitation des essais de connexion et des demandes de mot de passe oublié. Chaque test démarre sa propre API :
/// les compteurs sont en mémoire et partagés par toutes les requêtes du serveur de test (même adresse IP).
/// </summary>
public class LimitationTentativesTests
{
    private static Task<HttpResponseMessage> Connexion(ImmoPlusFactory f, string email, string motDePasse) =>
        f.CreateClient().PostJson("/utilisateur/connexion", new { email, motDePasse });

    [Fact]
    public async Task Cinq_echecs_bloquent_l_email_meme_avec_le_bon_mot_de_passe()
    {
        await using var f = new ImmoPlusFactory();
        var locataire = await f.LocataireAsync();
        var autre = await f.LocataireAsync();

        for (var i = 0; i < 5; i++)
        {
            await Connexion(f, locataire.Email, "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        // Bloqué 15 min, y compris avec le bon mot de passe et une autre casse.
        var message = await Connexion(f, locataire.Email.ToUpperInvariant(), Api.MotDePasse).Erreur(HttpStatusCode.TooManyRequests);
        Assert.Matches(new Regex(@"Trop de tentatives de connexion\. Réessayez dans 1[45] minutes\."), message);

        // Les autres comptes ne sont pas touchés.
        await Connexion(f, autre.Email, Api.MotDePasse).Verifier(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_connexion_reussie_remet_le_compteur_de_l_email_a_zero()
    {
        await using var f = new ImmoPlusFactory();
        var locataire = await f.LocataireAsync();

        for (var i = 0; i < 4; i++)
        {
            await Connexion(f, locataire.Email, "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        await Connexion(f, locataire.Email, Api.MotDePasse).Verifier(HttpStatusCode.OK);
        for (var i = 0; i < 4; i++)
        {
            await Connexion(f, locataire.Email, "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        await Connexion(f, locataire.Email, Api.MotDePasse).Verifier(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Vingt_echecs_depuis_la_meme_adresse_IP_bloquent_tous_les_emails()
    {
        await using var f = new ImmoPlusFactory();
        var locataire = await f.LocataireAsync();

        // 20 emails différents (inconnus) : aucun n'atteint 5 échecs, mais l'adresse IP oui.
        for (var i = 0; i < 20; i++)
        {
            await Connexion(f, $"inconnu{i}@tests.immoplus.test", "mauvais").Verifier(HttpStatusCode.NotFound);
        }
        await Connexion(f, locataire.Email, Api.MotDePasse).Erreur(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Mot_de_passe_oublie_cinq_emails_au_plus_puis_reinitialisation()
    {
        await using var f = new ImmoPlusFactory();
        var locataire = await f.LocataireAsync();
        var anonyme = f.CreateClient();

        for (var i = 0; i < 7; i++)
        {
            await anonyme.PostJson("/utilisateur/mot-de-passe-oublie", new { email = locataire.Email }).Verifier(HttpStatusCode.OK);
        }
        // Email inconnu : même réponse, pour ne pas révéler les comptes existants.
        await anonyme.PostJson("/utilisateur/mot-de-passe-oublie", new { email = "personne@tests.immoplus.test" }).Verifier(HttpStatusCode.OK);

        var emails = f.Smtp.EmailsPour(locataire.Email).Where(e => e.Sujet.Contains("Réinitialisation")).ToList();
        Assert.Equal(5, emails.Count);
        Assert.Empty(f.Smtp.EmailsPour("personne@tests.immoplus.test"));

        // Seul le dernier lien envoyé est valable.
        var jeton = Regex.Match(emails[^1].CorpsHtml, "jeton=([0-9a-f]+)").Groups[1].Value;
        Assert.StartsWith("http://front.immoplus.test/reinitialiser-mot-de-passe", Regex.Match(emails[^1].CorpsHtml, "href=\"([^\"]+)\"").Groups[1].Value);
        var ancien = Regex.Match(emails[0].CorpsHtml, "jeton=([0-9a-f]+)").Groups[1].Value;
        await anonyme.PostJson("/utilisateur/reinitialiser-mot-de-passe", new { jeton = ancien, nouveauMotDePasse = "Nouveau!2026" })
            .Erreur(HttpStatusCode.BadRequest);
        await anonyme.PostJson("/utilisateur/reinitialiser-mot-de-passe", new { jeton, nouveauMotDePasse = "Nouveau!2026" })
            .Verifier(HttpStatusCode.NoContent);

        await f.ConnecterAsync(locataire.Email, "Nouveau!2026");
        await Connexion(f, locataire.Email, Api.MotDePasse).Verifier(HttpStatusCode.NotFound);
        // Un lien ne sert qu'une fois.
        await anonyme.PostJson("/utilisateur/reinitialiser-mot-de-passe", new { jeton, nouveauMotDePasse = "Encore!2026" })
            .Erreur(HttpStatusCode.BadRequest);
    }
}
