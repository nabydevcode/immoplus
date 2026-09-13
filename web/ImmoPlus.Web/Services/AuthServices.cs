namespace ImmoPlus.Web.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Blazored.LocalStorage;

public class AuthServices
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _localStorage;

    public event Action? OnChange;

    public bool EstConnecte { get; private set; }
    public int? UtilisateurId { get; private set; }
    public string? NomUtilisateur { get; private set; }
    public string? RoleUtilisateur { get; private set; }

    public AuthServices(HttpClient http, ILocalStorageService localStorage)
    {
        _http = http;
        _localStorage = localStorage;
    }

    public async Task<bool> ConnexionAsync(string email, string motDePasse)
    {
        var reponse = await _http.PostAsJsonAsync("utilisateur/connexion", new
        {
            Email = email,
            MotDePasse = motDePasse
        });

        if (!reponse.IsSuccessStatusCode)
            return false;

        var resultat = await reponse.Content.ReadFromJsonAsync<ReponseConnexion>();
        if (resultat?.Token is null)
            return false;

        await _localStorage.SetItemAsync("authToken", resultat.Token);
        await _localStorage.SetItemAsync("authId", resultat.Id);
        await _localStorage.SetItemAsync("authNom", resultat.Nom ?? "");
        await _localStorage.SetItemAsync("authRole", resultat.Role ?? "");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", resultat.Token);

        EstConnecte = true;
        UtilisateurId = resultat.Id;
        NomUtilisateur = resultat.Nom;
        RoleUtilisateur = resultat.Role;
        OnChange?.Invoke();

        return true;
    }

    public async Task DeconnexionAsync()
    {
        await _localStorage.RemoveItemAsync("authToken");
        await _localStorage.RemoveItemAsync("authId");
        await _localStorage.RemoveItemAsync("authNom");
        await _localStorage.RemoveItemAsync("authRole");
        _http.DefaultRequestHeaders.Authorization = null;

        EstConnecte = false;
        UtilisateurId = null;
        NomUtilisateur = null;
        RoleUtilisateur = null;
        OnChange?.Invoke();
    }

    public async Task<bool> ChargerTokenAsync()
    {
        var token = await _localStorage.GetItemAsync<string>("authToken");
        if (string.IsNullOrEmpty(token))
        {
            EstConnecte = false;
            return false;
        }

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        EstConnecte = true;
        UtilisateurId = await _localStorage.GetItemAsync<int?>("authId");
        NomUtilisateur = await _localStorage.GetItemAsync<string>("authNom");
        RoleUtilisateur = await _localStorage.GetItemAsync<string>("authRole");
        OnChange?.Invoke();

        return true;
    }

    private class ReponseConnexion
    {
        public int Id { get; set; }
        public string? Token { get; set; }
        public string? Nom { get; set; }
        public string? Role { get; set; }
    }
}
