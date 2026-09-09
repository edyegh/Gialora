// Gialora.Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Gialora.Client;
using Gialora.Client.Auth;
using Gialora.Client.Services;
using Microsoft.AspNetCore.Components.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API-ի հասցեն hardcode արված չէ — override արա wwwroot/appsettings.json-ից ("ApiBaseUrl")
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:5001/";
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/"; // առանց վերջի "/"-ի BaseAddress-ը կուտեր path-ի վերջին հատվածը

// ՊԱՐՏԱԴԻՐ singleton։ IHttpClientFactory-ն handler-ների շղթան կառուցում է իր
// սեփական scope-ում, ուստի scoped token-ի պահոցը handler-ում ՈՒՐԻՇ օրինակ կլիներ,
// քան UI-ում, և գրանցվելուց հետո Authorization header-ը երբեք չէր ուղարկվի։
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<AuthHeaderHandler>();

builder.Services.AddHttpClient("GialoraApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
    .AddHttpMessageHandler<AuthHeaderHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi"));

builder.Services.AddScoped<TokenAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<TokenAuthStateProvider>());
builder.Services.AddAuthorizationCore();

// Typed API clients։ Էջերը երբեք ուղիղ HttpClient չեն դիպչում — URL-ները,
// JSON-ի կարգավորումները և սխալի թարգմանությունը մեկ շերտում են։
builder.Services.AddScoped<AccountApi>();
builder.Services.AddScoped<RecipeApi>();
builder.Services.AddScoped<PlannerApi>();
builder.Services.AddScoped<ContentApi>();
builder.Services.AddScoped<AdminApi>();

await builder.Build().RunAsync();
