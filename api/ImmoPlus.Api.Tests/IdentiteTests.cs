namespace ImmoPlus.Api.Tests;

using System.Net;
using System.Net.Http.Headers;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Vérification d'identité : une seule demande en attente, fichiers réservés aux admins, badge seulement après validation.</summary>
public class IdentiteTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private static MultipartFormDataContent Demande(string type, (string Nom, byte[] Contenu)? recto, (string Nom, byte[] Contenu)? verso = null)
    {
        var formulaire = new MultipartFormDataContent { { new StringContent(type), "type" } };
        foreach (var (champ, fichier) in new[] { ("recto", recto), ("verso", verso) })
        {
            if (fichier is { } fi)
            {
                var contenu = new ByteArrayContent(fi.Contenu);
                contenu.Headers.ContentType = new MediaTypeHeaderValue(fi.Nom.EndsWith(".pdf") ? "application/pdf" : "image/png");
                formulaire.Add(contenu, champ, fi.Nom);
            }
        }
        return formulaire;
    }

    [Fact]
    public async Task Une_seule_demande_en_attente_fichiers_admin_uniquement_puis_badge_apres_validation()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var autre = await f.LocataireAsync();

        // Un compte validé n'a pas pour autant le badge.
        Assert.False((await f.CreateClient().GetAsync($"/utilisateur/{bailleur.Id}/profil-public").Attendre<ProfilPublicDto>()).IdentiteVerifiee);
        Assert.Null((await bailleur.Client.GetAsync("/utilisateur/moi/identite").Attendre<StatutIdentiteDto>()).Demande);

        await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", null)).Erreur(HttpStatusCode.BadRequest);
        await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("Inconnu", ("recto.png", Api.Png))).Erreur(HttpStatusCode.BadRequest);
        await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", ("recto.png", "pas une image"u8.ToArray())))
            .Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", ("recto.png", Api.Png))).Verifier(HttpStatusCode.Forbidden);

        var demande = await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", ("recto.png", Api.Png), ("verso.pdf", Api.Pdf)))
            .Attendre<PieceIdentiteDto>(HttpStatusCode.Created);
        Assert.Equal("EnAttente", demande.Statut);
        Assert.True(demande.AVerso);
        Assert.Equal("application/pdf", demande.TypeVerso);
        await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("Passeport", ("recto.png", Api.Png))).Erreur(HttpStatusCode.Conflict);
        Assert.Equal("EnAttente", (await bailleur.Client.GetAsync("/utilisateur/moi/identite").Attendre<StatutIdentiteDto>()).Demande!.Statut);

        // Fichiers privés : ni le demandeur, ni un autre utilisateur, ni un visiteur ; l'admin oui.
        foreach (var client in new[] { bailleur.Client, autre.Client })
        {
            await client.GetAsync($"/admin/identites/{demande.Id}/fichier/recto").Verifier(HttpStatusCode.Forbidden);
        }
        await f.CreateClient().GetAsync($"/admin/identites/{demande.Id}/fichier/recto").Verifier(HttpStatusCode.Unauthorized);
        var recto = await admin.Client.GetAsync($"/admin/identites/{demande.Id}/fichier/recto").Verifier(HttpStatusCode.OK);
        Assert.Equal("image/jpeg", recto.Content.Headers.ContentType!.MediaType);
        var octets = await recto.Content.ReadAsByteArrayAsync();
        Assert.True(octets[0] == 0xFF && octets[1] == 0xD8, "Le recto image est réencodé en JPEG (sans EXIF)");
        var verso = await admin.Client.GetAsync($"/admin/identites/{demande.Id}/fichier/verso").Verifier(HttpStatusCode.OK);
        Assert.Equal("application/pdf", verso.Content.Headers.ContentType!.MediaType);
        await admin.Client.GetAsync($"/admin/identites/{demande.Id}/fichier/autre").Verifier(HttpStatusCode.BadRequest);

        var enAttente = await admin.Client.GetAsync("/admin/identites?statut=EnAttente").AttendreDonnees<PieceIdentiteDto>();
        Assert.Contains(enAttente, d => d.Id == demande.Id && d.UtilisateurEmail == bailleur.Email);
        await bailleur.Client.PostJson($"/admin/identites/{demande.Id}/valider").Verifier(HttpStatusCode.Forbidden);

        var validee = await admin.Client.PostJson($"/admin/identites/{demande.Id}/valider").Attendre<PieceIdentiteDto>();
        Assert.Equal("Validee", validee.Statut);
        await admin.Client.PostJson($"/admin/identites/{demande.Id}/refuser", new { motif = "Trop tard" }).Erreur(HttpStatusCode.Conflict);
        Assert.True((await f.CreateClient().GetAsync($"/utilisateur/{bailleur.Id}/profil-public").Attendre<ProfilPublicDto>()).IdentiteVerifiee);
        Assert.True((await bailleur.Client.GetAsync("/utilisateur/moi").Attendre<UtilisateurResponseDto>()).IdentiteVerifiee);
        Assert.Contains(await f.EmailsPourAsync(bailleur.Email), e => e.Sujet.Contains("identité est vérifiée"));
        // Identité déjà vérifiée : plus de nouvelle demande.
        await bailleur.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", ("recto.png", Api.Png))).Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Refus_avec_motif_obligatoire_puis_nouvelle_demande_possible()
    {
        var admin = await f.AdminAsync();
        var locataire = await f.LocataireAsync();
        var demande = await locataire.Client.PostAsync("/utilisateur/moi/identite", Demande("Passeport", ("passeport.pdf", Api.Pdf)))
            .Attendre<PieceIdentiteDto>(HttpStatusCode.Created);
        Assert.Equal("application/pdf", demande.TypeRecto);

        await admin.Client.PostJson($"/admin/identites/{demande.Id}/refuser", new { motif = " " }).Erreur(HttpStatusCode.BadRequest);
        var refusee = await admin.Client.PostJson($"/admin/identites/{demande.Id}/refuser", new { motif = "Document illisible" })
            .Attendre<PieceIdentiteDto>();
        Assert.Equal("Refusee", refusee.Statut);
        var statut = await locataire.Client.GetAsync("/utilisateur/moi/identite").Attendre<StatutIdentiteDto>();
        Assert.False(statut.IdentiteVerifiee);
        Assert.Equal("Document illisible", statut.Demande!.MotifRefus);

        await locataire.Client.PostAsync("/utilisateur/moi/identite", Demande("CNI", ("recto.png", Api.Png))).Verifier(HttpStatusCode.Created);
        var journal = await admin.Client.GetAsync($"/admin/journal?action=identite&cibleId={locataire.Id}").Attendre<PagedResultDto<JournalActionDto>>();
        Assert.Contains(journal.Donnees, j => j.Action == "identite.refuser" && j.UtilisateurId == admin.Id);
    }
}
