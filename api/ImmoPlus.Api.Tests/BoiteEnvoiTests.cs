namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Boîte d'envoi : mise en file avec l'action, envoi en arrière-plan, nouvelles tentatives, aucun doublon.</summary>
public class BoiteEnvoiTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private Task<List<MessageSortant>> MessagesPourAsync(string destinataire) =>
        f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking().Where(m => m.Destinataire == destinataire).OrderBy(m => m.Id).ToListAsync());

    [Fact]
    public async Task L_email_est_mis_en_file_avec_l_action_puis_envoye_une_seule_fois()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();

        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Test de la boîte d'envoi" }).Verifier(HttpStatusCode.OK);

        // Rien n'est parti pendant la requête : le message attend dans la boîte d'envoi.
        var enFile = Assert.Single(await MessagesPourAsync(locataire.Email), m => m.Sujet!.Contains("désactivé"));
        Assert.Equal(StatutMessageSortant.EnAttente, enFile.Statut);
        Assert.Equal(TypeMessageSortant.Email, enFile.Type);
        Assert.Equal("CompteDesactive", enFile.Evenement);
        Assert.DoesNotContain(f.Smtp.EmailsPour(locataire.Email), e => e.Sujet.Contains("désactivé"));

        await f.EnvoyerMessagesAsync();
        await f.EnvoyerMessagesAsync(); // un second passage ne renvoie rien

        Assert.Single(f.Smtp.EmailsPour(locataire.Email), e => e.Sujet.Contains("désactivé"));
        var envoye = (await MessagesPourAsync(locataire.Email)).Single(m => m.Id == enFile.Id);
        Assert.Equal(StatutMessageSortant.Envoye, envoye.Statut);
        Assert.Equal(1, envoye.Tentatives);
        Assert.NotNull(envoye.DateEnvoi);

        // Consultation admin, filtrée par statut ; refusée aux autres rôles ; statut inconnu : 400.
        var page = await admin.Client.GetAsync("/admin/messages-sortants?statut=Envoye&taille=100").Attendre<PagedResultDto<MessageSortantDto>>();
        var dto = Assert.Single(page.Donnees, m => m.Id == enFile.Id);
        Assert.Equal("Envoye", dto.Statut);
        Assert.Null(dto.Texte);
        Assert.Null(dto.ProchaineTentative);
        await (await f.BailleurAsync()).Client.GetAsync("/admin/messages-sortants").Verifier(HttpStatusCode.Forbidden);
        await admin.Client.GetAsync("/admin/messages-sortants?statut=Perdu").Erreur(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Serveur_SMTP_en_panne_nouvelles_tentatives_puis_envoi()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        await f.EnvoyerMessagesAsync(); // file vide avant la panne
        await admin.Client.PostJson($"/utilisateur/{locataire.Id}/desactiver", new { motif = "Panne SMTP" }).Verifier(HttpStatusCode.OK);

        f.Smtp.RefuserProchainsEnvois(1);
        await f.EnvoyerMessagesAsync();
        // Le compte a aussi reçu ses emails d'inscription (confirmation, validation), déjà partis : seul compte ici celui de la panne.
        var echec = (await MessagesPourAsync(locataire.Email)).Single(m => m.Sujet!.Contains("désactivé"));
        Assert.Equal(StatutMessageSortant.Echec, echec.Statut);
        Assert.Equal(1, echec.Tentatives);
        Assert.NotNull(echec.DerniereErreur);
        // Nouvel essai dans 1 min : un passage immédiat ne le reprend pas.
        Assert.InRange(echec.ProchaineTentative - echec.DateDerniereTentative!.Value, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(65));
        await f.EnvoyerMessagesAsync();
        Assert.Equal(1, (await MessagesPourAsync(locataire.Email)).Single(m => m.Id == echec.Id).Tentatives);

        // L'heure du nouvel essai arrive : le serveur est revenu, l'email part.
        await f.ModifierBaseAsync(async db =>
            (await db.MessagesSortants.SingleAsync(m => m.Id == echec.Id)).ProchaineTentative = DateTime.UtcNow.AddSeconds(-1));
        await f.EnvoyerMessagesAsync();
        var envoye = (await MessagesPourAsync(locataire.Email)).Single(m => m.Id == echec.Id);
        Assert.Equal(StatutMessageSortant.Envoye, envoye.Statut);
        Assert.Equal(2, envoye.Tentatives);
        Assert.Single(f.Smtp.EmailsPour(locataire.Email), e => e.Sujet.Contains("désactivé"));
    }

    [Fact]
    public async Task Abandon_apres_cinq_echecs_avec_delais_croissants()
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BoiteEnvoiServices>()
                .AjouterEmailAsync("adresse invalide", null, "Test abandon", "<p>x</p>", "Test", null);
            id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessagesSortants
                .Where(m => m.Destinataire == "adresse invalide").Select(m => m.Id).SingleAsync();
        }

        var delais = new List<TimeSpan>();
        for (var tentative = 1; tentative <= EnvoiMessagesServices.MaxTentatives; tentative++)
        {
            await f.ModifierBaseAsync(async db =>
                (await db.MessagesSortants.SingleAsync(m => m.Id == id)).ProchaineTentative = DateTime.UtcNow.AddSeconds(-1));
            await f.EnvoyerMessagesAsync();
            var message = await f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking().SingleAsync(m => m.Id == id));
            Assert.Equal(tentative, message.Tentatives);
            if (tentative < EnvoiMessagesServices.MaxTentatives)
            {
                Assert.Equal(StatutMessageSortant.Echec, message.Statut);
                delais.Add(message.ProchaineTentative - message.DateDerniereTentative!.Value);
            }
            else
            {
                Assert.Equal(StatutMessageSortant.Abandonne, message.Statut);
            }
        }
        // 1 min, 5 min, 30 min, 2 h.
        Assert.Equal(EnvoiMessagesServices.Delais.Select(d => (int)d.TotalMinutes), delais.Select(d => (int)Math.Round(d.TotalMinutes)));

        // Abandonné : plus jamais repris.
        await f.ModifierBaseAsync(async db =>
            (await db.MessagesSortants.SingleAsync(m => m.Id == id)).ProchaineTentative = DateTime.UtcNow.AddDays(-1));
        await f.EnvoyerMessagesAsync();
        Assert.Equal(EnvoiMessagesServices.MaxTentatives, await f.LireBaseAsync(db => db.MessagesSortants.Where(m => m.Id == id).Select(m => m.Tentatives).SingleAsync()));
    }

    [Fact]
    public async Task Quatre_depileurs_simultanes_n_envoient_chaque_message_qu_une_fois()
    {
        const int nombre = 40;
        var prefixe = $"concurrence-{Guid.NewGuid():N}"[..20];
        using (var scope = f.Services.CreateScope())
        {
            var boite = scope.ServiceProvider.GetRequiredService<BoiteEnvoiServices>();
            for (var i = 0; i < nombre; i++)
            {
                await boite.AjouterEmailAsync($"{prefixe}-{i}@tests.immoplus.test", null, $"Message {i}", "<p>x</p>", "Test", null);
            }
        }

        // Comme quatre instances de l'API : chaque dépileur a sa propre connexion (SELECT … FOR UPDATE SKIP LOCKED).
        var traites = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(f.EnvoyerMessagesAsync)));

        Assert.Equal(nombre, traites.Sum());
        var recus = f.Smtp.Emails.Where(e => e.Destinataire.StartsWith(prefixe)).ToList();
        Assert.Equal(nombre, recus.Count);
        Assert.Equal(nombre, recus.Select(e => e.Destinataire).Distinct().Count());
        var messages = await f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking().Where(m => m.Destinataire.StartsWith(prefixe)).ToListAsync());
        Assert.All(messages, m => { Assert.Equal(StatutMessageSortant.Envoye, m.Statut); Assert.Equal(1, m.Tentatives); });
    }

    [Fact]
    public async Task Action_annulee_aucun_message_en_file()
    {
        var destinataire = $"annule-{Guid.NewGuid():N}@tests.immoplus.test";
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<BoiteEnvoiServices>()
                .AjouterEmailAsync(destinataire, null, "Ne doit pas partir", "<p>x</p>", "Test", null);
            await transaction.RollbackAsync();
        }
        Assert.Empty(await MessagesPourAsync(destinataire));
        Assert.Empty(await f.EmailsPourAsync(destinataire));
    }
}
