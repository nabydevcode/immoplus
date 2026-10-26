namespace ImmoPlus.Api.Tests.Outils;

using System.Net;
using ImmoPlus.Api.Dtos;
using Microsoft.EntityFrameworkCore;

/// <summary>Création des comptes, annonces et réservations de test, uniquement par l'API (comme le ferait le front).</summary>
public static class Donnees
{
    private static readonly SemaphoreSlim VerrouAdmin = new(1, 1);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImmoPlusFactory, Compte> Admins = new();

    public static string NouvelEmail(string prefixe) => $"{prefixe}-{Guid.NewGuid():N}"[..(prefixe.Length + 13)] + "@tests.immoplus.test";

    /// <summary>Premier admin de la base de test (inscription d'amorçage : aucun admin actif), créé une fois par instance.</summary>
    public static async Task<Compte> AdminAsync(this ImmoPlusFactory f)
    {
        await VerrouAdmin.WaitAsync();
        try
        {
            if (Admins.TryGetValue(f, out var admin))
            {
                return admin;
            }
            var email = NouvelEmail("admin");
            await f.CreateClient().PostJson("/utilisateur/inscription", new
            {
                nom = "Admin Tests", telephone = "620000000", email, motDePasse = Api.MotDePasse, role = "Admin"
            }).Verifier(HttpStatusCode.Created);
            admin = await f.ConnecterAsync(email, Api.MotDePasse);
            Admins.Add(f, admin);
            return admin;
        }
        finally
        {
            VerrouAdmin.Release();
        }
    }

    public static async Task<Compte> ConnecterAsync(this ImmoPlusFactory f, string email, string motDePasse)
    {
        var utilisateur = await f.CreateClient().PostJson("/utilisateur/connexion", new { email, motDePasse })
            .Attendre<UtilisateurResponseDto>();
        return new Compte(utilisateur.Id, utilisateur.Nom, utilisateur.Email, motDePasse, utilisateur.Role, utilisateur.Token!,
            f.CreateClient().AvecJeton(utilisateur.Token!));
    }

    /// <summary>Inscription (compte en attente), confirmation de l'email par le lien reçu, validation par l'admin, puis connexion.</summary>
    public static async Task<Compte> CompteAsync(this ImmoPlusFactory f, string role, string? nom = null)
    {
        var admin = await f.AdminAsync();
        var email = NouvelEmail(role.ToLowerInvariant());
        var cree = await f.CreateClient().PostJson("/utilisateur/inscription", new
        {
            nom = nom ?? $"{role} {email[..12]}", telephone = "621000000", email, motDePasse = Api.MotDePasse, role
        }).Attendre<UtilisateurResponseDto>(HttpStatusCode.Created);
        await f.CreateClient().PostJson("/utilisateur/confirmer-email", new { jeton = await f.JetonConfirmationAsync(email) })
            .Verifier(HttpStatusCode.OK);
        await admin.Client.PostJson($"/utilisateur/{cree.Id}/valider").Verifier(HttpStatusCode.OK);
        return await f.ConnecterAsync(email, Api.MotDePasse);
    }

    /// <summary>
    /// Jeton du dernier email « Confirmez votre adresse email » mis en file pour cette adresse (lien {Frontend}/confirmer-email?jeton=…).
    /// Lu dans la boîte d'envoi : fonctionne aussi quand le SMTP des tests est coupé ou en panne.
    /// </summary>
    public static async Task<string> JetonConfirmationAsync(this ImmoPlusFactory f, string email)
    {
        var contenu = await f.LireBaseAsync(db => db.MessagesSortants.AsNoTracking()
            .Where(m => m.Destinataire == email && m.Evenement == "ConfirmationEmail")
            .OrderByDescending(m => m.Id).Select(m => m.Contenu).FirstAsync());
        return System.Text.RegularExpressions.Regex.Match(contenu, "/confirmer-email\\?jeton=([0-9a-f]{64})").Groups[1].Value;
    }

    public static Task<Compte> BailleurAsync(this ImmoPlusFactory f) => f.CompteAsync("Bailleur");
    public static Task<Compte> LocataireAsync(this ImmoPlusFactory f) => f.CompteAsync("Locataire");

    /// <summary>Annonce de séjour court publiée par le bailleur ; les champs passés remplacent les valeurs par défaut.</summary>
    public static async Task<AppartementResponseDto> SejourCourtAsync(this Compte bailleur, decimal prixParNuit = 100_000m,
        decimal? fraisMenage = 20_000m, decimal? caution = 50_000m, int? reductionSemainePct = null, int? reductionMoisPct = null,
        int? nuitsMin = null, int? capacite = 4, bool instantanee = true, string politique = "Moderee", int dureeMaxMois = 2)
    {
        return await bailleur.Client.PostJson("/appartement/ajouter", new
        {
            adresse = "12 rue des Tests",
            description = "Logement de test",
            typeOffre = "SejourCourt",
            ville = "Conakry",
            quartier = "Kipé",
            capacite,
            chambres = 2,
            lits = 2,
            sallesDeBain = 1,
            equipements = new[] { "wifi", "climatisation" },
            prixParNuit,
            dureeMaxMois,
            fraisMenage,
            caution,
            reductionSemainePct,
            reductionMoisPct,
            nuitsMin,
            reservationInstantanee = instantanee,
            politiqueAnnulation = politique
        }).Attendre<AppartementResponseDto>(HttpStatusCode.Created);
    }

    public static async Task<AppartementResponseDto> LongueDureeAsync(this Compte bailleur, decimal loyerMensuel = 1_000_000m, int dureeAvance = 6)
    {
        return await bailleur.Client.PostJson("/appartement/ajouter", new
        {
            adresse = "34 avenue des Tests",
            description = "Location longue durée de test",
            typeOffre = "LongueDuree",
            ville = "Conakry",
            quartier = "Ratoma",
            loyerMensuel,
            dureeAvanceExigee = dureeAvance
        }).Attendre<AppartementResponseDto>(HttpStatusCode.Created);
    }

    public static Task<HttpResponseMessage> Reserver(this Compte voyageur, int appartementId, int arrivee, int nuits, int voyageurs = 1) =>
        voyageur.Client.PostJson("/reservation/creer", new
        {
            appartementId,
            dateArrivee = Api.Jour(arrivee),
            dateDepart = Api.Jour(arrivee + nuits),
            nombreVoyageurs = voyageurs
        });

    public static Task<ReservationResponseDto> ReserverOk(this Compte voyageur, int appartementId, int arrivee, int nuits, int voyageurs = 1) =>
        voyageur.Reserver(appartementId, arrivee, nuits, voyageurs).Attendre<ReservationResponseDto>(HttpStatusCode.Created);
}
