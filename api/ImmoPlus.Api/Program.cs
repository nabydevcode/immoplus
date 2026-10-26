using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Hubs;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Middleware;
using ImmoPlus.Api.Models.Enums;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;
using System.IO.Compression;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.DataProtection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Journaux structurés : une ligne JSON par événement sur la console (horodatage UTC, niveau, catégorie, message et champs,
// portée CorrelationId de la requête). Journaux:Format = Texte pour une lecture humaine en local.
if (!string.Equals(builder.Configuration["Journaux:Format"], "Texte", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        // Accents lisibles tels quels (« Requête » et non « Requ\u00EAte ») ; le JSON reste valide.
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    });
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL : chaîne ConnectionStrings:Default (en Docker, fournie par infra/postgres.env avec le mot de passe).
// Requêtes SQL de plus de Journaux:SeuilRequeteLenteMs (200 ms) : avertissement dans le journal.
builder.Services.AddSingleton<RequetesLentesInterceptor>();
// Pool Npgsql plafonné par instance (BaseDeDonnees:TaillePoolMax, 40 par défaut ; Npgsql en ouvrirait jusqu'à 100) si la chaîne
// ne fixe pas déjà « Maximum Pool Size » : plusieurs instances × 100 dépasseraient max_connections=100 de PostgreSQL.
var chaineBase = AppDbContext.AvecPoolParDefaut(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default (PostgreSQL) n'est pas configurée"),
    builder.Configuration.GetValue("BaseDeDonnees:TaillePoolMax", AppDbContext.TaillePoolParDefaut));
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    AppDbContext.Configurer(options, chaineBase)
        .AddInterceptors(services.GetRequiredService<RequetesLentesInterceptor>()));

builder.Services.AddScoped<UtilisateurServices>();
builder.Services.AddScoped<DiffusionAdmins>();
builder.Services.AddScoped<AppartementServices>();
builder.Services.AddScoped<TokenServices>();
builder.Services.AddScoped<EmailServices>();
// Copie IMAP des emails envoyés (Imap:Actif) : singleton, une connexion réutilisée par instance.
builder.Services.AddSingleton<CopieEnvoyesServices>();
builder.Services.AddScoped<NotificationServices>();
builder.Services.AddScoped<LocationServices>();
builder.Services.AddScoped<FinancementServices>();
builder.Services.AddScoped<MensualiteServices>();
builder.Services.AddScoped<PaiementServices>();
builder.Services.AddScoped<CandidatureServices>();
builder.Services.AddScoped<DocumentServices>();
builder.Services.AddScoped<ReservationServices>();
builder.Services.AddScoped<PhotoServices>();
builder.Services.AddScoped<LoyerServices>();
builder.Services.AddScoped<RappelsServices>();
builder.Services.AddScoped<TableauDeBordServices>();
builder.Services.AddScoped<AvisServices>();
builder.Services.AddScoped<MessageServices>();
builder.Services.AddScoped<PaiementMobileServices>();
// Fournisseur simulé tant qu'il n'y a pas de contrat marchand : les vrais opérateurs implémenteront la même interface.
builder.Services.AddScoped<IFournisseurPaiementMobile, FournisseurSimule>();
builder.Services.AddScoped<EtatDesLieuxServices>();
builder.Services.AddScoped<FavoriServices>();
builder.Services.AddScoped<TableauDeBordHoteServices>();
// Vague B : journal des actions, signalements, vérification d'identité, exports Excel.
builder.Services.AddScoped<JournalServices>();
builder.Services.AddScoped<SignalementServices>();
builder.Services.AddScoped<IdentiteServices>();
builder.Services.AddScoped<ExportServices>();
// Liens des emails : Frontend:Url, ou en développement l'origine autorisée de la requête (Frontend:OriginesAutorisees, ex. tunnel).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LiensFrontend>();
// SMS / WhatsApp : Sms:Mode = Desactive, Simulation (rien ne part) ou PasserelleAndroid (vrais SMS via un téléphone Android).
builder.Services.AddScoped<SmsServices>();
if (string.Equals(builder.Configuration["Sms:Mode"], "PasserelleAndroid", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IFournisseurSms, FournisseurSmsPasserelleAndroid>(c => c.Timeout = TimeSpan.FromSeconds(8));
}
else
{
    builder.Services.AddScoped<IFournisseurSms, FournisseurSmsSimule>();
}
// Documents PDF (quittances, reçus, contrat, état des lieux).
builder.Services.AddScoped<DocumentsPdfServices>();
// Sauvegardes PostgreSQL (pg_dump quotidien) faites par le service compose « sauvegarde-db » : l'API les liste seulement.
builder.Services.AddSingleton<SauvegardeServices>();
// Horodatages UTC sérialisés avec « Z » (dates « jour » à minuit inchangées), en HTTP comme dans le hub SignalR.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new ConvertisseurDateUtc()));
// Fichiers (photos, documents, états des lieux, pièces d'identité) : Stockage:Mode = Disque (défaut, dossier Storage/)
// ou S3 (MinIO / tout service S3, indispensable avec plusieurs instances). Mêmes clés dans les deux modes.
var modeStockage = builder.Configuration["Stockage:Mode"] ?? "Disque";
if (string.Equals(modeStockage, "S3", StringComparison.OrdinalIgnoreCase))
{
    var optionsS3 = OptionsStockageS3.Lire(builder.Configuration);
    builder.Services.AddSingleton<IStockageFichiers>(_ => new StockageS3(optionsS3.CreerClient(), optionsS3.Bucket));
}
else if (string.Equals(modeStockage, "Disque", StringComparison.OrdinalIgnoreCase))
{
    // Stockage:Disque:Dossier : chemin absolu (production : /var/lib/immoplus/storage) ou relatif à la racine de l'API ; défaut Storage/.
    builder.Services.AddSingleton<IStockageFichiers>(sp =>
        new StockageDisque(StockageDisque.DossierConfigure(builder.Configuration, sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath)));
}
else
{
    throw new InvalidOperationException($"Stockage:Mode « {modeStockage} » inconnu : utilisez Disque ou S3");
}

// Redis (Redis:Connexion, ex. « redis:6379,password=…,abortConnect=false ») : facultatif. Renseigné, il partage entre les
// instances le temps réel SignalR, les compteurs de tentatives et le cache du statut des comptes ; vide, l'API fonctionne
// seule, comme avant (compteurs en mémoire, statut lu en base).
var connexionRedis = builder.Configuration["Redis:Connexion"];
var redisActif = !string.IsNullOrWhiteSpace(connexionRedis);
// Préfixe des clés et canaux Redis (Redis:Prefixe, « immoplus » par défaut) : deux environnements peuvent partager un Redis.
var prefixeRedis = PrefixeRedis.Lire(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CompteursTentativesMemoire>();
if (redisActif)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(connexionRedis!);
        options.AbortOnConnectFail = false; // Redis absent au démarrage : l'API démarre quand même et se reconnecte seule.
        options.ClientName = $"immoplus-api-{Environment.MachineName}";
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<ICompteursTentatives, CompteursTentativesRedis>();
    builder.Services.AddSingleton<ICacheStatutUtilisateur, CacheStatutUtilisateurRedis>();
    builder.Services.AddSingleton<ICacheHotes, CacheHotesRedis>();
}
else
{
    builder.Services.AddSingleton<ICompteursTentatives>(sp => sp.GetRequiredService<CompteursTentativesMemoire>());
    builder.Services.AddSingleton<ICacheStatutUtilisateur, CacheStatutUtilisateurBase>();
    builder.Services.AddSingleton<ICacheHotes, CacheHotesMemoire>();
}
// Connexion : 5 échecs par email ou 20 par IP en 15 min ; mot de passe oublié : 5 demandes par email en 15 min.
builder.Services.AddSingleton<LimiteurTentatives>();
// Boîte d'envoi : emails et SMS enregistrés avec l'action métier, envoyés en arrière-plan (nouvelles tentatives).
builder.Services.AddScoped<BoiteEnvoiServices>();
builder.Services.AddScoped<EnvoiMessagesServices>();
builder.Services.AddHostedService<EnvoiMessagesHostedService>();
// Rappels des échéances (mensualités, loyers) toutes les 24 h, une seule instance à la fois ; désactivable via Rappels:Actif.
builder.Services.AddHostedService<RappelsHostedService>();
// Maintenance des réservations et des loyers (expirations, fins de séjour, loyers du mois) toutes les 2 min, par lots bornés,
// une seule instance à la fois : plus jamais pendant une lecture de liste ou de tableau de bord. Désactivable : Maintenance:Actif.
builder.Services.AddScoped<MaintenanceServices>();
builder.Services.AddHostedService<MaintenanceHostedService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Cle"]!))
    };
    // Les WebSockets ne portent pas d'en-tête Authorization : SignalR passe le jeton dans l'URL (access_token).
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = contexte =>
        {
            var jeton = contexte.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(jeton) && contexte.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                contexte.Token = jeton;
            }
            return Task.CompletedTask;
        }
    };
});
var signalR = builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new ConvertisseurDateUtc()));
if (redisActif)
{
    // Backplane : un message poussé par une instance atteint les clients connectés à toutes les autres.
    signalR.AddStackExchangeRedis(connexionRedis!, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal($"{prefixeRedis}-signalr");
        options.Configuration.AbortOnConnectFail = false;
    });
}

