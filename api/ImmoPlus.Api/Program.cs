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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<UtilisateurServices>();
builder.Services.AddScoped<AppartementServices>();
builder.Services.AddScoped<TokenServices>();
builder.Services.AddScoped<EmailServices>();
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
// Sauvegarde de la base (VACUUM INTO) au démarrage puis toutes les 24 h ; désactivable via Sauvegardes:Actif.
builder.Services.AddSingleton<SauvegardeServices>();
builder.Services.AddHostedService<SauvegardeBaseService>();
// Horodatages UTC sérialisés avec « Z » (dates « jour » à minuit inchangées), en HTTP comme dans le hub SignalR.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new ConvertisseurDateUtc()));
// Compteurs d'essais de connexion / mot de passe oublié, en mémoire (une seule instance de l'API).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LimiteurTentatives>();
// Rappels des échéances (mensualités, loyers) au démarrage puis toutes les 24 h ; désactivable via Rappels:Actif.
builder.Services.AddHostedService<RappelsHostedService>();

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
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new ConvertisseurDateUtc()));

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

var app = builder.Build();

// Applique automatiquement les migrations en attente au démarrage (évite une étape manuelle à chaque déploiement).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Derrière nginx (même machine), l'adresse du client arrive dans X-Forwarded-For : seule la boucle locale est crue,
// pour que la limitation des connexions par IP vise le vrai client sans être contournable par un en-tête forgé.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });

app.UseMiddleware<MiddlewareException>();

// Photos et documents sont resservis tels quels : le navigateur ne doit jamais « deviner » un autre type (ex. HTML déguisé en image).
app.Use(async (contexte, suivant) =>
{
    contexte.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await suivant();
});

