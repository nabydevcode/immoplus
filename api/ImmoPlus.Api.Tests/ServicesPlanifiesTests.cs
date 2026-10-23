namespace ImmoPlus.Api.Tests;

using System.Net;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Tests.Outils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Rappels planifiés : un seul exécutant sur l'ensemble des instances, une fois par intervalle.</summary>
public class ServicesPlanifiesTests(ImmoPlusFactory f) : IClassFixture<ImmoPlusFactory>
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(24);

    private async Task<RappelsResultatDto?> PassagePlanifieAsync()
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RappelsServices>().ExecuterSiDuAsync(Intervalle);
    }

    private string ChaineConnexion()
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    private Task ReculerDernierPassageAsync(TimeSpan duree) => f.ModifierBaseAsync(async db =>
    {
        var execution = await db.ExecutionsPlanifiees.SingleAsync(e => e.Nom == RappelsServices.NomExecution);
        execution.DerniereExecution = DateTime.UtcNow - duree;
    });

    [Fact]
    public async Task Un_seul_passage_par_intervalle_meme_lance_par_plusieurs_instances()
    {
        // Hôte de test démarré d'abord : WebApplicationFactory.Services n'est pas sûr en accès concurrent au premier appel.
        _ = ChaineConnexion();
        // Plusieurs instances démarrent ensemble : une seule exécute les rappels.
        var resultats = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(PassagePlanifieAsync)));
        Assert.Single(resultats, r => r is not null);
        var execution = await f.LireBaseAsync(db => db.ExecutionsPlanifiees.AsNoTracking().SingleAsync(e => e.Nom == RappelsServices.NomExecution));
        Assert.Equal(Environment.MachineName, execution.Instance);

        // Redémarrage peu après (ou autre instance) : pas de nouveau passage avant 24 h.
        Assert.Null(await PassagePlanifieAsync());
        await ReculerDernierPassageAsync(TimeSpan.FromHours(20));
        Assert.Null(await PassagePlanifieAsync());

        // 24 h plus tard : un nouveau passage.
        await ReculerDernierPassageAsync(TimeSpan.FromHours(25));
        Assert.NotNull(await PassagePlanifieAsync());
        Assert.Null(await PassagePlanifieAsync());
    }

    [Fact]
    public async Task Une_autre_instance_detient_le_verrou_le_passage_est_ignore_et_la_route_admin_attend()
    {
        var admin = await f.AdminAsync();
        await PassagePlanifieAsync();
        await ReculerDernierPassageAsync(TimeSpan.FromDays(2));

        var verrou = await VerrouPlanifie.EssayerAsync(ChaineConnexion(), VerrouPlanifie.Rappels);
        Assert.NotNull(verrou);
        // Le second exécutant ne prend pas un verrou déjà détenu.
        Assert.Null(await VerrouPlanifie.EssayerAsync(ChaineConnexion(), VerrouPlanifie.Rappels));
        Assert.Null(await PassagePlanifieAsync());

        // La route admin attend la fin du passage en cours, puis s'exécute.
        var route = admin.Client.PostJson("/rappels/executer");
        await Task.Delay(1500);
        Assert.False(route.IsCompleted);
        await verrou!.DisposeAsync();
        await route.Verifier(HttpStatusCode.OK);

        // La route a enregistré son passage : le planifié ne repasse pas.
        Assert.Null(await PassagePlanifieAsync());
    }
}
