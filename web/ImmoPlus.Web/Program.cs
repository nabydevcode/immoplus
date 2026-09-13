using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ImmoPlus.Web;
using ImmoPlus.Web.Services;
using Blazored.LocalStorage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Développement local : API sur http://localhost:5107 (voir api/ImmoPlus.Api/Properties/launchSettings.json).
// Production : API déployée sur le VPS. À remplacer par une URL de domaine HTTPS quand un nom de domaine sera configuré.
var urlApi = builder.HostEnvironment.IsProduction()
    ? "http://168.231.77.68:5050/"
    : "http://localhost:5107/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(urlApi) });
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<AuthServices>();

await builder.Build().RunAsync();
