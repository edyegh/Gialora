using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gialora.Api.Middleware;
using Gialora.Api.Services;
using Gialora.Api.Services.Email;
using Gialora.Api.Services.Telegram;
using Gialora.Application.Common;
using Gialora.Application.Planning;
using Gialora.Application.Seed;
using Gialora.Application.Services;
using Gialora.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
// Production-ում բացակայությունը startup-ի սխալ է — localhost-ի fallback-ը լուռ
// կկոտրեր deploy արված client-ը (CORS-ի սխալ միայն browser-ի console-ում)։
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment()
        ? new[] { "https://localhost:7280", "http://localhost:5056" }
        : throw new InvalidOperationException("Cors:AllowedOrigins is not configured."));

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
    options.UseSqlServer(connectionString, sql =>
    {
        // Կարճ ցանցային խզումները (managed SQL-ի failover) 500-ի չվերածվեն։ Սա
        // ձեռքով BeginTransaction-ը արգելում է — ատոմարությունը մեկ SaveChanges-ով ենք անում։
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        // Մի քանի collection Include-ը մեկ query-ում → cartesian explosion (հազարավոր տողեր)
        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    }));

builder.Services.AddMemoryCache();

// Load balancer / orchestrator-ի համար. /health — DB-ն հասանելի՞ է
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// Reverse proxy-ի (nginx, IIS, App Service) հետևում RemoteIpAddress-ը proxy-ինն է —
// rate limiter-ը բոլոր user-ներին մեկ bucket-ում կդներ։ Լռելյայն վստահում ենք միայն
// loopback-ին (նույն մեքենայի proxy), այլ proxy-ները՝ "ForwardedHeaders:KnownProxies"։
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

// Կատալոգի բովանդակության լեզուն (Accept-Language / ?lang=) — Application-ը տեսնում է
// միայն IContentLanguage-ը, ոչ թե HttpContext-ը
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContentLanguage, RequestContentLanguage>();

// Application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
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

// Աջակցության chat → Telegram։ Կարգավորումը՝ "Telegram" section (BotToken-ը ԳԱՂՏՆԻ է՝
// user-secrets / environment variable)։ Առանց կարգավորման chat-ը աշխատում է, բայց
// հաղորդագրությունները չեն հասնում admin-ին (log-ում warning)։
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.Section));
builder.Services.AddHttpClient<TelegramBotClient>(client =>
    {
        // getUpdates-ը մինչև 50 վ սպասում է (long polling) — լռելյայն 100 վ-ը բավական է,
        // բայց սահմանը բացահայտ ենք դնում
        client.Timeout = TimeSpan.FromSeconds(75);
    })
    // URL-ը պարունակում է bot token-ը — HttpClient-ի request log-երը այն կգրեին
    .RemoveAllLoggers()
    // Polling service-ը singleton է և client-ը պահում է ամբողջ կյանքում — DNS-ի
    // փոփոխությունները տեսնելու համար connection-ները պարբերաբար թարմացնում ենք
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<IChatNotifier, TelegramChatNotifier>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddHostedService<TelegramPollingService>();

// Email-ի հաստատման կոդերը։ Կարգավորումը՝ "Smtp" section (Password-ը ԳԱՂՏՆԻ է՝
// user-secrets / environment variable)։ Առանց SMTP-ի ոչ ոք չի կարող գրանցվել, ուստի
// production-ում դա startup-ի սխալ է, իսկ Development-ում email-ը գնում է log։
var smtpOptions = builder.Configuration.GetSection(SmtpOptions.Section).Get<SmtpOptions>() ?? new SmtpOptions();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
if (smtpOptions.IsConfigured)
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
else if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
else
    throw new InvalidOperationException("SMTP is not configured (Smtp:Host / Smtp:FromAddress) — verification emails cannot be sent.");

// Admin-ի վիճակագրությունը (էջի դիտումներ, click-եր, հավանած ռեցեպտներ)
builder.Services.AddScoped<IActivityService, ActivityService>();

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

        // Ջնջված/role-ը փոխված user-ի token-ը չպիտի աշխատի մինչև expiry
        options.Events = new JwtBearerEvents { OnTokenValidated = ActiveUserValidator.OnTokenValidated };
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

    // Chat-ը բաց է հյուրերի համար — առանց սահմանի մեկը կարող էր admin-ի Telegram-ը
    // հազարավոր հաղորդագրություններով լցնել
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Վիճակագրությունը նույնպես բաց է հյուրերի համար։ Client-ը batch է ուղարկում
    // մոտ 5 վ-ը մեկ, ուստի 30/րոպեն ազնիվ օգտատիրոջը չի խանգարում, բայց բազան
    // աղբով լցնելը դժվարացնում է
    options.AddPolicy("activity", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
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

// Rate limiter-ից և HTTPS redirect-ից առաջ — որ երկուսն էլ իրական client-ի IP/scheme-ը տեսնեն
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    // API-ն միայն JSON է տալիս — browser-ը չպիտի "գուշակի" այլ content type
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next();
});

app.UseHttpsRedirection();
app.UseCors("AllowBlazorClient");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
