// Gialora.Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Gialora.Client;
using Gialora.Client.Auth;
using Gialora.Client.Localization;
using Gialora.Client.Services;
using Microsoft.AspNetCore.Components.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API-ի հասցեն՝ "ApiBaseUrl"։ Dev-ում այն գալիս է wwwroot/appsettings.Development.json-ից
// (localhost:5001), production-ում՝ appsettings.json-ից։ Դատարկ արժեքը նշանակում է
// "API-ն նույն origin-ի վրա է" (reverse proxy) — երբեք չենք ընկնում localhost-ի վրա,
// որ publish-ված հավելվածը օգտատիրոջ browser-ից չդիմի իր իսկ մեքենային։
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
    Console.WriteLine($"Gialora: ApiBaseUrl is not configured, using the app's own origin ({apiBaseUrl}).");
}
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/"; // առանց վերջի "/"-ի BaseAddress-ը կուտեր path-ի վերջին հատվածը

// ՊԱՐՏԱԴԻՐ singleton։ IHttpClientFactory-ն handler-ների շղթան կառուցում է իր
// սեփական scope-ում, ուստի scoped token-ի պահոցը handler-ում ՈՒՐԻՇ օրինակ կլիներ,
// քան UI-ում, և գրանցվելուց հետո Authorization header-ը երբեք չէր ուղարկվի։
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<AuthHeaderHandler>();
builder.Services.AddScoped<LanguageHeaderHandler>();

builder.Services.AddHttpClient("GialoraApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
    .AddHttpMessageHandler<AuthHeaderHandler>()
    .AddHttpMessageHandler<LanguageHeaderHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GialoraApi"));

builder.Services.AddScoped<TokenAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<TokenAuthStateProvider>());
builder.Services.AddAuthorizationCore();

// Typed API clients։ Էջերը երբեք ուղիղ HttpClient չեն դիպչում — URL-ները,
// JSON-ի կարգավորումները և սխալի թարգմանությունը մեկ շերտում են։
builder.Services.AddSingleton<PendingVerification>();
builder.Services.AddScoped<AccountApi>();
builder.Services.AddScoped<RecipeApi>();
builder.Services.AddScoped<PlannerApi>();
builder.Services.AddScoped<ContentApi>();
builder.Services.AddScoped<AdminApi>();
builder.Services.AddScoped<ChatApi>();
builder.Services.AddScoped<ActivityTracker>();

// UI-ի լեզուն (en / hy / el)։ Singleton — բոլոր բաղադրիչները նույն բառարանն են տեսնում։
// API-ի հասցեն պետք է admin-ի խմբագրած տեքստերը (api/site-texts) բեռնելու համար։
builder.Services.AddSingleton(sp => new Localizer(
    sp.GetRequiredService<IWebAssemblyHostEnvironment>(),
    sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
    new Uri(apiBaseUrl)));

var host = builder.Build();

// Բառարանը բեռնում ենք առաջին render-ից ԱՌԱՋ, որ էջը անգլերենով չթարթի
await host.Services.GetRequiredService<Localizer>().InitializeAsync();

await host.RunAsync();
