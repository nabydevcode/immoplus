namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;

/// <summary>Prix détaillé d'un séjour court : réductions semaine / mois, ménage, caution à part, arrondi au franc, refus motivés.</summary>
public class DevisTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private Task<DevisReservationDto> Devis(int appartementId, int arrivee, int nuits, int voyageurs = 1) =>
        f.CreateClient().GetAsync($"/reservation/devis?appartementId={appartementId}&arrivee={Api.Jour(arrivee)}&depart={Api.Jour(arrivee + nuits)}&voyageurs={voyageurs}")
            .Attendre<DevisReservationDto>();

    [Fact]
    public async Task Sans_reduction_le_total_ajoute_le_menage_et_laisse_la_caution_a_part()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync(prixParNuit: 100_000m, fraisMenage: 50_000m, caution: 200_000m,
            reductionSemainePct: 10, reductionMoisPct: 25);

        var devis = await Devis(logement.Id, 10, 3);

        Assert.True(devis.Disponible, devis.Raison);
        Assert.Equal(3, devis.NombreNuits);
        Assert.Equal(100_000m, devis.PrixParNuit);
        Assert.Equal(300_000m, devis.SousTotal);
        Assert.Equal(0, devis.ReductionPct);
        Assert.Equal(0m, devis.ReductionMontant);
        Assert.Equal(50_000m, devis.FraisMenage);
        Assert.Equal(350_000m, devis.MontantTotal);
        Assert.Equal(200_000m, devis.Caution);
    }

    [Fact]
    public async Task Reduction_semaine_a_partir_de_7_nuits_et_mois_a_partir_de_28_nuits()
    {
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.SejourCourtAsync(prixParNuit: 100_000m, fraisMenage: 50_000m,
            reductionSemainePct: 10, reductionMoisPct: 25, dureeMaxMois: 2);

        var six = await Devis(logement.Id, 10, 6);
        Assert.Equal(0, six.ReductionPct);

        var semaine = await Devis(logement.Id, 10, 7);
        Assert.Equal(10, semaine.ReductionPct);
        Assert.Equal(70_000m, semaine.ReductionMontant);
        Assert.Equal(700_000m - 70_000m + 50_000m, semaine.MontantTotal);

        var vingtSept = await Devis(logement.Id, 10, 27);
        Assert.Equal(10, vingtSept.ReductionPct);

        // La réduction au mois remplace celle à la semaine (pas de cumul).
        var mois = await Devis(logement.Id, 10, 28);
        Assert.Equal(25, mois.ReductionPct);
        Assert.Equal(700_000m, mois.ReductionMontant);
        Assert.Equal(2_800_000m - 700_000m + 50_000m, mois.MontantTotal);
    }

    [Fact]
    public async Task Reduction_arrondie_au_franc_et_meme_prix_a_la_reservation()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(prixParNuit: 33_333m, fraisMenage: 0m, reductionSemainePct: 10);

        var devis = await Devis(logement.Id, 5, 7);
        // 7 × 33 333 = 233 331 ; 10 % = 23 333,1 → 23 333.
        Assert.Equal(233_331m, devis.SousTotal);
        Assert.Equal(23_333m, devis.ReductionMontant);
        Assert.Equal(209_998m, devis.MontantTotal);

        var reservation = await voyageur.ReserverOk(logement.Id, 5, 7);
        Assert.Equal(devis.MontantTotal, reservation.MontantTotal);
        Assert.Equal(devis.ReductionMontant, reservation.ReductionMontant);
        Assert.Equal(7, reservation.NombreNuits);
        Assert.Equal(0m, reservation.MontantTotal % 1);
    }

    [Fact]
    public async Task Refus_motives_nuits_minimum_capacite_dates_passees_et_chevauchement()
    {
        var bailleur = await f.BailleurAsync();
        var voyageur = await f.LocataireAsync();
        var logement = await bailleur.SejourCourtAsync(nuitsMin: 3, capacite: 2);

        var tropCourt = await Devis(logement.Id, 10, 2);
        Assert.False(tropCourt.Disponible);
        Assert.Contains("3 nuits minimum", tropCourt.Raison);
        await voyageur.Reserver(logement.Id, 10, 2).Erreur(HttpStatusCode.BadRequest);

        var tropDeMonde = await Devis(logement.Id, 10, 3, voyageurs: 3);
        Assert.False(tropDeMonde.Disponible);
        await voyageur.Reserver(logement.Id, 10, 3, voyageurs: 3).Erreur(HttpStatusCode.Conflict);

        var passe = await Devis(logement.Id, -2, 3);
        Assert.False(passe.Disponible);

        var tropLong = await Devis(logement.Id, 10, 70);
        Assert.False(tropLong.Disponible);

        await voyageur.ReserverOk(logement.Id, 10, 4);
        var chevauche = await Devis(logement.Id, 12, 3);
        Assert.False(chevauche.Disponible);
        Assert.Contains("déjà réservé", chevauche.Raison);
        await voyageur.Reserver(logement.Id, 12, 3).Erreur(HttpStatusCode.Conflict);

        // Le jour du départ est libre pour une nouvelle arrivée.
        Assert.True((await Devis(logement.Id, 14, 3)).Disponible);
    }

    [Fact]
    public async Task Devis_refuse_pour_une_location_longue_duree_ou_un_logement_inconnu()
    {
        var bailleur = await f.BailleurAsync();
        var longue = await bailleur.LongueDureeAsync();

        await f.CreateClient().GetAsync($"/reservation/devis?appartementId={longue.Id}&arrivee={Api.Jour(5)}&depart={Api.Jour(8)}")
            .Erreur(HttpStatusCode.BadRequest);
        await f.CreateClient().GetAsync($"/reservation/devis?appartementId=999999&arrivee={Api.Jour(5)}&depart={Api.Jour(8)}")
            .Erreur(HttpStatusCode.NotFound);
        await f.CreateClient().GetAsync($"/reservation/devis?appartementId={longue.Id}&arrivee=05-10-2026&depart={Api.Jour(8)}")
            .Erreur(HttpStatusCode.BadRequest);
    }
}
