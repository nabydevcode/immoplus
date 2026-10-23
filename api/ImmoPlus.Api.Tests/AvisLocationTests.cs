namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Models;
using ImmoPlus.Api.Models.Enums;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>Avis longue durée : fenêtre (6 mois de location en cours, ou 30 jours après la fin) et un avis par sens et par location.</summary>
public class AvisLocationTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private static object Avis(int note = 5) => new
    {
        note, proprete = 5, communication = 4, emplacement = 5, rapportQualitePrix = 4, commentaire = "Logement conforme et bailleur réactif."
    };

    /// <summary>Location insérée directement (le parcours complet est couvert par ParcoursLongueDureeTests).</summary>
    private async Task<int> LocationAsync(int locataireId, int appartementId, StatutLocation statut, DateTime? remiseCles, DateTime? fin = null)
    {
        var id = 0;
        await f.ModifierBaseAsync(async db =>
        {
            var location = new Location
            {
                LocataireId = locataireId, AppartementId = appartementId, Statut = statut,
                DateDebut = (remiseCles ?? Api.Aujourdhui).AddDays(-1), DateRemiseCles = remiseCles, DateFin = fin
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            id = location.Id;
        });
        return id;
    }

    [Fact]
    public async Task Location_en_cours_avis_seulement_apres_six_mois_et_un_seul_par_sens()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var intrus = await f.LocataireAsync();
        var logement = await bailleur.LongueDureeAsync();

        var locationId = await LocationAsync(locataire.Id, logement.Id, StatutLocation.EnCours, Api.Aujourdhui.AddMonths(-2));
        Assert.Contains("partir du", await locataire.Client.PostJson($"/location/{locationId}/avis", Avis()).Erreur(HttpStatusCode.Conflict));
        var avant = await locataire.Client.GetAsync($"/location/{locationId}").Attendre<LocationResponseDto>();
        Assert.False(avant.AvisPossible);
        Assert.Equal(Api.Aujourdhui.AddMonths(-2).AddMonths(6), avant.AvisPossibleDepuis!.Value.Date);

        await f.ModifierBaseAsync(async db => (await db.Locations.SingleAsync(l => l.Id == locationId)).DateRemiseCles = Api.Aujourdhui.AddMonths(-7));
        var avis = await locataire.Client.PostJson($"/location/{locationId}/avis", Avis(4)).Attendre<AvisDto>(HttpStatusCode.Created);
        Assert.Equal("LocataireVersLogement", avis.Sens);
        Assert.Equal(locationId, avis.LocationId);
        Assert.Null(avis.ReservationId);
        await locataire.Client.PostJson($"/location/{locationId}/avis", Avis()).Erreur(HttpStatusCode.Conflict);
        await intrus.Client.PostJson($"/location/{locationId}/avis", Avis()).Erreur(HttpStatusCode.Conflict);
        await admin.Client.PostJson($"/location/{locationId}/avis", Avis()).Erreur(HttpStatusCode.Conflict);
        var avisBailleur = await bailleur.Client.PostJson($"/location/{locationId}/avis", Avis(5)).Attendre<AvisDto>(HttpStatusCode.Created);
        Assert.Equal("BailleurVersLocataire", avisBailleur.Sens);
        Assert.Null(avisBailleur.Proprete);
        Assert.Contains(await f.EmailsPourAsync(bailleur.Email), e => e.Sujet.Contains($"[Location n° {locationId:D6}]"));

        var apres = await bailleur.Client.GetAsync($"/location/{locationId}").Attendre<LocationResponseDto>();
        Assert.True(apres.AvisLocataireDonne);
        Assert.True(apres.AvisBailleurDonne);
        Assert.True(apres.AvisPossible);

        // Fiche publique : l'avis du locataire compte dans la note du logement ; le bailleur peut y répondre.
        var publics = await f.CreateClient().GetAsync($"/appartement/{logement.Id}/avis").Attendre<ListeAvisDto>();
        Assert.Equal(4m, publics.Moyenne);
        Assert.Single(publics.Avis, a => a.Id == avis.Id);
        await bailleur.Client.PostJson($"/avis/{avis.Id}/reponse", new { reponse = "Merci !" }).Verifier(HttpStatusCode.OK);
        // L'avis du bailleur rejoint la réputation du locataire.
        Assert.Equal(1, (await f.CreateClient().GetAsync($"/utilisateur/{locataire.Id}/profil-public").Attendre<ProfilPublicDto>()).NombreAvis);

        // La location se termine ensuite : pas de second avis.
        await f.ModifierBaseAsync(async db =>
        {
            var l = await db.Locations.SingleAsync(x => x.Id == locationId);
            l.Statut = StatutLocation.Terminee;
            l.DateFin = Api.Aujourdhui;
        });
        await locataire.Client.PostJson($"/location/{locationId}/avis", Avis()).Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Location_terminee_avis_pendant_trente_jours()
    {
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var logement = await bailleur.LongueDureeAsync();

        var expiree = await LocationAsync(locataire.Id, logement.Id, StatutLocation.Terminee, Api.Aujourdhui.AddMonths(-12), Api.Aujourdhui.AddDays(-31));
        Assert.Contains("expiré", await locataire.Client.PostJson($"/location/{expiree}/avis", Avis()).Erreur(HttpStatusCode.Conflict));
        Assert.False((await locataire.Client.GetAsync($"/location/{expiree}").Attendre<LocationResponseDto>()).AvisPossible);

        // Terminée il y a 29 jours, même après seulement 3 mois de location : avis possible.
        var recente = await LocationAsync(locataire.Id, logement.Id, StatutLocation.Terminee, Api.Aujourdhui.AddMonths(-3), Api.Aujourdhui.AddDays(-29));
        var dto = (await locataire.Client.GetAsync("/location/lister").AttendreDonnees<LocationResponseDto>()).Single(l => l.Id == recente);
        Assert.True(dto.AvisPossible);
        Assert.Equal(Api.Aujourdhui.AddDays(1), dto.AvisPossibleJusquau!.Value.Date);
        await locataire.Client.PostJson($"/location/{recente}/avis", Avis()).Verifier(HttpStatusCode.Created);
        await bailleur.Client.PostJson($"/location/{recente}/avis", new { note = 6, commentaire = "Note hors bornes pour tester." })
            .Erreur(HttpStatusCode.BadRequest);

        // Location annulée (résiliée) : jamais d'avis.
        var resiliee = await LocationAsync(locataire.Id, logement.Id, StatutLocation.Resiliee, null);
        await locataire.Client.PostJson($"/location/{resiliee}/avis", Avis()).Erreur(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Recherche_longue_duree_par_loyer_chambres_et_avance()
    {
        var bailleur = await f.BailleurAsync();
        var ville = $"Ville{Guid.NewGuid():N}"[..14];
        async Task<int> Publier(decimal loyer, int avance, int? chambres) => (await bailleur.Client.PostJson("/appartement/ajouter", new
        {
            adresse = "1 rue de la Recherche", typeOffre = "LongueDuree", ville, quartier = "Centre", chambres, loyerMensuel = loyer, dureeAvanceExigee = avance
        }).Attendre<AppartementResponseDto>(HttpStatusCode.Created)).Id;
        var economique = await Publier(800_000m, 3, 2);
        var grand = await Publier(2_000_000m, 6, 4);
        var sansChambres = await Publier(900_000m, 3, null);

        async Task<List<int>> Chercher(string criteres) =>
            (await f.CreateClient().GetAsync($"/appartement/lister?typeOffre=LongueDuree&ville={ville}&{criteres}")
                .AttendreDonnees<AppartementResponseDto>()).Select(a => a.Id).Order().ToList();
        Assert.Equal(new[] { economique, sansChambres }, await Chercher("loyerMax=1000000"));
        Assert.Equal(new[] { grand }, await Chercher("chambresMin=3"));
        Assert.Equal(new[] { economique, sansChambres }, await Chercher("avanceMax=4"));
        // Filtres du séjour court ignorés pour la longue durée.
        Assert.Equal(3, (await Chercher($"voyageurs=10&prixMax=1&arrivee={Api.Jour(5)}&depart={Api.Jour(7)}")).Count);
        await f.CreateClient().GetAsync("/appartement/lister?typeOffre=LongueDuree&loyerMax=-5").Verifier(HttpStatusCode.BadRequest);

        var villesLd = await f.CreateClient().GetAsync("/appartement/villes?typeOffre=LongueDuree").Attendre<List<VilleQuartiersDto>>();
        var villesSc = await f.CreateClient().GetAsync("/appartement/villes?typeOffre=SejourCourt").Attendre<List<VilleQuartiersDto>>();
        Assert.Contains(villesLd, v => v.Ville == ville);
        Assert.DoesNotContain(villesSc, v => v.Ville == ville);
        await f.CreateClient().GetAsync("/appartement/villes?typeOffre=Autre").Verifier(HttpStatusCode.BadRequest);
    }
}
