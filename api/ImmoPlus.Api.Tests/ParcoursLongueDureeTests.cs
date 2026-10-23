namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Location longue durée de bout en bout : candidature → visite → acceptation → apport → remise des clés → échéancier
/// (arrondi au franc, dernière mensualité ajustée) → loyers après la période couverte par l'avance → préavis → fin bloquée tant qu'il reste des impayés.
/// </summary>
public class ParcoursLongueDureeTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private const decimal Loyer = 1_000_000m;
    private const int DureeAvance = 6;

    private static Task<HttpResponseMessage> Candidater(Compte locataire, int appartementId, decimal apport) =>
        locataire.Client.PostJson("/candidature/creer", new
        {
            appartementId,
            apportInitial = apport,
            situationProfessionnelle = "CDI",
            garant = new { nom = "Garant Test", telephone = "625000000", situationProfessionnelle = "Independant" }
        });

    /// <summary>Visite programmée par le bailleur, puis ramenée dans le passé (on ne peut pas attendre l'heure de la visite).</summary>
    private async Task<int> VisiteEffectueeAsync(Compte bailleur, Compte locataire, int candidatureId)
    {
        var dossier = await bailleur.Client.PostJson($"/candidature/{candidatureId}/visite",
            new { dateHeure = DateTime.UtcNow.AddHours(2), message = "Rendez-vous devant l'immeuble" }).Attendre<CandidatureResponseDto>();
        Assert.Equal("Preselectionnee", dossier.Statut);
        var visiteId = dossier.Visites.Single().Id;
        Assert.Equal("Confirmee", (await locataire.Client.PostJson($"/visite/{visiteId}/repondre", new { disponible = true })
            .Attendre<CandidatureResponseDto>()).Visites.Single().Statut);

        await f.ModifierBaseAsync(async db => (await db.Visites.SingleAsync(v => v.Id == visiteId)).DateHeure = DateTime.UtcNow.AddHours(-1));
        var cloture = await bailleur.Client.PostJson($"/visite/{visiteId}/cloturer", new { effectuee = true, notes = "Candidat sérieux" })
            .Attendre<CandidatureResponseDto>();
        Assert.Equal("Effectuee", cloture.Visites.Single().Statut);
        Assert.Equal("Candidat sérieux", cloture.Visites.Single().NotesBailleur);
        return visiteId;
    }

    [Fact]
    public async Task Parcours_complet_de_la_candidature_a_la_fin_de_location()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var locataire = await f.LocataireAsync();
        var autreCandidat = await f.LocataireAsync();
        // Loyer choisi pour que l'échéancier tombe sur des décimales (vérifie l'arrondi au franc).
        const decimal loyer = 1_000_010m;
        var logement = await bailleur.LongueDureeAsync(loyer, DureeAvance);

        // --- Candidatures : apport fixé par la plateforme (2 mois de loyer), le montant envoyé est ignoré ;
        // une seule candidature ouverte par logement.
        var dossier = await Candidater(locataire, logement.Id, 1m).Attendre<CandidatureResponseDto>(HttpStatusCode.Created);
        Assert.Equal(2 * loyer, dossier.ApportInitial);
        Assert.Equal("EnAttente", dossier.Statut);
        Assert.Contains(await f.EmailsPourAsync(locataire.Email), e => e.Sujet.Contains($"Dossier n° {dossier.Id:D6}"));
        await Candidater(locataire, logement.Id, 3_000_000m).Erreur(HttpStatusCode.Conflict);
        var autreDossier = await Candidater(autreCandidat, logement.Id, 3_000_000m).Attendre<CandidatureResponseDto>(HttpStatusCode.Created);

        // --- Visite : obligatoire avant l'acceptation.
        await bailleur.Client.PostJson($"/candidature/{dossier.Id}/accepter", new { }).Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostJson($"/candidature/{dossier.Id}/visite", new { dateHeure = DateTime.UtcNow.AddHours(-2) })
            .Erreur(HttpStatusCode.BadRequest);
        var visiteId = await VisiteEffectueeAsync(bailleur, locataire, dossier.Id);
        await autreCandidat.Client.PostJson($"/visite/{visiteId}/repondre", new { disponible = true }).Erreur(HttpStatusCode.Conflict);
        // Le candidat ne voit pas les notes du bailleur.
        Assert.Null((await locataire.Client.GetAsync($"/candidature/{dossier.Id}").Attendre<CandidatureResponseDto>()).Visites.Single().NotesBailleur);

        // --- Acceptation par le bailleur (taux de frais par défaut 3 %) : les autres dossiers passent en liste d'attente.
        var accepte = await bailleur.Client.PostJson($"/candidature/{dossier.Id}/accepter", new { tauxFrais = 0.05m })
            .Attendre<CandidatureResponseDto>();
        Assert.Equal("Acceptee", accepte.Statut);
        var locationId = accepte.LocationId!.Value;
        var financementId = accepte.FinancementLocationId!.Value;
        Assert.Equal("ListeAttente", (await autreCandidat.Client.GetAsync($"/candidature/{autreDossier.Id}").Attendre<CandidatureResponseDto>()).Statut);

        var financement = (await locataire.Client.GetAsync("/financement/lister").AttendreDonnees<FinancementLocationResponseDto>()).Single();
        Assert.Equal(financementId, financement.Id);
        Assert.Equal(6 * loyer, financement.MontantTotal);
        Assert.Equal(4 * loyer, financement.MontantARembourser);
        Assert.Equal(0.03m, financement.TauxFrais);
        Assert.Equal(4, financement.NombreMensualites);
        Assert.Equal(1_030_010m, financement.MontantMensualite);
        Assert.Equal("EnAttenteValidation", financement.Statut);
        await bailleur.Client.GetAsync("/financement/lister").Verifier(HttpStatusCode.Forbidden);
        Assert.Empty(await autreCandidat.Client.GetAsync("/financement/lister").AttendreDonnees<FinancementLocationResponseDto>());

        // --- Apport puis remise des clés (admin uniquement, dans cet ordre).
        var dateApport = Api.Aujourdhui.AddMonths(-8);
        await admin.Client.PostJson($"/financement/{financementId}/generer-echeancier").Erreur(HttpStatusCode.Conflict);
        await admin.Client.PostJson($"/candidature/{dossier.Id}/remise-cles", new { dateRemise = Api.Jour(0) }).Erreur(HttpStatusCode.Conflict);
        await bailleur.Client.PostJson($"/candidature/{dossier.Id}/apport", new { modePaiement = "Espèces", datePaiement = dateApport })
            .Verifier(HttpStatusCode.Forbidden);
        await admin.Client.PostJson($"/candidature/{dossier.Id}/apport", new { modePaiement = "Espèces", datePaiement = Api.Jour(1) })
            .Erreur(HttpStatusCode.BadRequest);
        var apport = await admin.Client.PostJson($"/candidature/{dossier.Id}/apport",
            new { modePaiement = "Orange Money", reference = "OM-APPORT", datePaiement = dateApport }).Attendre<CandidatureResponseDto>();
        Assert.Equal(dateApport, apport.DateApportPaye!.Value.Date);
        await admin.Client.PostJson($"/candidature/{dossier.Id}/apport", new { modePaiement = "Espèces", datePaiement = dateApport })
            .Erreur(HttpStatusCode.Conflict);
        // L'attribution ne peut plus être annulée une fois l'apport payé.
        await locataire.Client.PostJson($"/candidature/{dossier.Id}/desister").Erreur(HttpStatusCode.Conflict);

        await admin.Client.PostJson($"/candidature/{dossier.Id}/remise-cles", new { dateRemise = dateApport.AddDays(-1) }).Erreur(HttpStatusCode.BadRequest);
        await admin.Client.PostJson($"/candidature/{dossier.Id}/remise-cles", new { dateRemise = Api.Jour(1) }).Erreur(HttpStatusCode.BadRequest);
        var remise = await admin.Client.PostJson($"/candidature/{dossier.Id}/remise-cles", new { dateRemise = dateApport })
            .Attendre<CandidatureResponseDto>();
        Assert.Equal(dateApport, remise.DateRemiseCles!.Value.Date);

        // --- Échéancier : arrondi au franc, la dernière mensualité absorbe l'écart.
        // 4 000 040 × 1,03 = 4 120 041,2 → 4 120 041 ; / 4 = 1 030 010,25 → 1 030 010 ; dernière = 1 030 011.
        await admin.Client.PostJson($"/financement/{financementId}/generer-echeancier").Verifier(HttpStatusCode.NoContent);
        await admin.Client.PostJson($"/financement/{financementId}/generer-echeancier").Erreur(HttpStatusCode.Conflict);
        var mensualites = await locataire.Client.GetAsync($"/financement/{financementId}/mensualites").Attendre<List<MensualiteResponseDto>>();
        Assert.Equal(new[] { 1_030_010m, 1_030_010m, 1_030_010m, 1_030_011m }, mensualites.Select(m => m.MontantDu));
        Assert.Equal(4_120_041m, mensualites.Sum(m => m.MontantDu));
        Assert.All(mensualites, m => Assert.Equal(0m, m.MontantDu % 1));
        Assert.Equal(Enumerable.Range(0, 4).Select(i => Api.Aujourdhui.AddMonths(i)), mensualites.Select(m => m.DateEcheance.Date));
        await autreCandidat.Client.GetAsync($"/financement/{financementId}/mensualites").Erreur(HttpStatusCode.Conflict);

        // Paiement d'une mensualité : pas au-delà du reste dû, et seulement par l'admin.
        var premiere = mensualites[0];
        object Mensualite(decimal montant) => new { mensualiteId = premiere.Id, montant, datePaiement = Api.Jour(0), modePaiement = "Espèces" };
        await admin.Client.PostJson("/paiement/enregistrer", Mensualite(1_030_011m)).Erreur(HttpStatusCode.BadRequest);
        await bailleur.Client.PostJson("/paiement/enregistrer", Mensualite(1_030_010m)).Erreur(HttpStatusCode.Conflict);
        await admin.Client.PostJson("/paiement/enregistrer", Mensualite(1_030_010m)).Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson("/paiement/enregistrer", Mensualite(1m)).Erreur(HttpStatusCode.Conflict);

        // --- Loyers : sans frais, à partir de la fin de la période couverte par l'avance (remise des clés + 6 mois).
        var debutLoyers = dateApport.AddMonths(DureeAvance);
        var loyers = await locataire.Client.GetAsync($"/location/{locationId}/loyers").Attendre<LoyersLocationDto>();
        Assert.Equal(debutLoyers, loyers.DebutLoyers!.Value.Date);
        Assert.Equal(loyer, loyers.LoyerMensuel);
        Assert.NotEmpty(loyers.Loyers);
        Assert.Equal(debutLoyers, loyers.Loyers[0].DateEcheance.Date);
        Assert.Equal(Enumerable.Range(1, loyers.Loyers.Count), loyers.Loyers.Select(l => l.Numero));
        Assert.All(loyers.Loyers, l =>
        {
            Assert.Equal(loyer, l.MontantDu);
            Assert.InRange(l.DateEcheance.Date, debutLoyers, Api.Aujourdhui.AddMonths(1));
        });
        Assert.Equal(debutLoyers.AddMonths(loyers.Loyers.Count - 1), loyers.Loyers[^1].DateEcheance.Date);
        Assert.True(loyers.Loyers[^1].DateEcheance.Date > Api.Aujourdhui, "Le loyer du mois prochain doit déjà être créé");
        await bailleur.Client.GetAsync($"/location/{locationId}/loyers").Verifier(HttpStatusCode.Forbidden);
        await autreCandidat.Client.GetAsync($"/location/{locationId}/loyers").Erreur(HttpStatusCode.Conflict);

        // --- Préavis de 3 mois, puis fin de location.
        await autreCandidat.Client.PostJson($"/location/{locationId}/preavis", new { motif = "Intrus" }).Erreur(HttpStatusCode.Conflict);
        var preavis = await locataire.Client.PostJson($"/location/{locationId}/preavis", new { motif = "Mutation professionnelle" })
            .Attendre<LocationResponseDto>();
        Assert.Equal(Api.Aujourdhui, preavis.DatePreavis!.Value.Date);
        Assert.Equal(Api.Aujourdhui.AddMonths(3), preavis.DateFinPrevue!.Value.Date);
        Assert.Equal("Locataire", preavis.PreavisDonnePar);
        await locataire.Client.PostJson($"/location/{locationId}/preavis", new { }).Erreur(HttpStatusCode.Conflict);
        Assert.Contains("préavis court", await admin.Client.PostJson($"/location/{locationId}/terminer").Erreur(HttpStatusCode.Conflict));
        await bailleur.Client.PostJson($"/location/{locationId}/terminer").Verifier(HttpStatusCode.Forbidden);

        // Les 3 mois sont écoulés.
        await f.ModifierBaseAsync(async db => (await db.Locations.SingleAsync(l => l.Id == locationId)).DateFinPrevue = Api.Aujourdhui);

        Assert.Contains("financement", await admin.Client.PostJson($"/location/{locationId}/terminer").Erreur(HttpStatusCode.Conflict));
        var solde = await admin.Client.PostJson($"/financement/{financementId}/solder", new { modePaiement = "Virement", datePaiement = Api.Jour(0) })
            .Attendre<Dictionary<string, int>>();
        Assert.Equal(3, solde["mensualitesSoldees"]);
        Assert.Equal("Solde", (await locataire.Client.GetAsync("/financement/lister").AttendreDonnees<FinancementLocationResponseDto>()).Single().Statut);

        var impayes = (await admin.Client.GetAsync($"/location/{locationId}/loyers").Attendre<LoyersLocationDto>()).Loyers
            .Where(l => l.DateEcheance.Date < Api.Aujourdhui && l.MontantPaye < l.MontantDu).ToList();
        Assert.NotEmpty(impayes);
        var refus = await admin.Client.PostJson($"/location/{locationId}/terminer").Erreur(HttpStatusCode.Conflict);
        Assert.Contains("impayé", refus);

        await admin.Client.PostJson("/paiement/enregistrer", new { loyerId = impayes[0].Id, montant = loyer + 1, datePaiement = Api.Jour(0), modePaiement = "Espèces" })
            .Erreur(HttpStatusCode.BadRequest);
        foreach (var impaye in impayes)
        {
            await admin.Client.PostJson("/paiement/enregistrer", new { loyerId = impaye.Id, montant = loyer, datePaiement = Api.Jour(0), modePaiement = "Espèces" })
                .Verifier(HttpStatusCode.OK);
        }

        var terminee = await admin.Client.PostJson($"/location/{locationId}/terminer").Attendre<LocationResponseDto>();
        Assert.Equal("Terminee", terminee.Statut);
        Assert.Equal(Api.Aujourdhui, terminee.DateFin!.Value.Date);
        Assert.True((await f.CreateClient().GetAsync($"/appartement/{logement.Id}").Attendre<AppartementResponseDto>()).Disponible);
    }

    [Fact]
    public async Task Trois_candidats_au_plus_retenus_pour_une_visite_et_absence_ne_permet_pas_d_accepter()
    {
        var admin = await f.AdminAsync();
        var bailleur = await f.BailleurAsync();
        var logement = await bailleur.LongueDureeAsync(Loyer, DureeAvance);
        var candidats = new List<Compte>();
        for (var i = 0; i < 4; i++)
        {
            candidats.Add(await f.LocataireAsync());
        }
        var dossiers = new List<CandidatureResponseDto>();
        foreach (var candidat in candidats)
        {
            dossiers.Add(await Candidater(candidat, logement.Id, 3_000_000m).Attendre<CandidatureResponseDto>(HttpStatusCode.Created));
        }

        for (var i = 0; i < 3; i++)
        {
            await bailleur.Client.PostJson($"/candidature/{dossiers[i].Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(1) })
                .Verifier(HttpStatusCode.OK);
        }
        Assert.Contains("3 candidats", await bailleur.Client.PostJson($"/candidature/{dossiers[3].Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(1) })
            .Erreur(HttpStatusCode.Conflict));

        // Candidat absent : la visite n'est pas « effectuée », le dossier ne peut pas être accepté.
        var visiteId = (await bailleur.Client.GetAsync($"/candidature/{dossiers[0].Id}").Attendre<CandidatureResponseDto>()).Visites.Single().Id;
        await f.ModifierBaseAsync(async db => (await db.Visites.SingleAsync(v => v.Id == visiteId)).DateHeure = DateTime.UtcNow.AddHours(-1));
        await bailleur.Client.PostJson($"/visite/{visiteId}/cloturer", new { effectuee = false }).Verifier(HttpStatusCode.OK);
        await bailleur.Client.PostJson($"/candidature/{dossiers[0].Id}/accepter", new { }).Erreur(HttpStatusCode.Conflict);

        // Annuler une visite libère une place.
        await bailleur.Client.PostJson($"/candidature/{dossiers[1].Id}/annuler-visite").Verifier(HttpStatusCode.OK);
        await bailleur.Client.PostJson($"/candidature/{dossiers[3].Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(1) }).Verifier(HttpStatusCode.OK);

        // L'admin choisit le taux de frais, borné entre 2 et 5 %.
        await VisiteEffectueeSurDossierExistantAsync(bailleur, candidats[2], dossiers[2].Id);
        await admin.Client.PostJson($"/candidature/{dossiers[2].Id}/accepter", new { tauxFrais = 0.06m }).Erreur(HttpStatusCode.Conflict);
        var accepte = await admin.Client.PostJson($"/candidature/{dossiers[2].Id}/accepter", new { tauxFrais = 0.05m }).Attendre<CandidatureResponseDto>();
        var financement = (await candidats[2].Client.GetAsync("/financement/lister").AttendreDonnees<FinancementLocationResponseDto>()).Single();
        Assert.Equal(accepte.FinancementLocationId, financement.Id);
        Assert.Equal(0.05m, financement.TauxFrais);
        // Apport fixé à 2 mois : 4 000 000 × 1,05 = 4 200 000 sur 4 mensualités.
        Assert.Equal(4, financement.NombreMensualites);
        Assert.Equal(1_050_000m, financement.MontantMensualite);
        // Logement attribué : plus de nouvelle visite.
        await bailleur.Client.PostJson($"/candidature/{dossiers[3].Id}/visite", new { dateHeure = DateTime.UtcNow.AddDays(2) }).Erreur(HttpStatusCode.Conflict);
    }

    private async Task VisiteEffectueeSurDossierExistantAsync(Compte bailleur, Compte locataire, int candidatureId)
    {
        var visiteId = (await locataire.Client.GetAsync($"/candidature/{candidatureId}").Attendre<CandidatureResponseDto>()).Visites.Single().Id;
        await f.ModifierBaseAsync(async db => (await db.Visites.SingleAsync(v => v.Id == visiteId)).DateHeure = DateTime.UtcNow.AddHours(-1));
        await bailleur.Client.PostJson($"/visite/{visiteId}/cloturer", new { effectuee = true }).Verifier(HttpStatusCode.OK);
    }
}