// Swagger exposé en permanence : sert de documentation vivante pour le futur client mobile.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AutoriserClients");
app.UseAuthentication();
// Un jeton reste valide jusqu'à son expiration : on vérifie donc à chaque requête que le compte est toujours actif
// (désactivé par un admin → 401 immédiat, sans attendre l'expiration du jeton).
app.Use(async (contexte, suivant) =>
{
    if (contexte.User.Identity?.IsAuthenticated == true)
    {
        var id = contexte.User.ObtenirId();
        var statut = await contexte.RequestServices.GetRequiredService<AppDbContext>().Utilisateurs
            .Where(u => u.Id == id).Select(u => (StatutUtilisateur?)u.Statut).FirstOrDefaultAsync();
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

// --- Utilisateur ---
app.MapPost("/utilisateur/inscription", async (InscriptionDto dto, UtilisateurServices services) =>
{
    var utilisateur = await services.InscrireAsync(dto);
    return Results.Created($"/utilisateur/{utilisateur.Id}", utilisateur);
});

// 5 échecs par email ou 20 par adresse IP en 15 min → 429 pendant 15 min.
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

// Filtres facultatifs : role (Locataire/Bailleur/Admin), statut (EnAttente/Valide/Refuse/Desactive), q (nom, email ou téléphone). 100 par page au plus.
app.MapGet("/utilisateur/lister", async (UtilisateurServices services, int? page, int? taille, string? role, string? statut, string? q) =>
    Results.Ok(await services.ListerAsync(page ?? 1, taille ?? 20, role, statut, q)))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

// Seul un admin crée un admin (l'inscription publique refuse ce rôle dès qu'un admin actif existe).
app.MapPost("/utilisateur/admin", async (CreerAdminDto dto, UtilisateurServices services) =>
{
    var admin = await services.CreerAdminAsync(dto);
    return Results.Created($"/utilisateur/{admin.Id}", admin);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

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

// --- Sauvegardes de la base (admin) ---
app.MapGet("/admin/sauvegardes", (SauvegardeServices services) =>
    Results.Ok(services.Lister())).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/admin/sauvegardes", async (SauvegardeServices services, JournalServices journal, CancellationToken annulation) =>
{
    var sauvegarde = await services.CreerAsync(annulation);
    await journal.EnregistrerAsync("sauvegarde.creer", "Sauvegarde", null, $"Sauvegarde manuelle {sauvegarde.Nom} ({sauvegarde.Taille / 1024} Ko)");
    return Results.Created($"/admin/sauvegardes/{sauvegarde.Nom}", sauvegarde);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// Nom strictement contrôlé (immoplus-yyyyMMdd-HHmm.db) : aucun autre fichier du serveur ne peut être téléchargé.
app.MapGet("/admin/sauvegardes/{nom}", async (string nom, SauvegardeServices services, JournalServices journal) =>
{
    var chemin = services.Chemin(nom);
    await journal.EnregistrerAsync("sauvegarde.telecharger", "Sauvegarde", null, $"Téléchargement de la sauvegarde {nom}");
    return Results.File(chemin, "application/vnd.sqlite3", nom);
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
app.MapGet("/admin/identites", async (IdentiteServices services, string? statut) =>
    Results.Ok(await services.ListerAsync(statut))).RequireAuthorization(policy => policy.RequireRole("Admin"));

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
app.MapGet("/appartement/photo/{photoId:int}", async (int photoId, string? taille, PhotoServices services, ClaimsPrincipal user) =>
{
    var connecte = user.Identity?.IsAuthenticated == true;
    var miniature = string.Equals(taille, "miniature", StringComparison.OrdinalIgnoreCase);
    var (flux, nomFichier) = await services.OuvrirAsync(photoId, connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null, miniature);
    if (!new FileExtensionContentTypeProvider().TryGetContentType(nomFichier, out var contentType))
    {
        contentType = "application/octet-stream";
    }
    return Results.File(flux, contentType);
});

// Longue durée : loyerMax, chambresMin, avanceMax (mois) ; dates, voyageurs et prixMax sont ignorés pour typeOffre=LongueDuree.
// Recherche publique : critères optionnels ; arrivee/depart (yyyy-MM-dd) ensemble, equipements en liste séparée par des virgules.
// nord/sud/est/ouest : bornes de la carte (les 4 ensemble). Token facultatif : le propriétaire et l'admin voient leurs logements en détail.
// bailleurId : logements d'un seul hôte (mêmes règles de visibilité).
app.MapGet("/appartement/lister", async (AppartementServices services, ClaimsPrincipal user, string? typeOffre, bool? disponible, string? ville, string? quartier,
    int? voyageurs, string? arrivee, string? depart, string? equipements, decimal? prixMax, double? nord, double? sud, double? est, double? ouest, int? bailleurId,
    decimal? loyerMax, int? chambresMin, int? avanceMax) =>
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
        AvanceMax = avanceMax
    };
    var connecte = user.Identity?.IsAuthenticated == true;
    return Results.Ok(await services.ListerAsync(filtre, disponible, recherche,
        connecte ? user.ObtenirId() : null, connecte ? user.ObtenirRole() : null));
});

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
});

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
app.MapGet("/location/lister", async (LocationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

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

app.MapGet("/financement/lister", async (FinancementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerFinancementLoctions(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization(policy => policy.RequireRole("Admin", "Locataire"));

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

app.MapGet("/candidature/lister", async (CandidatureServices services, ClaimsPrincipal user, int page, int taille) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole(), page, taille))).RequireAuthorization();

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

app.MapGet("/reservation/lister", async (ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

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

app.MapGet("/appartement/{id:int}/avis", async (int id, AvisServices services) =>
    Results.Ok(await services.ListerParAppartementAsync(id)));

app.MapGet("/utilisateur/{id:int}/avis", async (int id, AvisServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParUtilisateurAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// Public : badges de confiance, sans email ni téléphone.
app.MapGet("/utilisateur/{id:int}/profil-public", async (int id, AvisServices services) =>
    Results.Ok(await services.ProfilPublicAsync(id)));

// --- Messagerie voyageur ↔ hôte ---
app.MapGet("/reservation/{id:int}/messages", async (int id, MessageServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapPost("/reservation/{id:int}/messages", async (int id, EnvoyerMessageDto dto, MessageServices services, ClaimsPrincipal user) =>
{
    var message = await services.EnvoyerAsync(id, dto, user.ObtenirId(), user.ObtenirRole());
    return Results.Created($"/reservation/{id}/messages", message);
}).RequireAuthorization();

app.MapGet("/messages/non-lus", async (MessageServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.NonLusAsync(user.ObtenirId()))).RequireAuthorization();

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

app.MapGet("/favoris", async (FavoriServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole())))
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
