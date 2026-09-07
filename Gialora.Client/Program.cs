// Gialora.Client/Program.cs
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Gialora.Client;
using Gialora.Client.Auth;
using Microsoft.AspNetCore.Components.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API-ի հասցեն hardcode արված չէ — override արա wwwroot/appsettings.json-ից ("ApiBaseUrl")
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:5001/";
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/"; // առանց վերջի "/"-ի BaseAddress-ը կուտեր path-ի վերջին հատվածը

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

await builder.Build().RunAsync();
