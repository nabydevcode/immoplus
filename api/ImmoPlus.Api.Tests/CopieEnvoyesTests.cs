namespace ImmoPlus.Api.Tests;

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
/// Copie IMAP des emails envoyés dans le dossier « Envoyés » de l'expéditeur (Imap:Actif), contre un faux serveur IMAP local :
/// désactivée par défaut, dossier spécial \Sent ou noms usuels, connexion réutilisée, échec sans effet sur l'email et réessayé.
/// </summary>
public class CopieEnvoyesTests
{
    private const string Utilisateur = "kadiatou@immoplus.test";
    private const string MotDePasse = "mot-de-passe-boite-secret";

    private static ImmoPlusFactory Factory(FauxServeurImap imap, Dictionary<string, string?>? reglages = null)
    {
        var parametres = new Dictionary<string, string?>
        {
            ["Imap:Actif"] = "true",
            ["Imap:Host"] = "127.0.0.1",
            ["Imap:Port"] = imap.Port.ToString(),
            ["Imap:Securite"] = "Aucune",
            // Imap:Utilisateur et Imap:MotDePasse vides : ceux du SMTP servent.
            ["Smtp:Utilisateur"] = Utilisateur,
            ["Smtp:MotDePasse"] = MotDePasse,
            ["Smtp:Securite"] = "Aucune"
        };
        foreach (var (cle, valeur) in reglages ?? [])
        {
            parametres[cle] = valeur;
        }
        var f = ImmoPlusFactory.Creer(parametres);
        f.Smtp.IdentifiantsExiges = (Utilisateur, MotDePasse);
        imap.IdentifiantsExiges = (Utilisateur, MotDePasse);
        return f;
    }

    private static async Task<string> AjouterEmailAsync(ImmoPlusFactory f, string sujet)
    {
        var adresse = Donnees.NouvelEmail("copie");
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BoiteEnvoiServices>()
            .AjouterEmailAsync(adresse, null, sujet, $"<p>{sujet}</p>", "TestCopie", "Dossier n° 000042");
        return adresse;
    }

