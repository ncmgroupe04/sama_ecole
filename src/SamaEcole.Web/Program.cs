using System.Text;
using SamaEcole.Application;
using SamaEcole.Infrastructure;
using SamaEcole.Persistence;
using SamaEcole.Web.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Couches applicatives (Clean Architecture — docs/Volume_2_SDS.md) ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddPersistence(builder.Configuration);

// --- Authentification JWT (AGENTS.md — Décision D-07) ---
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews(); // API + vues Razor (Views/), voir docs/BACKLOG_TICKETS.md
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Sama Ecole API", Version = "v1" });
});

var app = builder.Build();

// Refuse de démarrer si l'application se connecte à PostgreSQL avec un rôle qui contourne la RLS
// (superutilisateur, BYPASSRLS, ou propriétaire des tables) : l'isolation multi-tenant serait
// silencieusement inopérante (ticket JGK-A03, AGENTS.md règle #2).
await app.Services.EnsureRuntimeRoleCannotBypassRlsAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Traduit toute exception applicative en réponse HTTP normalisée
// (docs/Volume_4_API_Design.md §0.4) — jamais une exception brute renvoyée au client.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles(); // sert wwwroot/css/site.css compilé depuis Tailwind (Décision D-13)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // Vues Razor — voir docs/BACKLOG_TICKETS.md

app.Run();

// Rendu accessible aux tests fonctionnels (WebApplicationFactory<Program>).
public partial class Program;