// Compression des réponses (JSON surtout) : Brotli, sinon Gzip, au niveau le plus rapide (le processeur compte plus que
// quelques octets). Les photos (JPEG) ne sont pas recompressées. En production, nginx termine le HTTPS et l'API reçoit du HTTP.
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// Cache de sortie des listes publiques anonymes (30 s) : en mémoire, ou dans Redis pour qu'il soit partagé et vidé
// sur toutes les instances à la fois.
builder.Services.AddOutputCache(CachePublic.Configurer);
if (redisActif)
{
    builder.Services.AddStackExchangeRedisOutputCache(options =>
    {
        var configuration = ConfigurationOptions.Parse(connexionRedis!);
        configuration.AbortOnConnectFail = false;
        options.ConfigurationOptions = configuration;
        options.InstanceName = $"{prefixeRedis}-cache:";
    });
}

// Santé : /sante/vivant (le processus répond) et /sante/pret (PostgreSQL, Redis s'il est configuré, S3 en mode S3).
var sante = builder.Services.AddHealthChecks()
    .AddCheck<SantePostgres>("postgresql", tags: [ReponsesSante.Pret], timeout: TimeSpan.FromSeconds(3));
if (redisActif)
{
    sante.AddCheck<SanteRedis>("redis", tags: [ReponsesSante.Pret], timeout: TimeSpan.FromSeconds(3));
}
if (string.Equals(modeStockage, "S3", StringComparison.OrdinalIgnoreCase))
{
    sante.AddCheck<SanteStockage>("s3", tags: [ReponsesSante.Pret], timeout: TimeSpan.FromSeconds(3));
}

// Métriques OpenTelemetry au format Prometheus sur /metriques (accès restreint : voir AccesMetriques) : requêtes HTTP et
// durées par route (histogrammes → p50/p95/p99), erreurs, connexions SignalR, requêtes SQL (Npgsql), processus (CPU,
// mémoire, GC) et boîte d'envoi.
builder.Services.AddSingleton<MetriquesImmoPlus>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(ressource => ressource.AddService("immoplus-api", serviceInstanceId: Environment.MachineName))
    .WithMetrics(metriques => metriques
        .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.Http.Connections",
            "Microsoft.AspNetCore.Diagnostics", "Npgsql", "System.Runtime", MetriquesImmoPlus.NomCompteur)
        .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 0.75, 1, 1.5, 2.5, 5, 10]
        })
        .AddPrometheusExporter());

