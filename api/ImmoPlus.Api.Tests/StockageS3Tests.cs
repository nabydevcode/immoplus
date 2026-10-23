namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;

/// <summary>
/// Fichiers dans un stockage S3 (Garage jetable, bucket de test dédié supprimé à la fin) : deux instances de l'API sur la
/// même base et le même bucket ; un fichier envoyé par l'une est servi par l'autre, rien n'est écrit sur le disque local.
/// </summary>
public class StockageS3Tests : IAsyncLifetime
{
    private readonly string _bucket = $"immoplus-test-{Guid.NewGuid():N}"[..30];
    private ImmoPlusFactory _a = null!;
    private ImmoPlusFactory _b = null!;

    public async Task InitializeAsync()
    {
        var reglages = await ServeursAnnexesTests.CreerBucketAsync(_bucket);
        _a = ImmoPlusFactory.Creer(reglages);
        _b = ImmoPlusFactory.Creer(reglages, memeBaseQue: _a);
    }

    public async Task DisposeAsync()
    {
        await _b.DisposeAsync();
        await _a.DisposeAsync();
        await ServeursAnnexesTests.SupprimerBucketAsync(_bucket);
    }

    private async Task<List<string>> ObjetsAsync()
    {
        using var s3 = await ServeursAnnexesTests.ClientS3Async();
        return await ServeursAnnexesTests.ListerAsync(s3, _bucket);
    }

    [Fact]
    public async Task Stockage_S3_enregistrer_lire_existe_supprimer()
    {
        using var s3 = await ServeursAnnexesTests.ClientS3Async();
        var stockage = new StockageS3(s3, _bucket);
        var contenu = System.Text.Encoding.UTF8.GetBytes("contenu de test é");

        Assert.False(await stockage.ExisteAsync("documents/1/absent.pdf"));
        Assert.Null(await stockage.OuvrirAsync("documents/1/absent.pdf"));

        await stockage.EnregistrerAsync("documents/1/essai.pdf", new MemoryStream(contenu), "application/pdf");
        Assert.True(await stockage.ExisteAsync("documents/1/essai.pdf"));
        await using (var flux = await stockage.OuvrirAsync("documents/1/essai.pdf"))
        {
            using var copie = new MemoryStream();
            await flux!.CopyToAsync(copie);
            Assert.Equal(contenu, copie.ToArray());
        }
        // Ancien chemin Windows enregistré en base : même clé.
        Assert.True(await stockage.ExisteAsync(ClesStockage.Cle(ClesStockage.Documents, "1\\essai.pdf")));

        await stockage.SupprimerAsync("documents/1/essai.pdf");
        await stockage.SupprimerAsync("documents/1/essai.pdf"); // déjà supprimé : pas d'erreur
        Assert.False(await stockage.ExisteAsync("documents/1/essai.pdf"));

        // Aucune sortie du préfixe possible.
        await Assert.ThrowsAsync<InvalidOperationException>(() => stockage.OuvrirAsync("photos/../identites/1/x.jpg"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stockage.OuvrirAsync("/etc/passwd"));
    }

    [Fact]
    public async Task Photo_et_document_envoyes_par_une_instance_servis_par_l_autre()
    {
        var bailleur = await _a.BailleurAsync();
        var locataire = await _a.LocataireAsync();
        var bailleurB = _b.CreateClient().AvecJeton(bailleur.Jeton);
        var locataireB = _b.CreateClient().AvecJeton(locataire.Jeton);

        // Photo envoyée à l'instance A, lue (photo et miniature) par l'instance B, sans connexion.
        var logement = await bailleur.LongueDureeAsync();
        var photo = await bailleur.Client.PostAsync($"/appartement/{logement.Id}/photos", Api.Formulaire("salon.png", Api.Png))
            .Attendre<PhotoAppartementDto>(HttpStatusCode.Created);
        var anonymeB = _b.CreateClient();
        var image = await (await anonymeB.GetAsync($"/{photo.Url}").Verifier(HttpStatusCode.OK)).Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, image[..3]); // réencodée en JPEG
        var miniature = await anonymeB.GetAsync($"/{photo.UrlMiniature}").Verifier(HttpStatusCode.OK);
        Assert.Equal("image/jpeg", miniature.Content.Headers.ContentType?.MediaType);

        // Document privé : envoyé à B, téléchargé depuis A, mêmes octets.
        var candidature = await locataireB.PostJson("/candidature/creer", new
        {
            appartementId = logement.Id, apportInitial = 6_000_000m, situationProfessionnelle = "CDI"
        }).Attendre<CandidatureResponseDto>(HttpStatusCode.Created);
        var document = await locataireB.PostAsync($"/candidature/{candidature.Id}/documents", Api.Formulaire("piece.pdf", Api.Pdf,
            ("proprietaire", "Locataire"), ("typeDocument", "PieceIdentite"))).Attendre<DocumentResponseDto>(HttpStatusCode.Created);
        var telecharge = await bailleur.Client.GetAsync($"/document/{document.Id}/telecharger").Verifier(HttpStatusCode.OK);
        Assert.Equal(Api.Pdf, await telecharge.Content.ReadAsByteArrayAsync());

        // Mêmes clés qu'avant (chemins relatifs de Storage/) ; rien sur le disque des instances.
        var objets = await ObjetsAsync();
        Assert.Contains(objets, o => o.StartsWith($"photos/{logement.Id}/") && o.EndsWith(".jpg"));
        Assert.Contains(objets, o => o.StartsWith($"miniatures/{logement.Id}/"));
        Assert.Contains(objets, o => o.StartsWith($"documents/{candidature.Id}/") && o.EndsWith(".pdf"));
        Assert.False(Directory.Exists(Path.Combine(_a.Dossier, "Storage")) && Directory.EnumerateFiles(Path.Combine(_a.Dossier, "Storage"), "*", SearchOption.AllDirectories).Any());
        Assert.False(Directory.Exists(Path.Combine(_b.Dossier, "Storage")) && Directory.EnumerateFiles(Path.Combine(_b.Dossier, "Storage"), "*", SearchOption.AllDirectories).Any());

        // Suppression par B : photo et miniature retirées du bucket, plus servies par A.
        await bailleurB.DeleteAsync($"/appartement/photos/{photo.Id}").Verifier(HttpStatusCode.NoContent);
        Assert.DoesNotContain(await ObjetsAsync(), o => o.StartsWith($"photos/{logement.Id}/") || o.StartsWith($"miniatures/{logement.Id}/"));
        await _a.CreateClient().GetAsync($"/{photo.Url}").Verifier(HttpStatusCode.NotFound);
    }
}
