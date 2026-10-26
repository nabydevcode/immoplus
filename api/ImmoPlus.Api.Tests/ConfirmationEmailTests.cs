namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Inscription → confirmation de l'adresse email par lien → admins prévenus → validation par l'admin → email.</summary>
public class ConfirmationEmailTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private const string SujetConfirmation = "Confirmez votre adresse email";

    private async Task<UtilisateurResponseDto> InscrireAsync(string email, string role = "Locataire", string nom = "Fatoumata Test") =>
        await f.CreateClient().PostJson("/utilisateur/inscription", new
        {
            nom, telephone = "622000000", email, motDePasse = Api.MotDePasse, role
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);

    private Task<HttpResponseMessage> Confirmer(string jeton) => f.CreateClient().PostJson("/utilisateur/confirmer-email", new { jeton });

    private Task<HttpResponseMessage> Connexion(string email) =>
        f.CreateClient().PostJson("/utilisateur/connexion", new { email, motDePasse = Api.MotDePasse });

    private static async Task<(string Message, string? Code)> ErreurAvecCode(Task<HttpResponseMessage> requete, HttpStatusCode attendu)
    {
        var reponse = await (await requete).Verifier(attendu);
        using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("message").GetString()!,
            doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private Task<int> JournalAsync(string action, int utilisateurId) =>
        f.LireBaseAsync(db => db.JournalActions.CountAsync(j => j.Action == action && j.CibleId == utilisateurId));

    [Fact]
    public async Task Parcours_complet_inscription_confirmation_validation()
    {
        var admin = await f.AdminAsync();
        var secondAdmin = await admin.Client.PostJson("/utilisateur/admin", new
        {
            nom = "Second Admin", telephone = "624000000", email = Donnees.NouvelEmail("admin-b"), motDePasse = Api.MotDePasse
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        var email = Donnees.NouvelEmail("bailleur");
        var nom = "Ibrahima Confirmation";

        var cree = await InscrireAsync(email, "Bailleur", nom);
        Assert.Equal("EnAttente", cree.Statut);
        Assert.False(cree.EmailConfirme);
        Assert.Null(cree.DateConfirmationEmail);

        // Email de confirmation : lien vers le front, jeton de 32 octets (64 caractères hexadécimaux), seul son hash en base.
        var recu = Assert.Single(await f.EmailsPourAsync(email), e => e.Sujet.Contains(SujetConfirmation));
        var lien = Regex.Match(recu.CorpsHtml, "http://front\\.immoplus\\.test/confirmer-email\\?jeton=([0-9a-f]{64})");
        Assert.True(lien.Success, recu.CorpsHtml);
        Assert.Contains("un administrateur ImmoPlus validera votre compte", recu.CorpsHtml);
        var jeton = lien.Groups[1].Value;
        var enBase = await f.LireBaseAsync(db => db.Utilisateurs.AsNoTracking().SingleAsync(u => u.Id == cree.Id));
        Assert.NotEqual(jeton, enBase.JetonConfirmationEmailHash, StringComparer.OrdinalIgnoreCase);
        Assert.InRange(enBase.JetonConfirmationEmailExpiration!.Value, DateTime.UtcNow.AddHours(47), DateTime.UtcNow.AddHours(49));
        // Pas encore d'alerte aux admins : l'adresse n'est pas confirmée.
        Assert.DoesNotContain(await f.EmailsPourAsync(admin.Email), e => e.Sujet.Contains(nom));

        // Onglet « Email non confirmé ».
        var nonConfirmes = await admin.Client.GetAsync($"/utilisateur/lister?statut=EnAttente&emailConfirme=false&q={Uri.EscapeDataString(email)}")
            .AttendreDonnees<UtilisateurResponseDto>();
        Assert.False(Assert.Single(nonConfirmes).EmailConfirme);

        // Avant confirmation : ni validation, ni connexion (code exploitable par le front).
        var refus = await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Erreur(HttpStatusCode.Conflict);
        Assert.Contains("pas encore confirmée", refus);
        var (message, code) = await ErreurAvecCode(Connexion(email), HttpStatusCode.Conflict);
        Assert.Equal("EmailNonConfirme", code);
        Assert.Equal(UtilisateurServices.MessageEmailNonConfirme, message);

        var confirmation = await Confirmer(jeton).Attendre<ConfirmationEmailResponseDto>();
        Assert.Equal("EnAttente", confirmation.Statut);
        Assert.StartsWith("Adresse confirmée", confirmation.Message);
        Assert.Equal(1, await JournalAsync("utilisateur.confirmer-email", cree.Id));

        // Tous les admins actifs prévenus, avec un lien vers la fiche filtrée sur l'email.
        foreach (var adresse in new[] { admin.Email, secondAdmin.Email })
        {
            var alerte = Assert.Single(await f.EmailsPourAsync(adresse), e => e.Sujet == $"Nouveau compte à valider : {nom} (Bailleur)");
            Assert.Contains($"http://front.immoplus.test/admin/utilisateurs?q={Uri.EscapeDataString(email)}", alerte.CorpsHtml);
        }

        // Second clic sur le lien : réponse idempotente, aucune nouvelle alerte.
        var encore = await Confirmer(jeton).Attendre<ConfirmationEmailResponseDto>();
        Assert.Contains("déjà confirmée", encore.Message);
        Assert.Single(await f.EmailsPourAsync(admin.Email), e => e.Sujet.Contains(nom));
        Assert.Equal(1, await JournalAsync("utilisateur.confirmer-email", cree.Id));

        // Onglet « À valider » ; connexion : message habituel d'attente, sans code.
        var aValider = await admin.Client.GetAsync($"/utilisateur/lister?statut=EnAttente&emailConfirme=true&q={Uri.EscapeDataString(email)}")
            .AttendreDonnees<UtilisateurResponseDto>();
        var ligne = Assert.Single(aValider);
        Assert.True(ligne.EmailConfirme);
        Assert.NotNull(ligne.DateConfirmationEmail);
        var (attente, codeAttente) = await ErreurAvecCode(Connexion(email), HttpStatusCode.Conflict);
        Assert.Contains("attente de validation", attente);
        Assert.Null(codeAttente);
        Assert.True((await admin.Client.GetAsync("/admin/tableau-de-bord").Attendre<TableauDeBordDto>()).UtilisateursAValider >= 1);

        var valide = await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Attendre<UtilisateurResponseDto>();
        Assert.Equal("Valide", valide.Statut);
        var bienvenue = Assert.Single(await f.EmailsPourAsync(email), e => e.Sujet == "Votre compte ImmoPlus est validé");
        Assert.Contains("http://front.immoplus.test/login", bienvenue.CorpsHtml);

        var connecte = await Connexion(email).Attendre<UtilisateurResponseDto>();
        Assert.True(connecte.EmailConfirme);
        Assert.False(string.IsNullOrEmpty(connecte.Token));
        Assert.Contains("vous pouvez vous connecter", (await Confirmer(jeton).Attendre<ConfirmationEmailResponseDto>()).Message);
    }

    [Fact]
    public async Task Jetons_inconnu_vide_expire_ou_remplace_refuses()
    {
        await f.AdminAsync();
        Assert.Contains("nouvel email", await Confirmer(new string('a', 64)).Erreur(HttpStatusCode.BadRequest));
        Assert.Contains("nouvel email", await Confirmer("").Erreur(HttpStatusCode.BadRequest));

        var email = Donnees.NouvelEmail("expire");
        var cree = await InscrireAsync(email);
        var ancien = await f.JetonConfirmationAsync(email);
        await f.ModifierBaseAsync(async db =>
            (await db.Utilisateurs.SingleAsync(u => u.Id == cree.Id)).JetonConfirmationEmailExpiration = DateTime.UtcNow.AddMinutes(-1));
        Assert.Contains("expiré", await Confirmer(ancien).Erreur(HttpStatusCode.BadRequest));

        // Nouvel email : l'ancien lien ne sert plus, le nouveau confirme.
        await f.CreateClient().PostJson("/utilisateur/renvoyer-confirmation", new { email }).Verifier(HttpStatusCode.OK);
        var nouveau = await f.JetonConfirmationAsync(email);
        Assert.NotEqual(ancien, nouveau);
        await Confirmer(ancien).Erreur(HttpStatusCode.BadRequest);
        await Confirmer(nouveau).Verifier(HttpStatusCode.OK);
        Assert.True(await f.LireBaseAsync(db => db.Utilisateurs.Where(u => u.Id == cree.Id).Select(u => u.EmailConfirme).SingleAsync()));
    }

    [Fact]
    public async Task Renvoi_neutre_et_limite_a_trois_par_heure()
    {
        await f.AdminAsync();
        var anonyme = f.CreateClient();
        async Task<string> Renvoyer(string adresse)
        {
            var corps = await anonyme.PostJson("/utilisateur/renvoyer-confirmation", new { email = adresse })
                .Attendre<Dictionary<string, string>>();
            return corps["message"];
        }

        // Adresse inconnue : même réponse, aucun email.
        var inconnue = Donnees.NouvelEmail("inconnu");
        Assert.Equal(UtilisateurServices.MessageRenvoiConfirmation, await Renvoyer(inconnue));
        Assert.Empty(await f.EmailsPourAsync(inconnue));

        // Compte non confirmé : 3 renvois par heure (en plus de l'email d'inscription), puis la même réponse sans email.
        var email = Donnees.NouvelEmail("renvoi");
        var cree = await InscrireAsync(email);
        for (var i = 0; i < LimiteurTentatives.MaxRenvoisConfirmation + 2; i++)
        {
            Assert.Equal(UtilisateurServices.MessageRenvoiConfirmation, await Renvoyer(i % 2 == 0 ? email : email.ToUpperInvariant()));
        }
        Assert.Equal(1 + LimiteurTentatives.MaxRenvoisConfirmation,
            (await f.EmailsPourAsync(email)).Count(e => e.Sujet.Contains(SujetConfirmation)));
        Assert.Equal(LimiteurTentatives.MaxRenvoisConfirmation, await JournalAsync("utilisateur.renvoyer-confirmation", cree.Id));

        // Compte déjà confirmé (ou validé) : aucun email.
        var locataire = await f.LocataireAsync();
        var avant = (await f.EmailsPourAsync(locataire.Email)).Count;
        Assert.Equal(UtilisateurServices.MessageRenvoiConfirmation, await Renvoyer(locataire.Email));
        Assert.Equal(avant, (await f.EmailsPourAsync(locataire.Email)).Count);
    }

    [Fact]
    public async Task Admin_cree_par_un_admin_et_compte_d_amorcage_confirmes_d_office()
    {
        var admin = await f.AdminAsync();
        var moi = await admin.Client.GetAsync("/utilisateur/moi").Attendre<UtilisateurResponseDto>();
        Assert.True(moi.EmailConfirme);

        var adresse = Donnees.NouvelEmail("admin-c");
        var cree = await admin.Client.PostJson("/utilisateur/admin", new
        {
            nom = "Admin Office", telephone = "624000001", email = adresse, motDePasse = Api.MotDePasse
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        Assert.True(cree.EmailConfirme);
        Assert.NotNull(cree.DateConfirmationEmail);
        Assert.DoesNotContain(await f.EmailsPourAsync(adresse), e => e.Sujet.Contains(SujetConfirmation));
        Assert.True((await Connexion(adresse).Attendre<UtilisateurResponseDto>()).EmailConfirme);
    }

    [Fact]
    public async Task Refus_previent_seulement_une_adresse_confirmee()
    {
        var admin = await f.AdminAsync();
        var nonConfirme = await InscrireAsync(Donnees.NouvelEmail("refus-a"));
        await admin.Client.PostJson($"/utilisateur/{nonConfirme.Id}/refuser").Verifier(HttpStatusCode.OK);
        Assert.DoesNotContain(await f.EmailsPourAsync(nonConfirme.Email), e => e.Sujet.Contains("pas été retenue"));

        var email = Donnees.NouvelEmail("refus-b");
        var confirme = await InscrireAsync(email);
        await Confirmer(await f.JetonConfirmationAsync(email)).Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson($"/utilisateur/{confirme.Id}/refuser").Verifier(HttpStatusCode.OK);
        Assert.Single(await f.EmailsPourAsync(email), e => e.Sujet == "Votre inscription n'a pas été retenue");
        Assert.Contains("pas été validé", await Connexion(email).Erreur(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Compte_en_attente_anterieur_a_la_confirmation_reste_validable()
    {
        // Compte en attente au moment de la migration : EmailConfirme = true, sans date de confirmation.
        var admin = await f.AdminAsync();
        var email = Donnees.NouvelEmail("ancien");
        var cree = await InscrireAsync(email);
        await f.ModifierBaseAsync(async db =>
        {
            var u = await db.Utilisateurs.SingleAsync(x => x.Id == cree.Id);
            u.EmailConfirme = true;
            u.DateConfirmationEmail = null;
            u.JetonConfirmationEmailHash = null;
            u.JetonConfirmationEmailExpiration = null;
        });
        Assert.Contains("attente de validation", await Connexion(email).Erreur(HttpStatusCode.Conflict));
        await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Verifier(HttpStatusCode.OK);
        await Connexion(email).Verifier(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Migration_confirme_tous_les_comptes_existants()
    {
        var nom = "immoplus_test_" + Guid.NewGuid().ToString("N");
        var chaine = await ServeurPostgresTests.CreerBaseAsync(nom);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            AppDbContext.Configurer(options, chaine);
            await using var db = new AppDbContext(options.Options);
            var migrateur = db.GetService<IMigrator>();
            // Base dans l'état précédent, avec des comptes de chaque statut, puis migration.
            await migrateur.MigrateAsync("20261025112445_CopieEnvoyesImap");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Utilisateurs" ("Nom", "Telephone", "Email", "MotDePasseHash", "Role", "Statut", "NotificationsSms", "IdentiteVerifiee")
                VALUES ('Valide', '1', 'a@x.test', 'h', 2, 1, TRUE, FALSE), ('Attente', '2', 'b@x.test', 'h', 1, 0, TRUE, FALSE),
                       ('Desactive', '3', 'c@x.test', 'h', 2, 3, TRUE, FALSE), ('Admin', '4', 'd@x.test', 'h', 0, 1, TRUE, FALSE)
                """);
            await migrateur.MigrateAsync();

            var comptes = await db.Utilisateurs.AsNoTracking().ToListAsync();
            Assert.Equal(4, comptes.Count);
            Assert.All(comptes, u => Assert.True(u.EmailConfirme));
            Assert.All(comptes, u => Assert.Null(u.JetonConfirmationEmailHash));
        }
        finally
        {
            await ServeurPostgresTests.SupprimerBaseAsync(nom, chaine);
        }
    }
}
