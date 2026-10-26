namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Envoi SMTP (MailKit) : chiffrement selon le port, authentification, emails conservés quand le SMTP n'est pas configuré,
/// relance des messages abandonnés ; dossier du stockage disque configurable.
/// </summary>
public class EmailsSmtpTests
{
    private static Task<List<MessageSortant>> MessagesPourAsync(ImmoPlusFactory f, string destinataire) =>
        f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking().Where(m => m.Destinataire == destinataire).OrderBy(m => m.Id).ToListAsync());

    [Theory]
    [InlineData(SecuriteSmtp.Auto, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(SecuriteSmtp.Auto, 587, SecureSocketOptions.StartTls)]
    [InlineData(SecuriteSmtp.Auto, 25, SecureSocketOptions.None)]
    [InlineData(SecuriteSmtp.Auto, 1025, SecureSocketOptions.None)]
    [InlineData(SecuriteSmtp.Auto, 2525, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(SecuriteSmtp.Aucune, 465, SecureSocketOptions.None)]
    [InlineData(SecuriteSmtp.SslImplicite, 2465, SecureSocketOptions.SslOnConnect)]
    [InlineData(SecuriteSmtp.StartTls, 25, SecureSocketOptions.StartTls)]
    public void Chiffrement_choisi_selon_le_reglage_et_le_port(SecuriteSmtp securite, int port, SecureSocketOptions attendu) =>
        Assert.Equal(attendu, EmailServices.OptionSocket(securite, port));

    [Fact]
    public void Reglage_Securite_lu_sans_casse_et_valeur_inconnue_refusee()
    {
        EmailServices Service(string? valeur) => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Securite"] = valeur }).Build());

        Assert.Equal(SecuriteSmtp.Auto, Service(null).Securite);
        Assert.Equal(SecuriteSmtp.Auto, Service("").Securite);
        Assert.Equal(SecuriteSmtp.SslImplicite, Service("sslimplicite").Securite);
        Assert.Equal(SecuriteSmtp.StartTls, Service(" StartTls ").Securite);
        Assert.Throws<InvalidOperationException>(() => Service("Ssl").Securite);
        Assert.Throws<InvalidOperationException>(() => Service("2").Securite);
    }

    [Fact]
    public async Task SMTP_non_configure_emails_conserves_en_attente_puis_envoyes_des_sa_configuration()
    {
        await using var f = ImmoPlusFactory.Creer(new Dictionary<string, string?> { ["Smtp:Host"] = "" });
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "SMTP absent" }).Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/reactiver").Verifier(HttpStatusCode.OK);
        await f.CreateClient().PostJson("/utilisateur/mot-de-passe-oublie", new { email = locataire.Email }).Verifier(HttpStatusCode.OK);

        // Plusieurs passages : rien ne part, aucune tentative consommée, jamais abandonnés.
        Assert.Equal(0, await f.EnvoyerMessagesAsync());
        Assert.Equal(0, await f.EnvoyerMessagesAsync());
        var enFile = await MessagesPourAsync(f, locataire.Email);
        Assert.Contains(enFile, m => m.Sujet!.Contains("désactivé"));
        Assert.Contains(enFile, m => m.Evenement == "ReinitialisationMotDePasse");
        Assert.All(enFile, m => { Assert.Equal(StatutMessageSortant.EnAttente, m.Statut); Assert.Equal(0, m.Tentatives); });
        Assert.Empty(f.Smtp.EmailsPour(locataire.Email));

        // L'administrateur configure le SMTP : tout part au passage suivant (y compris l'alerte « compte à valider » de l'admin).
        var toutEnFile = await f.LireBaseAsync(db => db.MessagesSortants.CountAsync(m => m.Statut == StatutMessageSortant.EnAttente));
        Assert.True(toutEnFile > enFile.Count);
        f.Services.GetRequiredService<IConfiguration>()["Smtp:Host"] = "127.0.0.1";
        Assert.Equal(toutEnFile, await f.EnvoyerMessagesAsync());
        Assert.Equal(enFile.Count, f.Smtp.EmailsPour(locataire.Email).Count);
        Assert.All(await MessagesPourAsync(f, locataire.Email), m => { Assert.Equal(StatutMessageSortant.Envoye, m.Statut); Assert.Equal(1, m.Tentatives); });
    }

    [Fact]
    public async Task Authentification_SMTP_avec_identifiant_et_mot_de_passe()
    {
        const string motDePasse = "mot-de-passe-smtp-secret";
        await using var f = ImmoPlusFactory.Creer(new Dictionary<string, string?>
        {
            ["Smtp:Utilisateur"] = "noreply@mail.immoplus.test",
            ["Smtp:MotDePasse"] = motDePasse,
            ["Smtp:Securite"] = "Aucune",
            ["Smtp:NomExpediteur"] = "ImmoPlus Tests"
        });
        f.Smtp.IdentifiantsExiges = ("noreply@mail.immoplus.test", motDePasse);
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        await f.EnvoyerMessagesAsync();

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Authentification" }).Verifier(HttpStatusCode.OK);
        Assert.Single(await f.EmailsPourAsync(locataire.Email), e => e.Sujet.Contains("désactivé"));
        Assert.True(f.Smtp.AuthentificationsReussies >= 1);

        // Mot de passe refusé par le serveur : échec avec nouvel essai, et le mot de passe n'apparaît pas dans l'erreur.
        f.Smtp.IdentifiantsExiges = ("noreply@mail.immoplus.test", "autre-mot-de-passe");
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/reactiver").Verifier(HttpStatusCode.OK);
        await f.EnvoyerMessagesAsync();
        var echec = (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Sujet!.Contains("réactivé"));
        Assert.Equal(StatutMessageSortant.Echec, echec.Statut);
        Assert.NotNull(echec.DerniereErreur);
        Assert.DoesNotContain(motDePasse, echec.DerniereErreur);
    }

    [Fact]
    public async Task Relance_des_messages_abandonnes_par_l_admin()
    {
        await using var f = ImmoPlusFactory.Creer();
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        await f.EnvoyerMessagesAsync();

        // Serveur en panne pendant les 5 tentatives : abandon.
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Relance" }).Verifier(HttpStatusCode.OK);
        var id = (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Sujet!.Contains("désactivé")).Id;
        for (var tentative = 1; tentative <= EnvoiMessagesServices.MaxTentatives; tentative++)
        {
            f.Smtp.RefuserProchainsEnvois(1);
            await f.ModifierBaseAsync(async db =>
                (await db.MessagesSortants.SingleAsync(m => m.Id == id)).ProchaineTentative = DateTime.UtcNow.AddSeconds(-1));
            await f.EnvoyerMessagesAsync();
        }
        f.Smtp.RefuserProchainsEnvois(0);
        Assert.Equal(StatutMessageSortant.Abandonne, (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Id == id).Statut);

        // Refus : autres rôles, statut absent ou non relançable, message déjà envoyé ou inconnu.
        await locataire.Client.PostJson("/admin/messages-sortants/relancer?statut=Abandonne").Verifier(HttpStatusCode.Unauthorized);
        await (await f.BailleurAsync()).Client.PostJson("/admin/messages-sortants/relancer?statut=Abandonne").Verifier(HttpStatusCode.Forbidden);
        await admin.Client.PostJson("/admin/messages-sortants/relancer").Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson("/admin/messages-sortants/relancer?statut=Envoye").Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson("/admin/messages-sortants/999999/relancer").Erreur(HttpStatusCode.NotFound);
        var adresseEnvoye = Donnees.NouvelEmail("envoye");
        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BoiteEnvoiServices>().AjouterEmailAsync(adresseEnvoye, null, "Déjà parti", "<p>x</p>", "Test", null);
        }
        await f.EnvoyerMessagesAsync();
        var envoye = Assert.Single(await MessagesPourAsync(f, adresseEnvoye), m => m.Statut == StatutMessageSortant.Envoye).Id;
        await admin.Client.PostJson($"/admin/messages-sortants/{envoye}/relancer").Erreur(HttpStatusCode.Conflict);

        // Relance : 5 nouvelles tentatives, envoi au passage suivant.
        var resultat = await admin.Client.PostJson("/admin/messages-sortants/relancer?statut=Abandonne").Attendre<RelanceMessagesDto>();
        Assert.Equal(1, resultat.Relances);
        var relance = (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Id == id);
        Assert.Equal(StatutMessageSortant.EnAttente, relance.Statut);
        Assert.Equal(0, relance.Tentatives);
        Assert.Single(await f.EmailsPourAsync(locataire.Email), e => e.Sujet.Contains("désactivé"));
        Assert.Equal(StatutMessageSortant.Envoye, (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Id == id).Statut);
        Assert.Equal(0, (await admin.Client.PostJson("/admin/messages-sortants/relancer?statut=Abandonne").Attendre<RelanceMessagesDto>()).Relances);

        // Relance d'un seul message (par son n°), tracée dans le journal.
        await f.ModifierBaseAsync(async db =>
        {
            var message = await db.MessagesSortants.SingleAsync(m => m.Id == id);
            message.Statut = StatutMessageSortant.Abandonne;
            message.Tentatives = EnvoiMessagesServices.MaxTentatives;
        });
        Assert.Equal(1, (await admin.Client.PostJson($"/admin/messages-sortants/{id}/relancer").Attendre<RelanceMessagesDto>()).Relances);
        Assert.Equal(StatutMessageSortant.EnAttente, (await MessagesPourAsync(f, locataire.Email)).Single(m => m.Id == id).Statut);
        Assert.True(await f.LireBaseAsync(db => db.JournalActions.AnyAsync(j => j.Action == "message-sortant.relancer" && j.CibleId == id)));
    }

    [Fact]
    public async Task Dossier_du_stockage_disque_configurable()
    {
        var racine = Path.GetFullPath("/api");
        Assert.Equal(Path.Combine(racine, "Storage"), StockageDisque.DossierConfigure(new ConfigurationBuilder().Build(), racine));
        Assert.Equal(Path.Combine(racine, "fichiers"), StockageDisque.DossierConfigure(Config("fichiers"), racine));
        Assert.Equal(Path.GetFullPath("/var/lib/immoplus/storage"), StockageDisque.DossierConfigure(Config("/var/lib/immoplus/storage"), racine));

        var dossier = Path.Combine(Path.GetTempPath(), "immoplus-tests", "stockage-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var f = ImmoPlusFactory.Creer(new Dictionary<string, string?> { ["Stockage:Mode"] = "Disque", ["Stockage:Disque:Dossier"] = dossier });
            var stockage = f.Services.GetRequiredService<IStockageFichiers>();
            await stockage.EnregistrerAsync("photos/1/essai.png", new MemoryStream(Api.Png), "image/png");

            Assert.True(File.Exists(Path.Combine(dossier, "photos", "1", "essai.png")));
            Assert.False(Directory.Exists(Path.Combine(f.Dossier, "Storage", "photos")));
            Assert.True(await stockage.ExisteAsync("photos/1/essai.png"));
        }
        finally
        {
            if (Directory.Exists(dossier))
            {
                Directory.Delete(dossier, recursive: true);
            }
        }

        static IConfiguration Config(string dossier) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stockage:Disque:Dossier"] = dossier }).Build();
    }
}