    private static Task<MessageSortant> MessageAsync(ImmoPlusFactory f, string destinataire) =>
        f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking().SingleAsync(m => m.Destinataire == destinataire));

    private static Task ReculerCopieAsync(ImmoPlusFactory f, string destinataire) =>
        f.ModifierBaseAsync(async db =>
            (await db.MessagesSortants.SingleAsync(m => m.Destinataire == destinataire)).CopieProchaineTentative = DateTime.UtcNow.AddSeconds(-1));

    [Theory]
    [InlineData(SecuriteSmtp.Auto, 993, SecureSocketOptions.SslOnConnect)]
    [InlineData(SecuriteSmtp.Auto, 143, SecureSocketOptions.StartTls)]
    [InlineData(SecuriteSmtp.Auto, 1143, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(SecuriteSmtp.Aucune, 993, SecureSocketOptions.None)]
    [InlineData(SecuriteSmtp.SslImplicite, 143, SecureSocketOptions.SslOnConnect)]
    [InlineData(SecuriteSmtp.StartTls, 993, SecureSocketOptions.StartTls)]
    public void Chiffrement_IMAP_selon_le_reglage_et_le_port(SecuriteSmtp securite, int port, SecureSocketOptions attendu) =>
        Assert.Equal(attendu, CopieEnvoyesServices.OptionSocket(securite, port));

    [Fact]
    public async Task Desactivee_par_defaut_aucune_connexion_IMAP()
    {
        await using var imap = new FauxServeurImap(specialUse: true);
        // Hôte et port renseignés mais Imap:Actif absent : rien ne change.
        await using var f = ImmoPlusFactory.Creer(new Dictionary<string, string?>
        {
            ["Imap:Host"] = "127.0.0.1",
            ["Imap:Port"] = imap.Port.ToString()
        });
        var adresse = await AjouterEmailAsync(f, "Sans copie");
        Assert.Single(await f.EmailsPourAsync(adresse));

        var message = await MessageAsync(f, adresse);
        Assert.Equal(StatutMessageSortant.Envoye, message.Statut);
        Assert.Null(message.CopieEnvoyes);
        Assert.Equal(0, message.CopieTentatives);
        Assert.Equal(0, imap.Connexions);
    }

    [Fact]
    public async Task Copie_deposee_dans_le_dossier_special_Sent_avec_une_connexion_reutilisee()
    {
        // Nom quelconque : seul l'attribut \Sent annoncé par SPECIAL-USE compte (le dossier « Sent » est un leurre).
        await using var imap = new FauxServeurImap(true, ("INBOX", null), ("Sent", null), ("INBOX.Messages-envoyes", @"\Sent"));
        await using var f = Factory(imap);
        var admin = await f.AdminAsync();
        await f.EnvoyerMessagesAsync();
        var avant = imap.Depots.Count;

        var premier = await AjouterEmailAsync(f, "Dossier accepté");
        var second = await AjouterEmailAsync(f, "Apport reçu");
        var envoye = Assert.Single(await f.EmailsPourAsync(premier));
        Assert.Single(f.Smtp.EmailsPour(second));

        var depots = imap.Depots.Skip(avant).ToList();
        Assert.Equal(2, depots.Count);
        Assert.All(depots, d => { Assert.Equal("INBOX.Messages-envoyes", d.Dossier); Assert.Contains(@"\Seen", d.Drapeaux); });
        // Même message que celui reçu : Message-Id, date, objet, destinataire et corps.
        var copie = depots.Single(d => d.Message.To.Mailboxes.Single().Address == premier).Message;
        Assert.Equal(envoye.MessageId, copie.MessageId);
        Assert.Equal(envoye.Date, copie.Date);
        Assert.Equal("Dossier accepté", copie.Subject);
        Assert.Contains("Dossier accepté", copie.HtmlBody);
        Assert.StartsWith("immoplus.", copie.MessageId);
        // Une seule connexion pour tous les emails (compte de l'admin compris).
        Assert.Equal(1, imap.Connexions);

        var message = await MessageAsync(f, premier);
        Assert.Equal(StatutCopieEnvoyes.Ok, message.CopieEnvoyes);
        Assert.Equal(1, message.CopieTentatives);
        Assert.Null(message.CopieErreur);

        // Page admin « Messages sortants » : état de la copie.
        var liste = await admin.Client.GetAsync("/admin/messages-sortants?taille=100").Attendre<PagedResultDto<MessageSortantDto>>();
        var dto = liste.Donnees.Single(m => m.Destinataire == premier);
        Assert.Equal("Ok", dto.CopieEnvoyes);
        Assert.Equal(1, dto.CopieTentatives);
        Assert.Null(dto.CopieErreur);
    }

    [Theory]
    [InlineData("INBOX.Sent")]
    [InlineData("Sent Messages")]
    [InlineData("Sent")]
    public async Task Sans_SPECIAL_USE_repli_sur_les_noms_usuels(string nom)
    {
        await using var imap = new FauxServeurImap(false, ("INBOX", null), ("Brouillons", null), (nom, null));
        await using var f = Factory(imap);
        var adresse = await AjouterEmailAsync(f, "Repli");
        await f.EnvoyerMessagesAsync();

        Assert.Equal(nom, Assert.Single(imap.Depots).Dossier);
        Assert.Equal(StatutCopieEnvoyes.Ok, (await MessageAsync(f, adresse)).CopieEnvoyes);
    }

    [Fact]
    public async Task Dossier_impose_par_Imap_Dossier()
    {
        await using var imap = new FauxServeurImap(true, ("INBOX", null), ("Sent", @"\Sent"), ("Archives.Site", null));
        await using var f = Factory(imap, new() { ["Imap:Dossier"] = "Archives.Site" });
        var adresse = await AjouterEmailAsync(f, "Dossier imposé");
        await f.EnvoyerMessagesAsync();

        Assert.Equal("Archives.Site", Assert.Single(imap.Depots).Dossier);
        Assert.Equal(StatutCopieEnvoyes.Ok, (await MessageAsync(f, adresse)).CopieEnvoyes);
    }

    [Fact]
    public async Task Aucun_dossier_des_envoyes_email_parti_et_echec_explicite()
    {
        await using var imap = new FauxServeurImap(false, ("INBOX", null), ("Archives", null));
        await using var f = Factory(imap);
        var adresse = await AjouterEmailAsync(f, "Pas de dossier");
        Assert.Single(await f.EmailsPourAsync(adresse));

        var message = await MessageAsync(f, adresse);
        Assert.Equal(StatutMessageSortant.Envoye, message.Statut);
        Assert.Equal(1, message.Tentatives);
        Assert.Equal(StatutCopieEnvoyes.Echec, message.CopieEnvoyes);
        Assert.Contains("Imap:Dossier", message.CopieErreur);
        Assert.Empty(imap.Depots);
    }

    [Fact]
    public async Task Echec_IMAP_n_empeche_pas_l_envoi_est_trace_puis_la_copie_seule_est_reessayee()
    {
        await using var imap = new FauxServeurImap(true);
        await using var f = Factory(imap);
        var admin = await f.AdminAsync();
        await f.EnvoyerMessagesAsync();
        var avant = imap.Depots.Count;

        imap.RefuserProchainsDepots(1);
        var adresse = await AjouterEmailAsync(f, "Clés remises");
        Assert.Single(await f.EmailsPourAsync(adresse));

        var message = await MessageAsync(f, adresse);
        Assert.Equal(StatutMessageSortant.Envoye, message.Statut);
        Assert.Equal(1, message.Tentatives);
        Assert.NotNull(message.DateEnvoi);
        Assert.Equal(StatutCopieEnvoyes.Echec, message.CopieEnvoyes);
        Assert.NotNull(message.CopieErreur);
        Assert.DoesNotContain(MotDePasse, message.CopieErreur);
        Assert.InRange(message.CopieProchaineTentative!.Value, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddMinutes(2));

        var dto = (await admin.Client.GetAsync("/admin/messages-sortants?taille=100").Attendre<PagedResultDto<MessageSortantDto>>())
            .Donnees.Single(m => m.Destinataire == adresse);
        Assert.Equal("Echec", dto.CopieEnvoyes);
        Assert.Equal("Envoye", dto.Statut);
        Assert.NotNull(dto.CopieErreur);

        // Pas encore dû : rien ne se passe. Une fois dû : la copie seule repart (l'email n'est pas renvoyé).
        await f.EnvoyerMessagesAsync();
        Assert.Equal(avant, imap.Depots.Count);
        await ReculerCopieAsync(f, adresse);
        Assert.Equal(0, await f.EnvoyerMessagesAsync());
        Assert.Single(f.Smtp.EmailsPour(adresse));
        var depot = Assert.Single(imap.Depots.Skip(avant));
        message = await MessageAsync(f, adresse);
        Assert.Equal(StatutCopieEnvoyes.Ok, message.CopieEnvoyes);
        Assert.Equal(2, message.CopieTentatives);
        Assert.Null(message.CopieErreur);
        Assert.Null(message.CopieProchaineTentative);
        // La copie réessayée garde le Message-Id et la date de l'email envoyé.
        var envoye = f.Smtp.EmailsPour(adresse).Single();
        Assert.Equal(envoye.MessageId, depot.Message.MessageId);
        Assert.Equal(envoye.Date, depot.Message.Date);
    }

    [Fact]
    public async Task Serveur_IMAP_injoignable_copie_abandonnee_apres_4_essais_sans_toucher_a_l_email()
    {
        await using var imap = new FauxServeurImap(true);
        await using var f = Factory(imap);
        // Port fermé : connexion refusée.
        f.Services.GetRequiredService<IConfiguration>()["Imap:Port"] = "1";
        var adresse = await AjouterEmailAsync(f, "Injoignable");
        Assert.Single(await f.EmailsPourAsync(adresse));

        for (var essai = 2; essai <= EnvoiMessagesServices.MaxTentativesCopie; essai++)
        {
            await ReculerCopieAsync(f, adresse);
            await f.EnvoyerMessagesAsync();
        }
        var message = await MessageAsync(f, adresse);
        Assert.Equal(StatutMessageSortant.Envoye, message.Statut);
        Assert.Equal(1, message.Tentatives);
        Assert.Equal(StatutCopieEnvoyes.Abandonnee, message.CopieEnvoyes);
        Assert.Equal(EnvoiMessagesServices.MaxTentativesCopie, message.CopieTentatives);
        Assert.Null(message.CopieProchaineTentative);
        Assert.Single(f.Smtp.EmailsPour(adresse));

        // Plus aucun essai automatique.
        await f.ModifierBaseAsync(async db =>
            (await db.MessagesSortants.SingleAsync(m => m.Destinataire == adresse)).CopieProchaineTentative = DateTime.UtcNow.AddSeconds(-1));
        f.Services.GetRequiredService<IConfiguration>()["Imap:Port"] = imap.Port.ToString();
        await f.EnvoyerMessagesAsync();
        Assert.DoesNotContain(imap.Depots, d => d.Message.To.Mailboxes.Any(m => m.Address == adresse));
    }

    [Fact]
    public async Task Connexion_coupee_par_le_serveur_reconnexion_transparente()
    {
        await using var imap = new FauxServeurImap(true);
        await using var f = Factory(imap);
        var premier = await AjouterEmailAsync(f, "Avant coupure");
        await f.EnvoyerMessagesAsync();
        Assert.Equal(1, imap.Connexions);

        imap.CouperConnexions();
        var second = await AjouterEmailAsync(f, "Après coupure");
        await f.EnvoyerMessagesAsync();

        Assert.Equal(2, imap.Connexions);
        Assert.Equal(StatutCopieEnvoyes.Ok, (await MessageAsync(f, premier)).CopieEnvoyes);
        var message = await MessageAsync(f, second);
        Assert.Equal(StatutCopieEnvoyes.Ok, message.CopieEnvoyes);
        Assert.Equal(1, message.CopieTentatives);
        Assert.Equal(2, imap.Depots.Count);
    }

    [Fact]
    public async Task Identifiants_IMAP_propres_prioritaires_sur_ceux_du_SMTP()
    {
        await using var imap = new FauxServeurImap(true);
        await using var f = Factory(imap, new() { ["Imap:Utilisateur"] = "archives@immoplus.test", ["Imap:MotDePasse"] = "autre-secret" });
        imap.IdentifiantsExiges = ("archives@immoplus.test", "autre-secret");
        var adresse = await AjouterEmailAsync(f, "Compte IMAP dédié");
        await f.EnvoyerMessagesAsync();
        Assert.Equal(StatutCopieEnvoyes.Ok, (await MessageAsync(f, adresse)).CopieEnvoyes);

        // Mot de passe refusé : échec tracé, sans le mot de passe dans l'erreur.
        imap.IdentifiantsExiges = ("archives@immoplus.test", "change");
        imap.CouperConnexions();
        var refuse = await AjouterEmailAsync(f, "Refusé");
        await f.EnvoyerMessagesAsync();
        var message = await MessageAsync(f, refuse);
        Assert.Equal(StatutMessageSortant.Envoye, message.Statut);
        Assert.Equal(StatutCopieEnvoyes.Echec, message.CopieEnvoyes);
        Assert.DoesNotContain("autre-secret", message.CopieErreur);
    }
}
