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
using Npgsql;

/// <summary>
/// API complète, en mémoire (TestServer), sur une base PostgreSQL propre à cette instance (« immoplus_test_… », créée
/// puis supprimée ; jamais la base réelle « immoplus »), et un dossier temporaire pour Storage/. Les emails partent vers
/// un faux serveur SMTP local, les services planifiés de l'API (rappels, boîte d'envoi…) sont retirés : les tests les
/// déclenchent eux-mêmes (POST /rappels/executer, EnvoyerMessagesAsync / EmailsPourAsync pour la boîte d'envoi).
/// <see cref="Creer"/> : réglages supplémentaires (S3, Redis…) et « seconde instance » sur la même base qu'une autre.
/// </summary>
public class ImmoPlusFactory : WebApplicationFactory<Program>
{
    public const string SecretWebhook = "secret-webhook-des-tests";
    private const string PrefixeBase = "immoplus_test_";

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "immoplus-tests", Guid.NewGuid().ToString("N"));
    private readonly string _nomBase;
    private readonly bool _proprietaireBase;
    private readonly IReadOnlyDictionary<string, string?> _reglages;

    public ImmoPlusFactory() : this(null, null)
    {
    }

    private ImmoPlusFactory(IReadOnlyDictionary<string, string?>? reglages, ImmoPlusFactory? memeBaseQue)
    {
        Directory.CreateDirectory(_dossier);
        _reglages = reglages ?? new Dictionary<string, string?>();
        if (memeBaseQue is null)
        {
            _nomBase = PrefixeBase + Guid.NewGuid().ToString("N");
            _proprietaireBase = true;
            ChaineConnexion = ServeurPostgresTests.CreerBaseAsync(_nomBase).GetAwaiter().GetResult();
        }
        else
        {
            // Seconde « instance » de l'API : même base, son propre serveur, ses propres dossier et mémoire.
            _nomBase = memeBaseQue._nomBase;
            ChaineConnexion = memeBaseQue.ChaineConnexion;
        }
    }

    /// <param name="reglages">Réglages ajoutés ou remplacés (ex. Stockage:Mode = S3, Redis:Connexion).</param>
    /// <param name="memeBaseQue">Partage la base de cette instance (simule une seconde instance de l'API) ; elle seule la supprime.</param>
    public static ImmoPlusFactory Creer(IReadOnlyDictionary<string, string?>? reglages = null, ImmoPlusFactory? memeBaseQue = null) =>
        new(reglages, memeBaseQue);

    /// <summary>Dossier servant de racine de contenu (Storage/ en mode Disque).</summary>
    public string Dossier => _dossier;

    public FauxServeurSmtp Smtp { get; } = new();

    private string ChaineConnexion { get; }

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
        foreach (var (cle, valeur) in _reglages)
        {
            parametres[cle] = valeur;
        }
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
            services.AddDbContext<AppDbContext>(options => AppDbContext.Configurer(options, ChaineConnexion));

            // Services planifiés de l'API (rappels, boîte d'envoi…) retirés : pas de traitement en tâche de fond pendant les tests.
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
        if (!chaine.Contains(_nomBase))
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

    /// <summary>Un passage du service d'envoi : tous les messages dus de la boîte d'envoi partent (ou échouent).</summary>
    public async Task<int> EnvoyerMessagesAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ImmoPlus.Api.Services.EnvoiMessagesServices>().TraiterToutAsync();
    }

    /// <summary>Emails reçus par ce destinataire, après envoi des messages en attente de la boîte d'envoi.</summary>
    public async Task<IReadOnlyList<EmailCapture>> EmailsPourAsync(string destinataire)
    {
        await EnvoyerMessagesAsync();
        return Smtp.EmailsPour(destinataire);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Smtp.DisposeAsync();
        if (_proprietaireBase)
        {
            await ServeurPostgresTests.SupprimerBaseAsync(_nomBase, ChaineConnexion);
        }
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
