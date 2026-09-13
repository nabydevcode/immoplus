using Microsoft.EntityFrameworkCore;
using ImmoPlus.Api.Data;
using ImmoPlus.Api.Services;
using ImmoPlus.Api.Dtos;
using ImmoPlus.Api.Middleware;
using ImmoPlus.Api.Models.Enums;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<UtilisateurServices>();
builder.Services.AddScoped<AppartementServices>();
builder.Services.AddScoped<TokenServices>();
builder.Services.AddScoped<LocationServices>();
builder.Services.AddScoped<FinancementServices>();
builder.Services.AddScoped<MensualiteServices>();
builder.Services.AddScoped<PaiementServices>();
builder.Services.AddScoped<CandidatureServices>();
builder.Services.AddScoped<DocumentServices>();
builder.Services.AddScoped<ReservationServices>();

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
});

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

app.UseMiddleware<MiddlewareException>();

// Swagger exposé en permanence : sert de documentation vivante pour le futur client mobile.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AutoriserClients");
app.UseAuthentication();
app.UseAuthorization();

// --- Utilisateur ---
app.MapPost("/utilisateur/inscription", async (InscriptionDto dto, UtilisateurServices services) =>
{
    var utilisateur = await services.InscrireAsync(dto);
    return Results.Created($"/utilisateur/{utilisateur.Id}", utilisateur);
});

app.MapPost("/utilisateur/connexion", async (ConnexionDto dto, UtilisateurServices services) =>
{
    return Results.Ok(await services.ConnexionAsync(dto));
});

app.MapGet("/utilisateur/lister", async (UtilisateurServices services, int page, int taille) =>
{
    return Results.Ok(await services.ListerAsync(page, taille));
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

// --- Appartement ---
app.MapPost("/appartement/ajouter", async (AppartementServices services, AppartementDto dto) =>
{
    var appartement = await services.AjouterAppartementAsync(dto);
    return Results.Created($"/appartement/{appartement.Id}", appartement);
}).RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapGet("/appartement/lister", async (AppartementServices services, string? typeOffre) =>
{
    TypeOffre? filtre = null;
    if (!string.IsNullOrEmpty(typeOffre) && Enum.TryParse<TypeOffre>(typeOffre, true, out var valeur))
    {
        filtre = valeur;
    }
    return Results.Ok(await services.ListerAsync(filtre));
});

// --- Location (lecture seule : créée en interne via l'acceptation d'une candidature) ---
app.MapGet("/location/lister", async (LocationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/location/{id:int}", async (int id, LocationServices services) =>
    Results.Ok(await services.RechercherLocation(id))).RequireAuthorization();

// --- Financement ---
app.MapPost("/financement/creer", async (FinancementServices services, FinancementLocationDto dto) =>
{
    var financement = await services.CreerFinancementLocation(dto);
    return Results.Created($"/financement/{financement.Id}", financement);
}).RequireAuthorization();

app.MapGet("/financement/lister", async (FinancementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerFinancementLoctions(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapPost("/financement/{id:int}/generer-echeancier", async (int id, MensualiteServices services, ClaimsPrincipal user) =>
{
    await services.GenererEcheancierAsync(id, user.ObtenirId(), user.ObtenirRole());
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/financement/{id:int}/mensualites", async (int id, MensualiteServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParFinancementAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

// --- Paiement ---
app.MapPost("/paiement/enregistrer", async (PaiementDto dto, PaiementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.EnregistrerPaiementAsync(dto, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/mensualite/{id:int}/paiements", async (int id, PaiementServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerParMensualiteAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

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

app.MapPost("/candidature/{id:int}/accepter", async (int id, AccepterCandidatureDto dto, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.AccepterAsync(id, user.ObtenirId(), user.ObtenirRole(), dto)))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

app.MapPost("/candidature/{id:int}/refuser", async (int id, RefuserCandidatureDto dto, CandidatureServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RefuserAsync(id, user.ObtenirId(), user.ObtenirRole(), dto)))
    .RequireAuthorization(policy => policy.RequireRole("Bailleur", "Admin"));

// --- Document ---
app.MapPost("/candidature/{id:int}/documents", async (int id, [FromForm] UploadDocumentForm form, DocumentServices services, ClaimsPrincipal user) =>
{
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

app.MapGet("/reservation/lister", async (ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.ListerAsync(user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapGet("/reservation/{id:int}", async (int id, ReservationServices services, ClaimsPrincipal user) =>
    Results.Ok(await services.RechercherAsync(id, user.ObtenirId(), user.ObtenirRole()))).RequireAuthorization();

app.MapPost("/reservation/{id:int}/annuler", async (int id, ReservationServices services, ClaimsPrincipal user) =>
{
    await services.AnnulerAsync(id, user.ObtenirId(), user.ObtenirRole());
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

public class UploadDocumentForm
{
    public IFormFile Fichier { get; set; } = default!;
    public string Proprietaire { get; set; } = string.Empty;
    public string TypeDocument { get; set; } = string.Empty;
}
