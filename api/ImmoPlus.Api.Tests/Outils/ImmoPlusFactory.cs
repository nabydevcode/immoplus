namespace ImmoPlus.Api.Tests.Outils;

using ImmoPlus.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// API complète, en mémoire (TestServer), sur une base SQLite propre à cette instance, dans un dossier temporaire
/// (jamais api/ImmoPlus.Api/immoplus.db). Les emails partent vers un faux serveur SMTP local, les services planifiés
/// de l'API (rappels, sauvegarde…) sont retirés : les tests les déclenchent eux-mêmes (POST /rappels/executer).
/// </summary>
public class ImmoPlusFactory : WebApplicationFactory<Program>
{
    public const string SecretWebhook = "secret-webhook-des-tests";

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "immoplus-tests", Guid.NewGuid().ToString("N"));

    public ImmoPlusFactory()
    {
        Directory.CreateDirectory(_dossier);
        CheminBase = Path.Combine(_dossier, "immoplus-tests.db");
    }

    public FauxServeurSmtp Smtp { get; } = new();

    public string CheminBase { get; }

    private string ChaineConnexion => $"Data Source={CheminBase}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Dossier temporaire comme racine : Storage/ (photos, documents, états des lieux) et sauvegardes y sont écrits,
        // et aucun appsettings*.json du projet n'est chargé (pas de SMTP MailHog, pas de base réelle).
        builder.UseContentRoot(_dossier);
        builder.UseEnvironment("Tests");

        var parametres = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ChaineConnexion,
            ["Jwt:Issuer"] = "ImmoPlus.Api",
            ["Jwt:Cle"] = "cle-jwt-des-tests-automatises-immoplus-suffisamment-longue-0123456789",
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = Smtp.Port.ToString(),
            ["Smtp:Expediteur"] = "no-reply@immoplus.test",
            ["Frontend:Url"] = "http://front.immoplus.test",
            ["Rappels:Actif"] = "false",
            ["PaiementMobile:Mode"] = "Simulation",
            ["PaiementMobile:SecretWebhook"] = SecretWebhook,
            ["Sms:Mode"] = "Desactive",
            ["Sauvegardes:Dossier"] = Path.Combine(_dossier, "Sauvegardes"),
            ["Cors:OriginesAutorisees:0"] = "http://front.immoplus.test",
            // Journaux limités aux avertissements : les requêtes SQL ralentiraient les tests (dotnet test --logger "console;verbosity=detailed" pour les voir).
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning"
        };
        foreach (var (cle, valeur) in parametres)
        {
            builder.UseSetting(cle, valeur);
        }
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(parametres));

        builder.ConfigureTestServices(services =>
        {
            // Ceinture et bretelles : le DbContext est réenregistré sur la base temporaire, quelle que soit la configuration lue.
            foreach (var descripteur in services.Where(d =>
                         d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>)).ToList())
            {
                services.Remove(descripteur);
            }
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(ChaineConnexion));

            // Services planifiés de l'API (rappels, sauvegarde…) retirés : pas de traitement en tâche de fond pendant les tests.
            var assemblageApi = typeof(Program).Assembly;
            foreach (var descripteur in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType?.Assembly == assemblageApi
                             || d.ImplementationFactory?.Method.ReturnType.Assembly == assemblageApi)).ToList())
            {
                services.Remove(descripteur);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var hote = base.CreateHost(builder);
        using var scope = hote.Services.CreateScope();
        var chaine = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString() ?? "";
        if (!chaine.Contains(_dossier))
        {
            throw new InvalidOperationException($"Les tests doivent utiliser la base temporaire, pas « {chaine} »");
        }
        return hote;
    }

    /// <summary>Accès direct à la base de test, pour préparer un cas (ex. reculer une date de création de 25 h).</summary>
    public async Task ModifierBaseAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var contexte = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(contexte);
        await contexte.SaveChangesAsync();
    }

    public async Task<T> LireBaseAsync<T>(Func<AppDbContext, Task<T>> lecture)
    {
        using var scope = Services.CreateScope();
        return await lecture(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Smtp.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Fichier encore ouvert : le dossier temporaire sera nettoyé par le système.
        }
    }
}
