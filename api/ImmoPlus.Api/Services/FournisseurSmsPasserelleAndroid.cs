namespace ImmoPlus.Api.Services;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImmoPlus.Api.Models.Enums;

/// <summary>
/// Vrais SMS envoyés par un téléphone Android avec l'application « SMS Gateway for Android » (capcom6, open source),
/// en mode « serveur local » : l'API appelle le téléphone sur le Wi-Fi, qui envoie le SMS avec sa propre carte SIM.
/// Réglages Sms:Passerelle : Url (ex. http://192.168.1.20:8080), Utilisateur, MotDePasse, Chemin (défaut /message).
/// La passerelle n'envoie que des SMS : le canal WhatsApp éventuellement configuré part donc en SMS.
/// </summary>
public class FournisseurSmsPasserelleAndroid : IFournisseurSms
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public FournisseurSmsPasserelleAndroid(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public async Task<string> EnvoyerAsync(string telephone, string texte, CanalMessage canal)
    {
        var url = _configuration["Sms:Passerelle:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Passerelle SMS non configurée (Sms:Passerelle:Url)");
        }
        var chemin = _configuration["Sms:Passerelle:Chemin"] ?? "/message";
        var requete = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/" + chemin.TrimStart('/'))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { textMessage = new { text = texte }, phoneNumbers = new[] { telephone } }),
                Encoding.UTF8, "application/json")
        };
        var identifiants = $"{_configuration["Sms:Passerelle:Utilisateur"]}:{_configuration["Sms:Passerelle:MotDePasse"]}";
        requete.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(identifiants)));

        using var reponse = await _http.SendAsync(requete);
        var corps = await reponse.Content.ReadAsStringAsync();
        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Passerelle SMS : réponse {(int)reponse.StatusCode} {corps[..Math.Min(corps.Length, 200)]}");
        }
        // Réponse attendue : { "id": "...", "state": "Pending", ... } ; l'identifiant sert de référence fournisseur.
        try
        {
            using var json = JsonDocument.Parse(corps);
            if (json.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Corps inattendu mais envoi accepté : référence générique.
        }
        return $"PASS-{Guid.NewGuid():N}"[..20];
    }
}
