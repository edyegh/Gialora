using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gialora.Api.Middleware;
using Gialora.Api.Services;
using Gialora.Application.Planning;
using Gialora.Application.Seed;
using Gialora.Application.Services;
using Gialora.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string is not configured.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("JWT signing key is not configured.");

// HMAC-SHA256-ը պահանջում է առնվազն 256-bit (32 բայթ) բանալի — ավելի կարճը
// runtime-ին է ձախողում, ոչ թե startup-ին։ Բռնում ենք հիմա։
if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
    throw new InvalidOperationException("JWT signing key must be at least 32 bytes (256 bits) long.");

// Client-ի origin-ները (Gialora.Client/Properties/launchSettings.json)։
// Override՝ appsettings-ի "Cors:AllowedOrigins" զանգվածով։
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:7280", "http://localhost:5056" };

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enum-երը JSON-ում անունով են գնում, ոչ թե թվով։ Առանց սրա client-ի և
        // API-ի enum-ների ցանկացած վերադասավորում լուռ կփչացներ պահված տվյալները։
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

builder.Services.AddDbContext<GialoraDbContext>(options =>
    options.UseSqlServer(connectionString));

// Application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IFamilyService, FamilyService>();
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IIngredientService, IngredientService>();
builder.Services.AddScoped<IMealPlanService, MealPlanService>();
builder.Services.AddScoped<IShoppingListService, ShoppingListService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IFavoriteService, FavoriteService>();
builder.Services.AddScoped<IBlogService, BlogService>();
builder.Services.AddScoped<IRecipeImportService, RecipeImportService>();
builder.Services.AddScoped<DbSeeder>();

// Engine-ը state չունի — singleton-ը բավական է
builder.Services.AddSingleton<IMealPlanningEngine, MealPlanningEngine>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Չենք ուզում claim-ների անունների "ավտոմատ" ձևափոխում —
        // token-ում ինչ գրել ենք, նույնն էլ կարդում ենք
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.Zero,
            // Առանց սրանց User.Identity.Name-ը դատարկ է, իսկ [Authorize(Roles = "Admin")]-ը՝ միշտ 403
            NameClaimType = JwtTokenGenerator.NameClaimType,
            RoleClaimType = JwtTokenGenerator.RoleClaimType
        };
    });

builder.Services.AddAuthorization();

// Login/register-ի brute-force պաշտպանությունը account lockout-ից բացի նաև IP-ի մակարդակում։
// Առանց սրա attacker-ը կարող է շատ տարբեր email-եր փորձել առանց որևէ սահմանափակման։
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database: migrate + seed
// ---------------------------------------------------------------------------

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GialoraDbContext>();
    await db.Database.MigrateAsync();

    // Seed-ը դատարկ բազան դարձնում է աշխատունակ (բաղադրիչներ + ռեցեպտներ),
    // առանց դրանց meal planner-ը ոչինչ չէր կարող առաջարկել։
    if (builder.Configuration.GetValue("Seed:Enabled", app.Environment.IsDevelopment()))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(
            builder.Configuration["Seed:AdminEmail"] ?? "admin@gialora.local",
            builder.Configuration["Seed:AdminPassword"] ?? "ChangeMe123!");
    }
}

// EF-ը model-ը և ամեն առանձին LINQ query-ն կոմպիլացնում է ԱՌԱՋԻՆ կատարման պահին։
// Առանց warm-up-ի այդ գինը վճարում է առաջին այցելուն. չափված՝ 925 ms
// /api/mealplans/current-ի վրա, հետագա կանչերը՝ 46 ms։ Այստեղ մեկ անգամ
// "անվճար" կատարում ենք ամենածանր query-ները՝ background-ում, որ startup-ը չկանգնի։
_ = Task.Run(async () =>
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GialoraDbContext>();
        var warmupDate = DateOnly.FromDateTime(DateTime.UtcNow);

        await db.Recipes.AsNoTracking()
            .Where(r => r.IsPublished)
            .Select(r => new { r.Id, Tags = r.RecipeTags.Select(t => t.Tag.Name).ToList() })
            .Take(1).ToListAsync();

        await db.MealPlans.AsNoTracking()
            .Include(p => p.Days).ThenInclude(d => d.Entries).ThenInclude(e => e.Recipe)
            .Where(p => p.Days.Any(d => d.Date >= warmupDate))
            .Take(1).ToListAsync();

        await db.ShoppingLists.AsNoTracking()
            .Include(l => l.Items).ThenInclude(i => i.Ingredient)
            .Take(1).ToListAsync();

        await db.BlogPosts.AsNoTracking().Where(p => p.IsPublished).Take(1).ToListAsync();
    }
    catch (Exception ex)
    {
        // Warm-up-ը օպտիմիզացիա է, ոչ թե պահանջ — ձախողումը չպիտի տապալի հավելվածը
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup")
            .LogWarning(ex, "Query warm-up failed; the first request will be slower.");
    }
});

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------

// Exception handler-ը ամենաառաջինն է — որ ամեն ներքևի սխալ բռնվի
app.UseGialoraExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorClient");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