// Liste d'origines configurable (appsettings) : prête pour un futur client mobile / domaine de prod.
var originesAutorisees = builder.Configuration.GetSection("Cors:OriginesAutorisees").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AutoriserClients", policy =>
    {
        policy.WithOrigins(originesAutorisees)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddAuthorization();

// DataProtection n'est utilisé par rien (jetons JWT signés par Jwt:Cle, pas de cookie, antiforgery désactivé sur les formulaires) :
// clés éphémères en mémoire, plutôt qu'un trousseau écrit dans le conteneur (non partagé entre instances, avertissements
// au démarrage). Si un jour une donnée protégée doit survivre à un redémarrage, persister les clés (PostgreSQL ou Redis).
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
// Le service de démarrage de DataProtection chargerait quand même le trousseau sur disque (et ses avertissements) : retiré.
foreach (var demarrage in builder.Services.Where(d => d.ServiceType == typeof(IHostedService)
             && d.ImplementationType?.FullName == "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService").ToList())
{
    builder.Services.Remove(demarrage);
}

var app = builder.Build();

// Applique automatiquement les migrations en attente au démarrage (évite une étape manuelle à chaque déploiement).
// Plusieurs instances démarrées ensemble : EF Core 9+ pose un verrou de migration, une seule les applique.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
app.Logger.LogInformation("Stockage des fichiers : {Mode}{Detail} ; Redis : {Redis}", modeStockage,
    string.Equals(modeStockage, "S3", StringComparison.OrdinalIgnoreCase)
        ? $" (bucket « {builder.Configuration["Stockage:S3:Bucket"]} » sur {builder.Configuration["Stockage:S3:Url"]})"
        : $" (dossier {StockageDisque.DossierConfigure(builder.Configuration, app.Environment.ContentRootPath)})",
    redisActif ? "actif (SignalR, limiteur, cache du statut partagés)" : "désactivé (instance unique)");
// Emails : hôte, port et chiffrement retenus (jamais le mot de passe) ; sans hôte, ils s'accumulent dans la boîte d'envoi.
var hoteSmtp = builder.Configuration["Smtp:Host"];
if (string.IsNullOrWhiteSpace(hoteSmtp))
{
    app.Logger.LogWarning("SMTP non configuré (Smtp:Host vide) : les emails restent en attente dans la boîte d'envoi jusqu'à sa configuration");
}
else
{
    using var scope = app.Services.CreateScope();
    var email = scope.ServiceProvider.GetRequiredService<EmailServices>();
    app.Logger.LogInformation("SMTP : {Hote}:{Port}, chiffrement {Chiffrement}, authentification {Authentification}", hoteSmtp, email.Port,
        EmailServices.OptionSocket(email.Securite, email.Port),
        string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Utilisateur"]) ? "aucune" : "par identifiant");
}
{
    var copie = app.Services.GetRequiredService<CopieEnvoyesServices>();
    if (copie.EstActive)
    {
        app.Logger.LogInformation("Copie IMAP des emails envoyés : {Hote}:{Port}, chiffrement {Chiffrement}, compte {Compte}, dossier {Dossier}",
            builder.Configuration["Imap:Host"], copie.Port, CopieEnvoyesServices.OptionSocket(copie.Securite, copie.Port),
            copie.Utilisateur ?? "(aucun)", builder.Configuration["Imap:Dossier"] is { Length: > 0 } dossier ? dossier : "détection automatique");
    }
}

// Derrière nginx, l'adresse du client arrive dans X-Forwarded-For : seuls la boucle locale et les réseaux listés dans
// ProxysDeConfiance:Reseaux (ex. « 172.16.0.0/12 », réseau Docker du répartiteur ; vide par défaut) sont crus, pour que
// la limitation des connexions par IP vise le vrai client sans être contournable par un en-tête forgé.
var optionsProxys = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
foreach (var reseau in builder.Configuration.GetSection("ProxysDeConfiance:Reseaux").Get<string[]>() ?? [])
{
    if (!string.IsNullOrWhiteSpace(reseau))
    {
        optionsProxys.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(reseau.Trim()));
    }
}
app.UseForwardedHeaders(optionsProxys);

// Identifiant de corrélation (X-Correlation-Id) et une ligne de journal par requête, avant tout le reste.
app.UseMiddleware<MiddlewareCorrelation>();

// /metriques : jeton (Metriques:Jeton) ou réseau interne uniquement ; 404 pour les autres (rien n'indique que la route existe).
var jetonMetriques = builder.Configuration["Metriques:Jeton"];
app.Use(async (contexte, suivant) =>
{
    if (contexte.Request.Path.StartsWithSegments(AccesMetriques.Chemin) && !AccesMetriques.Autorise(contexte, jetonMetriques))
    {
        contexte.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await suivant();
});

// Pas de compression sur le temps réel SignalR (flux longs et WebSockets).
app.UseWhen(contexte => !contexte.Request.Path.StartsWithSegments("/hubs"), branche => branche.UseResponseCompression());

app.UseMiddleware<MiddlewareException>();

// Photos et documents sont resservis tels quels : le navigateur ne doit jamais « deviner » un autre type (ex. HTML déguisé en image).
app.Use(async (contexte, suivant) =>
{
    contexte.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await suivant();
});

// Swagger : documentation vivante en développement ; masqué en production (liste des routes = information utile à un attaquant),
// sauf si Swagger:Actif=true est demandé explicitement.
if (app.Configuration.GetValue("Swagger:Actif", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Derrière nginx (qui termine HTTPS et parle à l'API en HTTP), RedirectionHttps:Actif = false : pas de redirection
// ni d'avertissement « Failed to determine the https port ».
if (builder.Configuration.GetValue("RedirectionHttps:Actif", true))
{
    app.UseHttpsRedirection();
}
app.UseCors("AutoriserClients");
app.UseAuthentication();
// Un jeton reste valide jusqu'à son expiration : on vérifie donc à chaque requête que le compte est toujours actif
// (désactivé par un admin → 401 immédiat, sans attendre l'expiration du jeton).
app.Use(async (contexte, suivant) =>
{
    if (contexte.User.Identity?.IsAuthenticated == true)
    {
        // Avec Redis, statut gardé 30 s et effacé à chaque changement (voir CacheStatutUtilisateurRedis) ; sinon lu en base.
        var statut = await contexte.RequestServices.GetRequiredService<ICacheStatutUtilisateur>()
            .LireAsync(contexte.User.ObtenirId(), contexte.RequestServices.GetRequiredService<AppDbContext>());
        if (statut != StatutUtilisateur.Valide)
        {
            contexte.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await contexte.Response.WriteAsJsonAsync(new { message = statut == StatutUtilisateur.Desactive
                ? "Votre compte a été désactivé par un administrateur"
                : "Votre session n'est plus valide, reconnectez-vous" });
            return;
        }
    }
    await suivant();
});
app.UseAuthorization();

// Une modification réussie de ce qu'affichent les listes publiques vide leur cache (avant l'envoi de la réponse : la
// relecture qui suit voit déjà la nouvelle version, sur toutes les instances avec Redis).
app.Use(async (contexte, suivant) =>
{
    if (CachePublic.DoitVider(contexte.Request))
    {
        contexte.Response.OnStarting(async () =>
        {
            if (contexte.Response.StatusCode < 400)
            {
                await contexte.RequestServices.GetRequiredService<IOutputCacheStore>().EvictByTagAsync(CachePublic.Etiquette, default);
            }
        });
    }
    await suivant();
});
app.UseOutputCache();

app.MapHealthChecks("/sante/vivant", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = ReponsesSante.EcrireAsync });
app.MapHealthChecks("/sante/pret", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReponsesSante.Pret), ResponseWriter = ReponsesSante.EcrireAsync });
app.MapPrometheusScrapingEndpoint(AccesMetriques.Chemin);
// Compteur ImmoPlus créé dès le démarrage (la boîte d'envoi est lue à la demande de /metriques).
app.Services.GetRequiredService<MetriquesImmoPlus>();

// --- Utilisateur ---
app.MapPost("/utilisateur/inscription", async (InscriptionDto dto, UtilisateurServices services) =>
{
    var utilisateur = await services.InscrireAsync(dto);
    return Results.Created($"/utilisateur/{utilisateur.Id}", utilisateur);
});

// Lien de l'email d'inscription ({Frontend}/confirmer-email?jeton=…) : 200 { message, statut } (aussi si déjà confirmée),
// 400 si le lien est inconnu, remplacé ou expiré.
app.MapPost("/utilisateur/confirmer-email", async (ConfirmerEmailDto dto, UtilisateurServices services) =>
    Results.Ok(await services.ConfirmerEmailAsync(dto)));

// Toujours 200 et le même message (ne révèle pas si le compte existe) ; 3 renvois par adresse et par heure au plus.
app.MapPost("/utilisateur/renvoyer-confirmation", async (RenvoyerConfirmationDto dto, UtilisateurServices services) =>
{
    await services.RenvoyerConfirmationAsync(dto);
    return Results.Ok(new { message = UtilisateurServices.MessageRenvoiConfirmation });
});

// 5 échecs par email ou 20 par adresse IP en 15 min → 429 pendant 15 min.
// Compte dont l'email n'est pas confirmé : 409 { message, code: "EmailNonConfirme" }.
app.MapPost("/utilisateur/connexion", async (ConnexionDto dto, UtilisateurServices services, HttpContext contexte) =>
{
    return Results.Ok(await services.ConnexionAsync(dto, contexte.Connection.RemoteIpAddress?.ToString()));
});

app.MapPost("/utilisateur/mot-de-passe-oublie", async (MotDePasseOublieDto dto, UtilisateurServices services) =>
{
    await services.DemanderReinitialisationAsync(dto);
    return Results.Ok(new { message = "Un lien de réinitialisation a été envoyé à votre adresse email." });
});

app.MapPost("/utilisateur/reinitialiser-mot-de-passe", async (ReinitialisationMotDePasseDto dto, UtilisateurServices services) =>
{
    await services.ReinitialiserMotDePasseAsync(dto);
    return Results.NoContent();
});

// Filtres facultatifs : role (Locataire/Bailleur/Admin), statut (EnAttente/Valide/Refuse/Desactive), q (nom, email ou téléphone),
// emailConfirme (true/false ; « À valider » = statut=EnAttente&emailConfirme=true). 100 par page au plus.
app.MapGet("/utilisateur/lister", async (UtilisateurServices services, int? page, int? taille, string? role, string? statut, string? q, bool? emailConfirme) =>
    Results.Ok(await services.ListerAsync(page ?? 1, taille ?? 20, role, statut, q, emailConfirme)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// Seul un admin crée un admin (l'inscription publique refuse ce rôle dès qu'un admin actif existe).
app.MapPost("/utilisateur/admin", async (CreerAdminDto dto, UtilisateurServices services) =>
{
    var admin = await services.CreerAdminAsync(dto);
    return Results.Created($"/utilisateur/{admin.Id}", admin);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// 409 tant que l'utilisateur n'a pas confirmé son adresse email (sauf compte Admin).
app.MapPost("/utilisateur/{id:int}/valider", async (int id, UtilisateurServices services) =>
    Results.Ok(await services.ValiderAsync(id))).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/utilisateur/{id:int}/refuser", async (int id, UtilisateurServices services) =>
    Results.Ok(await services.RefuserAsync(id))).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/utilisateur/{id:int}/desactiver", async (int id, DesactiverUtilisateurDto? dto, UtilisateurServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.DesactiverAsync(id, user.ObtenirId(), dto?.Motif))).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/utilisateur/{id:int}/reactiver", async (int id, UtilisateurServices services) =>
    Results.Ok(await services.ReactiverAsync(id))).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Profil de l'utilisateur connecté (tous rôles) : l'id vient du token, jamais du body ---
app.MapGet("/utilisateur/moi", async (UtilisateurServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ObtenirProfilAsync(user.ObtenirId()))).RequireAuthorization();

app.MapPut("/utilisateur/moi", async (ModifierProfilDto dto, UtilisateurServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ModifierProfilAsync(user.ObtenirId(), dto))).RequireAuthorization();

// 50 derniers SMS / WhatsApp (envoyés, simulés ou en échec) de l'utilisateur connecté.
app.MapGet("/utilisateur/moi/sms", async (SmsServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerPourUtilisateurAsync(user.ObtenirId()))).RequireAuthorization();

// Vérification d'identité (multipart : type, recto, verso facultatif ; image ou PDF, 10 Mo max chacun).
app.MapPost("/utilisateur/moi/identite", async ([FromForm] UploadIdentiteForm? form, IdentiteServices services, ClaimsPrincipal user) =>
{
    form ??= new UploadIdentiteForm();
    var demande = await services.EnvoyerAsync(user.ObtenirId(), form.Type, form.Recto, form.Verso);
    return Results.Created("/utilisateur/moi/identite", demande);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Locataire")).DisableAntiforgery();

app.MapGet("/utilisateur/moi/identite", async (IdentiteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.StatutAsync(user.ObtenirId()))).RequireAuthorization();

app.MapPost("/utilisateur/moi/mot-de-passe", async (ChangerMotDePasseDto dto, UtilisateurServices services, ClaimsPrincipal user) =>
{
    await services.ChangerMotDePasseAsync(user.ObtenirId(), dto);
    return Results.NoContent();
}).RequireAuthorization();

// --- Tableau de bord admin ---
app.MapGet("/admin/tableau-de-bord", async (TableauDeBordServices services) =>
    Results.Ok(await services.ObtenirAsync())).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Historique des SMS / WhatsApp (admin), du plus récent au plus ancien ; 100 par page au plus ---
app.MapGet("/admin/sms", async (SmsServices services, int? page, int? taille) =>
    Results.Ok(await services.ListerAsync(page ?? 1, taille ?? 20))).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Boîte d'envoi des emails et SMS (admin), du plus récent au plus ancien ; statut : EnAttente, Envoye, Echec ou Abandonne ---
app.MapGet("/admin/messages-sortants", async (BoiteEnvoiServices services, string? statut, int? page, int? taille) =>
    Results.Ok(await services.ListerAsync(statut, page ?? 1, taille ?? 20))).RequireAuthorization(policy => policy.RequireRole("Admin"));

// Relance (SMTP corrigé…) : statut = Abandonne ou Echec ; 5 nouvelles tentatives, envoi dans les secondes qui suivent.
app.MapPost("/admin/messages-sortants/relancer", async (BoiteEnvoiServices services, JournalServices journal, string? statut) =>
{
    var relances = await services.RelancerAsync(statut);
    await journal.EnregistrerAsync("message-sortant.relancer", "MessageSortant", null, $"{relances} message(s) {statut?.Trim()} relancé(s)");
    return Results.Ok(new RelanceMessagesDto { Relances = relances });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/admin/messages-sortants/{id:int}/relancer", async (int id, BoiteEnvoiServices services, JournalServices journal) =>
{
    var relances = await services.RelancerAsync(null, id);
    await journal.EnregistrerAsync("message-sortant.relancer", "MessageSortant", id, $"Message sortant n° {id} relancé");
    return Results.Ok(new RelanceMessagesDto { Relances = relances });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Sauvegardes de la base (admin) ---
app.MapGet("/admin/sauvegardes", (SauvegardeServices services) =>
    Results.Ok(services.Lister())).RequireAuthorization(policy => policy.RequireRole("Admin"));

// Sauvegarde manuelle : plus faite par l'API (pg_dump côté serveur), toujours 409 « Sauvegarde gérée par le serveur ».
app.MapPost("/admin/sauvegardes", () => Results.Created("", SauvegardeServices.Creer()))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// Nom strictement contrôlé (immoplus-yyyyMMdd-HHmm.dump) : aucun autre fichier du serveur ne peut être téléchargé.
app.MapGet("/admin/sauvegardes/{nom}", async (string nom, SauvegardeServices services, JournalServices journal) =>
{
    var chemin = services.Chemin(nom);
    await journal.EnregistrerAsync("sauvegarde.telecharger", "Sauvegarde", null, $"Téléchargement de la sauvegarde {nom}");
    // Format personnalisé pg_dump (-Fc) : à restaurer avec pg_restore (infra/restaurer-postgres.sh).
    return Results.File(chemin, "application/octet-stream", nom);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Journal des actions (admin), du plus récent au plus ancien ; 200 par page au plus ---
// action : code exact ou préfixe (« utilisateur ») ; du / au : yyyy-MM-dd (inclus) ; q : texte libre.
app.MapGet("/admin/journal", async (JournalServices services, int? page, int? taille, int? utilisateurId, string? action, string? cible,
    int? cibleId, string? du, string? au, string? q) =>
    Results.Ok(await services.ListerAsync(page ?? 1, taille ?? 50, utilisateurId, action, cible, cibleId,
        DatesJour.Lire(du, "du"), DatesJour.Lire(au, "au"), q)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Exports Excel (admin) : paiements, loyers, mensualites, reservations, utilisateurs, locations ; du / au facultatifs (yyyy-MM-dd) ---
app.MapGet("/admin/exports/{type}.xlsx", async (string type, string? du, string? au, ExportServices services) =>
{
    var fichier = await services.ExporterAsync(type, DatesJour.Lire(du, "du"), DatesJour.Lire(au, "au"));
    return Results.File(fichier.Contenu, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fichier.NomFichier);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Pastilles du menu admin : signalements « Nouveau », demandes d'identité en attente ---
app.MapGet("/admin/compteurs", async (SignalementServices signalements, IdentiteServices identites) =>
    Results.Ok(new CompteursAdminDto
    {
        SignalementsNouveaux = await signalements.NombreNouveauxAsync(),
        IdentitesEnAttente = await identites.NombreEnAttenteAsync()
    })).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Signalements ---
// Public (jeton facultatif) : email obligatoire sans connexion ; 5 par heure (429) ; doublon ouvert sur la même cible : 409.
app.MapPost("/signalements", async (CreerSignalementDto dto, SignalementServices services, ClaimsPrincipal user, HttpContext contexte) =>
{
    var connecte = user.Identity?.IsAuthenticated == true;
    var signalement = await services.CreerAsync(dto, connecte ? user.ObtenirId() : null, contexte.Connection.RemoteIpAddress?.ToString());
    return Results.Created($"/admin/signalements/{signalement.Id}", signalement);
});

app.MapGet("/admin/signalements", async (SignalementServices services, string? statut, string? cible, int? page, int? taille) =>
    Results.Ok(await services.ListerAsync(statut, cible, page ?? 1, taille ?? 20)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/admin/signalements/{id:int}/traiter", async (int id, TraiterSignalementDto dto, SignalementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.TraiterAsync(id, dto, user.ObtenirId())))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Vérification d'identité ---
// Paginé (page, taille ≤ 100) : en attente d'abord, puis les plus récentes.
app.MapGet("/admin/identites", async (IdentiteServices services, string? statut, int? page, int? taille) =>
    Results.Ok(await services.ListerAsync(statut, Pagination.Lire(page, taille)))).RequireAuthorization(policy => policy.RequireRole("Admin"));

// Fichiers privés : admin uniquement, servis en ligne (aperçu) avec le jeton.
app.MapGet("/admin/identites/{id:int}/fichier/{face}", async (int id, string face, IdentiteServices services) =>
{
    var (flux, contentType, nomFichier) = await services.OuvrirFichierAsync(id, face);
    return Results.File(flux, contentType, nomFichier, enableRangeProcessing: false);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/admin/identites/{id:int}/valider", async (int id, IdentiteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ValiderAsync(id, user.ObtenirId()))).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/admin/identites/{id:int}/refuser", async (int id, RefuserIdentiteDto dto, IdentiteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RefuserAsync(id, dto.Motif, user.ObtenirId()))).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Appartement ---
app.MapPost("/appartement/ajouter", async (AppartementServices services, AppartementDto dto, ClaimsPrincipal user) =>
{
    // Un bailleur publie toujours pour son propre compte ; seul l'admin choisit le bailleur.
    if (user.ObtenirRole() == RoleUtilisateur.Bailleur)
    {
        dto.BailleurId = user.ObtenirId();
    }
    var appartement = await services.AjouterAppartementAsync(dto);
    return Results.Created($"/appartement/{appartement.Id}", appartement);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPut("/appartement/{id:int}", async (int id, ModifierAppartementDto dto, AppartementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ModifierAsync(id, dto, user.ObtenirId(), user.ObtenirRole())))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// « Supprimer » désactive le logement (jamais d'effacement) : masqué des annonces, historique conservé, réactivable.
app.MapDelete("/appartement/{id:int}", async (int id, AppartementServices services, ClaimsPrincipal user) =>
{
    await services.SupprimerAsync(id, user.ObtenirId(), user.ObtenirRole());
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/appartement/{id:int}/reactiver", async (int id, AppartementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ReactiverAsync(id, user.ObtenirId(), user.ObtenirRole())))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// disponible=true : uniquement les logements ouverts (annonces publiques) ; sans filtre : tous (espaces admin et bailleur).
app.MapPost("/appartement/{id:int}/photos", async (int id, [FromForm] UploadPhotoForm? form, PhotoServices services, ClaimsPrincipal user) =>
{
    // Formulaire sans aucun champ : le fichier manque (400), pas d'erreur interne.
    form ??= new UploadPhotoForm();
    var photo = await services.AjouterAsync(id, form.Fichier, form.Est360, form.Libelle, user.ObtenirId(), user.ObtenirRole());
    return Results.Created(photo.Url, photo);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin")).DisableAntiforgery();

app.MapDelete("/appartement/photos/{photoId:int}", async (int photoId, PhotoServices services, ClaimsPrincipal user) =>
{
    await services.SupprimerAsync(photoId, user.ObtenirId(), user.ObtenirRole());
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// Public : les photos illustrent les annonces, visibles sans connexion. Logement supprimé ou hôte désactivé :
// 404 pour le public, servies au propriétaire et à l'admin qui présentent leur jeton (en-tête Authorization).
// taille=miniature : version 480 px (JPEG), générée et mise en cache à la première demande.
// Cache : une photo ne change jamais sous le même n° (remplacer = supprimer + ajouter) ; publique → « public, max-age=86400,
// immutable » + ETag (304 sans relire le stockage) ; protégée (annonce supprimée, hôte désactivé) → « private, no-store ».
app.MapGet("/appartement/photo/{photoId:int}", async (int photoId, string? taille, PhotoServices services, ClaimsPrincipal user, HttpContext contexte) =>
{
    var connecte = user.Identity?.IsAuthenticated == true;
    var miniature = string.Equals(taille, "miniature", StringComparison.OrdinalIgnoreCase);
    var (photo, protegee) = await services.AccesAsync(photoId, connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null);
    var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"photo-{photoId}{(miniature ? "-miniature" : "")}\"");
    if (protegee)
    {
        contexte.Response.Headers.CacheControl = "private, no-store";
    }
    else
    {
        contexte.Response.Headers.CacheControl = $"public, max-age={(int)PhotoServices.DureeCachePublic.TotalSeconds}, immutable";
        if (contexte.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } attendus && attendus.Any(e => e.Compare(etag, useStrongComparison: false)))
        {
            contexte.Response.Headers.ETag = etag.ToString();
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
    }
    var (flux, nomFichier) = await services.OuvrirAsync(photo, miniature);
    if (!new FileExtensionContentTypeProvider().TryGetContentType(nomFichier, out var contentType))
    {
        contentType = "application/octet-stream";
    }
    return Results.File(flux, contentType, entityTag: protegee ? null : etag);
});

// Longue durée : loyerMax, chambresMin, avanceMax (mois) ; dates, voyageurs et prixMax sont ignorés pour typeOffre=LongueDuree.
// Recherche publique : critères optionnels ; arrivee/depart (yyyy-MM-dd) ensemble, equipements en liste séparée par des virgules.
// nord/sud/est/ouest : bornes de la carte (les 4 ensemble). Token facultatif : le propriétaire et l'admin voient leurs logements en détail.
// bailleurId : logements d'un seul hôte (mêmes règles de visibilité). supprimes=true/false et q (n°, adresse, ville, quartier, bailleur).
// Paginé : page, taille (20 par défaut, 100 au plus, 200 avec les 4 bornes de la carte), les plus récentes d'abord.
// Public anonyme : réponse mise en cache 30 s (cache de sortie, vidé à chaque modification d'annonce).
app.MapGet("/appartement/lister", async (AppartementServices services, ClaimsPrincipal user, string? typeOffre, bool? disponible, string? ville, string? quartier,
    int? voyageurs, string? arrivee, string? depart, string? equipements, decimal? prixMax, double? nord, double? sud, double? est, double? ouest, int? bailleurId,
    decimal? loyerMax, int? chambresMin, int? avanceMax, bool? supprimes, string? q, int? page, int? taille) =>
{
    TypeOffre? filtre = null;
    if (!string.IsNullOrEmpty(typeOffre) && Enum.TryParse<TypeOffre>(typeOffre, true, out var valeur))
    {
        filtre = valeur;
    }
    var recherche = new RechercheAppartementDto
    {
        Ville = ville,
        Quartier = quartier,
        Voyageurs = voyageurs,
        Arrivee = DatesJour.Lire(arrivee, "arrivee"),
        Depart = DatesJour.Lire(depart, "depart"),
        Equipements = (equipements ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        PrixMax = prixMax,
        Nord = nord,
        Sud = sud,
        Est = est,
        Ouest = ouest,
        BailleurId = bailleurId,
        LoyerMax = loyerMax,
        ChambresMin = chambresMin,
        AvanceMax = avanceMax,
        Supprimes = supprimes,
        Q = q
    };
    var carte = nord is not null && sud is not null && est is not null && ouest is not null;
    var connecte = user.Identity?.IsAuthenticated == true;
    return Results.Ok(await services.ListerAsync(filtre, disponible, recherche,
        connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null,
        Pagination.Lire(page, taille, max: carte ? AppartementServices.TailleMaxCarte : Pagination.TailleMax)));
}).CacheOutput(CachePublic.Annonces);

// Fiche d'un logement (public, token facultatif) : 404 pour le public si l'annonce est supprimée ou l'hôte désactivé.
app.MapGet("/appartement/{id:int}", async (int id, AppartementServices services, ClaimsPrincipal user) =>
{
    var connecte = user.Identity?.IsAuthenticated == true;
    return Results.Ok(await services.ObtenirAsync(id, connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null));
});

// typeOffre facultatif (LongueDuree / SejourCourt) : villes des seules annonces de ce type.
app.MapGet("/appartement/villes", async (AppartementServices services, string? typeOffre) =>
{
    TypeOffre? filtre = null;
    if (!string.IsNullOrWhiteSpace(typeOffre))
    {
        filtre = Enum.TryParse<TypeOffre>(typeOffre.Trim(), true, out var valeur) && Enum.IsDefined(valeur) && !int.TryParse(typeOffre, out _)
            ? valeur
            : throw new InvalidOperationException("typeOffre invalide. Utilisez LongueDuree ou SejourCourt");
    }
    return Results.Ok(await services.ListerVillesAsync(filtre));
}).CacheOutput(CachePublic.Annonces);

// Public ; un token facultatif du bailleur propriétaire ou d'un admin détaille chaque période (id, motif).
app.MapGet("/appartement/{id:int}/calendrier", async (int id, string? du, string? au, AppartementServices services, ClaimsPrincipal user) =>
{
    var connecte = user.Identity?.IsAuthenticated == true;
    return Results.Ok(await services.CalendrierAsync(id, DatesJour.Lire(du, "du"), DatesJour.Lire(au, "au"),
        connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null));
});

app.MapPost("/appartement/{id:int}/indisponibilites", async (int id, IndisponibiliteDto dto, AppartementServices services, ClaimsPrincipal user) =>
{
    var periode = await services.BloquerDatesAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    return Results.Created($"/appartement/{id}/calendrier", periode);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapDelete("/appartement/indisponibilites/{id:int}", async (int id, AppartementServices services, ClaimsPrincipal user) =>
{
    await services.DebloquerDatesAsync(id, user.ObtenirId(), user.ObtenirRole());
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// --- Location (lecture seule : créée en interne via l'acceptation d'une candidature) ---
// Paginé (page, taille ≤ 100), les plus récentes d'abord ; statut (EnCours, Terminee, Resiliee), filtre=AEvaluer, q (n°, nom, adresse).
app.MapGet("/location/lister", async (LocationServices services, ClaimsPrincipal user, int? page, int? taille, string? statut, string? filtre, string? q) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille), statut, filtre, q))).RequireAuthorization();

// Loyers mensuels sans frais, après la période couverte par l'avance : gérés par l'admin, consultés par le locataire.
app.MapGet("/location/{id:int}/loyers", async (int id, LoyerServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(id, user.ObtenirId(), user.ObtenirRole())))
    .RequireAuthorization(policy => policy.RequireRole("Admin", "Locataire"));

app.MapGet("/location/{id:int}", async (int id, LocationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RechercherLocation(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// Fin de location : préavis de 3 mois (locataire ou admin), puis clôture par l'admin à la date de fin.
app.MapPost("/location/{id:int}/preavis", async (int id, DonnerPreavisDto dto, LocationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var location = await services.DonnerPreavisAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("location.preavis", "Location", id,
        $"Préavis ({location.PreavisDonnePar}) sur la location n° {id:D6} : fin prévue le {location.DateFinPrevue:dd/MM/yyyy}");
    return Results.Ok(location);
})
    .RequireAuthorization(policy => policy.RequireRole("Locataire", "Admin"));

app.MapPost("/location/{id:int}/annuler-preavis", async (int id, LocationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var location = await services.AnnulerPreavisAsync(id, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("location.annuler-preavis", "Location", id, $"Préavis annulé sur la location n° {id:D6}");
    return Results.Ok(location);
})
    .RequireAuthorization(policy => policy.RequireRole("Locataire", "Admin"));

app.MapPost("/location/{id:int}/terminer", async (int id, LocationServices services, JournalServices journal) =>
{
    var location = await services.TerminerAsync(id);
    await journal.EnregistrerAsync("location.terminer", "Location", id, $"Location n° {id:D6} de {location.LocataireNom} terminée ({location.AppartementAdresse})");
    return Results.Ok(location);
})
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Financement ---
// Le financement (création, échéancier, paiements des mensualités) est géré par l'admin ; seul le locataire concerné le consulte.
// Le bailleur s'arrête à l'acceptation du dossier : apport, remise des clés et financement relèvent de l'admin.
app.MapPost("/financement/creer", async (FinancementServices services, FinancementLocationDto dto) =>
{
    var financement = await services.CreerFinancementLocation(dto);
    return Results.Created($"/financement/{financement.Id}", financement);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// Paginé (page, taille ≤ 100), les plus récents d'abord ; statut (EnAttenteValidation, EnCours, Solde), q (n° de financement ou de location, nom, adresse).
app.MapGet("/financement/lister", async (FinancementServices services, ClaimsPrincipal user, int? page, int? taille, string? statut, string? q) =>
    Results.Ok(await services.ListerFinancementLoctions(user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille), statut, q)))
    .RequireAuthorization(policy => policy.RequireRole("Admin", "Locataire"));

app.MapPost("/financement/{id:int}/generer-echeancier", async (int id, MensualiteServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    await services.GenererEcheancierAsync(id, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("financement.echeancier", "Financement", id, $"Échéancier généré pour le financement n° {id:D6}");
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapGet("/financement/{id:int}/mensualites", async (int id, MensualiteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParFinancementAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization(policy => policy.RequireRole("Admin", "Locataire"));

// Lance immédiatement les rappels des échéances (sans attendre l'exécution planifiée) : nombre d'emails par catégorie.
app.MapPost("/rappels/executer", async (RappelsServices services, CancellationToken annulation) =>
    Results.Ok(await services.ExecuterAsync(annulation)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Paiement ---
app.MapPost("/financement/{id:int}/solder", async (int id, SolderFinancementDto dto, PaiementServices services, JournalServices journal) =>
{
    var soldees = await services.SolderFinancementAsync(id, dto);
    await journal.EnregistrerAsync("financement.solder", "Financement", id,
        $"Financement n° {id:D6} soldé : {soldees} mensualité(s) réglée(s) ({dto.ModePaiement})");
    return Results.Ok(new { mensualitesSoldees = soldees });
})
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/paiement/enregistrer", async (PaiementDto dto, PaiementServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var paiement = await services.EnregistrerPaiementAsync(dto, user.ObtenirId(), user.ObtenirRole());
    var (cible, cibleId, libelle) = dto.MensualiteId is { } m ? ("Mensualite", m, $"la mensualité n° {m}")
        : dto.LoyerId is { } l ? ("Loyer", l, $"le loyer n° {l}")
        : ("Reservation", dto.ReservationId!.Value, $"la réservation n° {dto.ReservationId:D6}");
    await journal.EnregistrerAsync("paiement.enregistrer", cible, cibleId,
        $"Paiement n° {paiement.Id:D6} de {JournalServices.Montant(paiement.Montant)} ({paiement.ModePaiement}) enregistré pour {libelle}");
    return Results.Ok(paiement);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapGet("/mensualite/{id:int}/paiements", async (int id, PaiementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParMensualiteAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization(policy => policy.RequireRole("Admin", "Locataire"));

// --- Candidature ---
app.MapPost("/candidature/creer", async (CandidatureDto dto, CandidatureServices services, ClaimsPrincipal user) =>
{
    var candidature = await services.CreerAsync(user.ObtenirId(), dto);
    return Results.Created($"/candidature/{candidature.Id}", candidature);
}).RequireAuthorization(policy => policy.RequireRole("Locataire"));

// Paginé (page, taille ≤ 100, facultatifs), les plus récents d'abord ; statut, appartementId, q (n° de dossier, nom, adresse).
app.MapGet("/candidature/lister", async (CandidatureServices services, ClaimsPrincipal user, int? page, int? taille, string? statut, int? appartementId, string? q) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille), statut, appartementId, q))).RequireAuthorization();

app.MapGet("/candidature/{id:int}", async (int id, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RechercherAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapPost("/candidature/{id:int}/accepter", async (int id, AccepterCandidatureDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.AccepterAsync(id, user.ObtenirId(), user.ObtenirRole(), dto), "candidature.accepter",
        c => $"Dossier n° {c.Id:D6} de {c.LocataireNom} accepté ({c.AppartementAdresse})"))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/apport", async (int id, EnregistrerApportDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.EnregistrerApportAsync(id, dto, user.ObtenirId(), user.ObtenirRole()), "candidature.apport",
        c => $"Apport de {JournalServices.Montant(c.ApportInitial)} ({dto.ModePaiement}) enregistré pour le dossier n° {c.Id:D6} de {c.LocataireNom}"))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/candidature/{id:int}/remise-cles", async (int id, RemiseClesDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.RemettreClesAsync(id, dto, user.ObtenirId(), user.ObtenirRole()), "candidature.remise-cles",
        c => $"Remise des clés enregistrée pour le dossier n° {c.Id:D6} de {c.LocataireNom} ({c.AppartementAdresse})"))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/candidature/{id:int}/visite", async (int id, ProgrammerVisiteDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.ProgrammerVisiteAsync(id, dto, user.ObtenirId(), user.ObtenirRole()), "candidature.visite",
        c => $"Visite programmée le {dto.DateHeure:dd/MM/yyyy HH:mm} (UTC) pour le dossier n° {c.Id:D6} de {c.LocataireNom} (présélectionné)"))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/annuler-visite", async (int id, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.AnnulerVisiteAsync(id, user.ObtenirId(), user.ObtenirRole()), "candidature.annuler-visite",
        c => $"Visite annulée pour le dossier n° {c.Id:D6} de {c.LocataireNom}"))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/visite/{id:int}/repondre", async (int id, RepondreVisiteDto dto, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RepondreVisiteAsync(id, dto, user.ObtenirId())))
    .RequireAuthorization(policy => policy.RequireRole("Locataire"));

app.MapPost("/visite/{id:int}/cloturer", async (int id, CloturerVisiteDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.CloturerVisiteAsync(id, dto, user.ObtenirId(), user.ObtenirRole()), "candidature.cloturer-visite",
        c => $"Visite n° {id} du dossier n° {c.Id:D6} de {c.LocataireNom} : {(dto.Effectuee ? "effectuée" : "candidat absent")}"))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/annuler-attribution", async (int id, RefuserCandidatureDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.AnnulerAttributionAsync(id, dto.MotifRefus, user.ObtenirId(), user.ObtenirRole()), "candidature.annuler-attribution",
        c => $"Attribution du dossier n° {c.Id:D6} de {c.LocataireNom} annulée{(string.IsNullOrWhiteSpace(dto.MotifRefus) ? "" : $" : {dto.MotifRefus}")}"))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/candidature/{id:int}/desister", async (int id, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.DesisterAsync(id, user.ObtenirId())))
    .RequireAuthorization(policy => policy.RequireRole("Locataire"));

app.MapPost("/appartement/{id:int}/clore-liste-attente", async (int id, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(new { refuses = await services.CloreListeAttenteAsync(id, user.ObtenirId(), user.ObtenirRole()) }))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/demande-complement", async (int id, DemanderComplementDto dto, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.DemanderComplementAsync(id, dto, user.ObtenirId(), user.ObtenirRole())))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/refuser", async (int id, RefuserCandidatureDto dto, CandidatureServices services, JournalServices journal, ClaimsPrincipal user) =>
    await Journaliser(journal, await services.RefuserAsync(id, user.ObtenirId(), user.ObtenirRole(), dto), "candidature.refuser",
        c => $"Dossier n° {c.Id:D6} de {c.LocataireNom} refusé{(string.IsNullOrWhiteSpace(dto.MotifRefus) ? "" : $" : {dto.MotifRefus}")}"))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// --- Document ---
app.MapPost("/candidature/{id:int}/documents", async (int id, [FromForm] UploadDocumentForm? form, DocumentServices services, ClaimsPrincipal user) =>
{
    form ??= new UploadDocumentForm();
    var document = await services.UploaderAsync(id, user.ObtenirId(), user.ObtenirRole(), form.Proprietaire, form.TypeDocument, form.Fichier);
    return Results.Created($"/document/{document.Id}", document);
}).RequireAuthorization(policy => policy.RequireRole("Locataire", "Admin")).DisableAntiforgery();

app.MapGet("/candidature/{id:int}/documents", async (int id, DocumentServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParCandidatureAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/document/{id:int}/telecharger", async (int id, DocumentServices services, ClaimsPrincipal user) =>
{
    var (flux, nomFichier) = await services.TelechargerAsync(id, user.ObtenirId(), user.ObtenirRole());
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(nomFichier, out var contentType))
    {
        contentType = "application/octet-stream";
    }
    return Results.File(flux, contentType, nomFichier);
}).RequireAuthorization();

// --- Reservation (séjour court) ---
app.MapPost("/reservation/creer", async (ReservationDto dto, ReservationServices services, ClaimsPrincipal user) =>
{
    var reservation = await services.CreerAsync(user.ObtenirId(), dto);
    return Results.Created($"/reservation/{reservation.Id}", reservation);
}).RequireAuthorization(policy => policy.RequireRole("Locataire"));

// Public : prix détaillé et disponibilité avant de réserver (même calcul que la création).
app.MapGet("/reservation/devis", async (int appartementId, string? arrivee, string? depart, int? voyageurs, ReservationServices services) =>
    Results.Ok(await services.DevisAsync(appartementId, DatesJour.Lire(arrivee, "arrivee"), DatesJour.Lire(depart, "depart"), voyageurs)));

// Paginé (page, taille ≤ 100) ; filtre (Demandes, Actives, AVenir, AEvaluer, Passees, ARembourser ; tri propre à chaque onglet),
// statut (exact), q (n° de réservation, nom du voyageur, adresse). Sans filtre : les plus récentes d'abord.
app.MapGet("/reservation/lister", async (ReservationServices services, ClaimsPrincipal user, int? page, int? taille, string? filtre, string? statut, string? q) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille), filtre, statut, q))).RequireAuthorization();

app.MapGet("/reservation/{id:int}", async (int id, ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RechercherAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// Voyageur, hôte propriétaire ou admin ; corps facultatif { motif }. Le remboursement dû est calculé selon la politique d'annulation.
app.MapPost("/reservation/{id:int}/annuler", async (int id, AnnulerReservationDto? dto, ReservationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    await services.AnnulerAsync(id, dto?.Motif, user.ObtenirId(), user.ObtenirRole());
    var annulee = await services.RechercherAsync(id, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("reservation.annuler", "Reservation", id,
        $"Réservation n° {id:D6} annulée ({annulee.AnnuleePar}){(string.IsNullOrWhiteSpace(dto?.Motif) ? "" : $" : {dto!.Motif}")}"
        + (annulee.MontantARembourser is > 0 ? $" — à rembourser : {JournalServices.Montant(annulee.MontantARembourser.Value)}" : ""));
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/reservation/{id:int}/annulation/apercu", async (int id, ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ApercuAnnulationAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapPost("/reservation/{id:int}/remboursement", async (int id, RemboursementDto dto, ReservationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var reservation = await services.EnregistrerRemboursementAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("reservation.remboursement", "Reservation", id,
        $"Remboursement de {JournalServices.Montant(dto.Montant)} ({dto.ModePaiement}) enregistré pour la réservation n° {id:D6}");
    return Results.Ok(reservation);
})
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Avis (après un séjour terminé) ---
app.MapPost("/reservation/{id:int}/avis", async (int id, CreerAvisDto dto, AvisServices services, ClaimsPrincipal user) =>
{
    var avis = await services.CreerAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    return Results.Created($"/avis/{avis.Id}", avis);
}).RequireAuthorization();

// Longue durée : locataire ou bailleur, location terminée (30 jours) ou en cours depuis 6 mois ; un avis par sens et par location.
app.MapPost("/location/{id:int}/avis", async (int id, CreerAvisDto dto, AvisServices services, ClaimsPrincipal user) =>
{
    var avis = await services.CreerPourLocationAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    return Results.Created($"/avis/{avis.Id}", avis);
}).RequireAuthorization();

app.MapPost("/avis/{id:int}/reponse", async (int id, RepondreAvisDto dto, AvisServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RepondreAsync(id, dto, user.ObtenirId()))).RequireAuthorization();

// Public, paginé (page, taille ≤ 100) ; Moyenne, Nombre et Moyennes portent sur tous les avis. Mis en cache 30 s pour le public.
// reservationId : seulement l'avis (public) de ce séjour, les moyennes restant celles du logement.
app.MapGet("/appartement/{id:int}/avis", async (int id, AvisServices services, int? page, int? taille, int? reservationId) =>
    Results.Ok(await services.ListerParAppartementAsync(id, Pagination.Lire(page, taille), reservationId))).CacheOutput(CachePublic.Annonces);

app.MapGet("/utilisateur/{id:int}/avis", async (int id, AvisServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParUtilisateurAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// Public : badges de confiance, sans email ni téléphone.
app.MapGet("/utilisateur/{id:int}/profil-public", async (int id, AvisServices services) =>
    Results.Ok(await services.ProfilPublicAsync(id)));

// --- Messagerie voyageur ↔ hôte ---
// Paginé : page 1 = les messages les plus récents (taille 30 par défaut, 100 au plus), chaque page en ordre chronologique ;
// avant={id de message} : les messages plus anciens que celui-ci (curseur stable pendant que la conversation avance).
app.MapGet("/reservation/{id:int}/messages", async (int id, MessageServices services, ClaimsPrincipal user, int? page, int? taille, int? avant) =>
    Results.Ok(await services.ListerAsync(id, user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille, defaut: 30), avant))).RequireAuthorization();

app.MapPost("/reservation/{id:int}/messages", async (int id, EnvoyerMessageDto dto, MessageServices services, ClaimsPrincipal user) =>
{
    var message = await services.EnvoyerAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    return Results.Created($"/reservation/{id}/messages", message);
}).RequireAuthorization();

// filtre facultatif (Demandes, Actives, AVenir, AEvaluer, Passees, ARembourser) : non-lus des seules réservations de cet onglet.
app.MapGet("/messages/non-lus", async (MessageServices services, ClaimsPrincipal user, string? filtre) =>
    Results.Ok(await services.NonLusAsync(user.ObtenirId(), user.ObtenirRole(), filtre))).RequireAuthorization();

// Réservation sans réservation instantanée : l'hôte (ou l'admin) accepte ou refuse la demande.
app.MapPost("/reservation/{id:int}/accepter", async (int id, ReservationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var reservation = await services.AccepterAsync(id, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("reservation.accepter", "Reservation", id, $"Demande de réservation n° {id:D6} acceptée");
    return Results.Ok(reservation);
})
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/reservation/{id:int}/refuser", async (int id, RefuserReservationDto? dto, ReservationServices services, JournalServices journal, ClaimsPrincipal user) =>
{
    var reservation = await services.RefuserAsync(id, dto?.Motif, user.ObtenirId(), user.ObtenirRole());
    await journal.EnregistrerAsync("reservation.refuser", "Reservation", id,
        $"Demande de réservation n° {id:D6} refusée{(string.IsNullOrWhiteSpace(dto?.Motif) ? "" : $" : {dto!.Motif}")}");
    return Results.Ok(reservation);
})
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapGet("/reservation/{id:int}/paiements", async (int id, ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerPaiementsAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// --- Paiement mobile (Orange Money / MTN MoMo) ---
app.MapPost("/paiement-mobile/initier", async (InitierPaiementMobileDto dto, PaiementMobileServices services, ClaimsPrincipal user) =>
{
    var transaction = await services.InitierAsync(user.ObtenirId(), dto);
    return Results.Created($"/paiement-mobile/{transaction.Id}", transaction);
}).RequireAuthorization(policy => policy.RequireRole("Locataire"));

app.MapGet("/paiement-mobile/{id:int}", async (int id, PaiementMobileServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ObtenirAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// Transaction en attente du voyageur pour cette réservation (204 s'il n'y en a pas).
app.MapGet("/reservation/{id:int}/paiement-mobile/en-cours", async (int id, PaiementMobileServices services, ClaimsPrincipal user) =>
    await services.EnCoursAsync(id, user.ObtenirId()) is { } transaction ? Results.Ok(transaction) : Results.NoContent())
    .RequireAuthorization(policy => policy.RequireRole("Locataire"));

// Anonyme, appelé par le fournisseur : authentifié par la signature HMAC-SHA256 du corps brut (en-tête X-Signature).
app.MapPost("/paiement-mobile/webhook/{operateur}", async (string operateur, HttpRequest requete, PaiementMobileServices services) =>
{
    const int TailleMaxCorps = 64 * 1024;
    if (requete.ContentLength > TailleMaxCorps)
    {
        return Results.Json(new { message = "Notification trop volumineuse" }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    using var memoire = new MemoryStream();
    await requete.Body.CopyToAsync(memoire);
    if (memoire.Length > TailleMaxCorps)
    {
        return Results.Json(new { message = "Notification trop volumineuse" }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    try
    {
        var statut = await services.TraiterWebhookAsync(operateur, memoire.ToArray(), requete.Headers["X-Signature"].FirstOrDefault());
        return Results.Ok(new { statut });
    }
    catch (SignatureInvalideException ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }
}).AllowAnonymous();

app.MapPost("/paiement-mobile/{id:int}/simuler", async (int id, SimulerPaiementMobileDto dto, PaiementMobileServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.SimulerAsync(id, dto.Succes, user.ObtenirId()))).RequireAuthorization();

// --- État des lieux photo (séjour confirmé) ---
app.MapPost("/reservation/{id:int}/etat-des-lieux", async (int id, [FromForm] UploadEtatDesLieuxForm? form, EtatDesLieuxServices services, ClaimsPrincipal user) =>
{
    form ??= new UploadEtatDesLieuxForm();
    var photo = await services.AjouterAsync(id, form.Fichier, form.Moment, form.Commentaire, user.ObtenirId());
    return Results.Created(photo.Url, photo);
}).RequireAuthorization().DisableAntiforgery();

app.MapGet("/reservation/{id:int}/etat-des-lieux", async (int id, EtatDesLieuxServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/etat-des-lieux/photo/{id:int}", async (int id, EtatDesLieuxServices services, ClaimsPrincipal user) =>
{
    var (flux, nomFichier) = await services.OuvrirAsync(id, user.ObtenirId(), user.ObtenirRole());
    if (!new FileExtensionContentTypeProvider().TryGetContentType(nomFichier, out var contentType))
    {
        contentType = "application/octet-stream";
    }
    return Results.File(flux, contentType);
}).RequireAuthorization();

app.MapDelete("/etat-des-lieux/{id:int}", async (int id, EtatDesLieuxServices services, ClaimsPrincipal user) =>
{
    await services.SupprimerAsync(id, user.ObtenirId());
    return Results.NoContent();
}).RequireAuthorization();

// --- Favoris (tout utilisateur connecté sauf l'admin) ---
app.MapPost("/favoris/{appartementId:int}", async (int appartementId, FavoriServices services, ClaimsPrincipal user) =>
{
    await services.AjouterAsync(user.ObtenirId(), appartementId);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Locataire", "Bailleur"));

app.MapDelete("/favoris/{appartementId:int}", async (int appartementId, FavoriServices services, ClaimsPrincipal user) =>
{
    await services.RetirerAsync(user.ObtenirId(), appartementId);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Locataire", "Bailleur"));

// Paginé (page, taille ≤ 100), du plus récent au plus ancien ; /favoris/ids reste la liste complète des n° (cœurs des cartes).
app.MapGet("/favoris", async (FavoriServices services, ClaimsPrincipal user, int? page, int? taille) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole(), Pagination.Lire(page, taille))))
    .RequireAuthorization(policy => policy.RequireRole("Locataire", "Bailleur"));

app.MapGet("/favoris/ids", async (FavoriServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.IdsAsync(user.ObtenirId())))
    .RequireAuthorization(policy => policy.RequireRole("Locataire", "Bailleur"));

// --- Tableau de bord de l'hôte (l'admin indique bailleurId) ---
app.MapGet("/bailleur/tableau-de-bord", async (int? annee, int? bailleurId, TableauDeBordHoteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ObtenirAsync(annee, bailleurId, user.ObtenirId(), user.ObtenirRole())))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// --- Documents PDF : locataire / voyageur, bailleur / hôte concernés et admin (409 sinon) ---
app.MapGet("/loyer/{id:int}/quittance.pdf", async (int id, DocumentsPdfServices services, ClaimsPrincipal user) =>
    Pdf(await services.QuittanceLoyerAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/reservation/{id:int}/recu.pdf", async (int id, DocumentsPdfServices services, ClaimsPrincipal user) =>
    Pdf(await services.RecuReservationAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/paiement/{id:int}/recu.pdf", async (int id, DocumentsPdfServices services, ClaimsPrincipal user) =>
    Pdf(await services.RecuPaiementAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/location/{id:int}/contrat.pdf", async (int id, DocumentsPdfServices services, ClaimsPrincipal user) =>
    Pdf(await services.ContratLocationAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/reservation/{id:int}/etat-des-lieux.pdf", async (int id, DocumentsPdfServices services, ClaimsPrincipal user) =>
    Pdf(await services.EtatDesLieuxAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapHub<MessagesHub>(MessagesHub.Chemin);

app.Run();

// Décision sur un dossier : tracée dans le journal après succès, puis renvoyée telle quelle.
static async Task<IResult> Journaliser(JournalServices journal, CandidatureResponseDto dossier, string action, Func<CandidatureResponseDto, string> details)
{
    await journal.EnregistrerAsync(action, "Candidature", dossier.Id, details(dossier));
    return Results.Ok(dossier);
}

static IResult Pdf(DocumentsPdfServices.FichierPdf fichier) => Results.File(fichier.Contenu, "application/pdf", fichier.NomFichier);

public class UploadPhotoForm
{
    public IFormFile Fichier { get; set; } = default!;
    public bool Est360 { get; set; }
    public string? Libelle { get; set; }
}

public class UploadEtatDesLieuxForm
{
    public IFormFile? Fichier { get; set; }
    /// <summary>« Arrivee » ou « Depart ».</summary>
    public string? Moment { get; set; }
    public string? Commentaire { get; set; }
}

public class UploadDocumentForm
{
    public IFormFile Fichier { get; set; } = default!;
    public string Proprietaire { get; set; } = string.Empty;
    public string TypeDocument { get; set; } = string.Empty;
}

public class UploadIdentiteForm
{
    /// <summary>« CNI », « Passeport », « PermisConduire » ou « CarteConsulaire ».</summary>
    public string? Type { get; set; }
    public IFormFile? Recto { get; set; }
    public IFormFile? Verso { get; set; }
}
