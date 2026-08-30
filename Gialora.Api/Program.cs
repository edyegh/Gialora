using Gialora.Application.Services;
using Gialora.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string is not configured.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("JWT signing key is not configured.");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // ← Swagger-ի համար պետք է
builder.Services.AddSwaggerGen();              // ← Swagger generator
builder.Services.AddDbContext<GialoraDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IAuthService, AuthService>();    

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();       // ← generates /swagger/v1/swagger.json
    app.UseSwaggerUI();     // ← UI-ն /swagger հասցեով
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();