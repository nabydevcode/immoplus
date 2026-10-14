namespace ImmoPlus.Api.Tests.Outils;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImmoPlus.Api.Dtos;

/// <summary>Compte de test connecté : son client HTTP porte déjà le jeton.</summary>
public sealed record Compte(int Id, string Nom, string Email, string MotDePasse, string Role, string Jeton, HttpClient Client);

/// <summary>Raccourcis HTTP communs aux tests (JSON « web » comme l'API : camelCase).</summary>
public static class Api
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string MotDePasse = "MotDePasse!2026";

    public static DateTime Aujourdhui => DateTime.UtcNow.Date;

    /// <summary>Date « jour » au format attendu par l'API (yyyy-MM-dd), à n jours d'aujourd'hui (UTC).</summary>
    public static string Jour(int decalage) => Aujourdhui.AddDays(decalage).ToString("yyyy-MM-dd");

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? corps = null) =>
        client.PostAsJsonAsync(url, corps ?? new { }, Json);

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object corps) =>
        client.PutAsJsonAsync(url, corps, Json);

    public static async Task<T> Lire<T>(this HttpResponseMessage reponse)
    {
        var texte = await reponse.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(texte, Json)
            ?? throw new InvalidOperationException($"Réponse vide ({(int)reponse.StatusCode}) : {texte}");
    }

    /// <summary>Vérifie le code HTTP (avec le corps de la réponse dans le message d'échec) puis lit le JSON.</summary>
    public static async Task<T> Attendre<T>(this Task<HttpResponseMessage> requete, HttpStatusCode attendu = HttpStatusCode.OK)
    {
        var reponse = await requete;
        await reponse.Verifier(attendu);
        return await reponse.Lire<T>();
    }

    public static async Task<HttpResponseMessage> Verifier(this HttpResponseMessage reponse, HttpStatusCode attendu)
    {
        if (reponse.StatusCode != attendu)
        {
            var corps = await reponse.Content.ReadAsStringAsync();
            Assert.Fail($"{reponse.RequestMessage?.Method} {reponse.RequestMessage?.RequestUri?.PathAndQuery} : attendu {(int)attendu}, reçu {(int)reponse.StatusCode} — {corps}");
        }
        return reponse;
    }

    public static async Task<HttpResponseMessage> Verifier(this Task<HttpResponseMessage> requete, HttpStatusCode attendu) =>
        await (await requete).Verifier(attendu);

    /// <summary>Code attendu et message d'erreur métier { message } renvoyé par l'API.</summary>
    public static async Task<string> Erreur(this Task<HttpResponseMessage> requete, HttpStatusCode attendu)
    {
        var reponse = await (await requete).Verifier(attendu);
        var texte = await reponse.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(texte))
        {
            return "";
        }
        using var doc = JsonDocument.Parse(texte);
        return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? "" : texte;
    }

    public static HttpClient AvecJeton(this HttpClient client, string jeton)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return client;
    }

    public static MultipartFormDataContent Formulaire(string nomFichier, byte[] contenu, params (string Nom, string Valeur)[] champs)
    {
        var formulaire = new MultipartFormDataContent();
        var fichier = new ByteArrayContent(contenu);
        fichier.Headers.ContentType = new MediaTypeHeaderValue(nomFichier.EndsWith(".pdf") ? "application/pdf" : "image/png");
        formulaire.Add(fichier, "fichier", nomFichier);
        foreach (var (nom, valeur) in champs)
        {
            formulaire.Add(new StringContent(valeur), nom);
        }
        return formulaire;
    }

    /// <summary>Vraie image PNG 1×1 (décodable par une bibliothèque d'images, pas seulement l'en-tête).</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Count 0/Kids[]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>Paiement d'une réservation enregistré par l'admin (ou l'hôte) : daté d'aujourd'hui.</summary>
    public static Task<HttpResponseMessage> PayerReservation(this HttpClient client, int reservationId, decimal montant, string? date = null) =>
        client.PostJson("/paiement/enregistrer", new
        {
            reservationId,
            montant,
            datePaiement = date ?? Jour(0),
            modePaiement = "Espèces",
            reference = "TEST"
        });

    public static Task<ReservationResponseDto> Reservation(this HttpClient client, int id) =>
        client.GetAsync($"/reservation/{id}").Attendre<ReservationResponseDto>();
}
