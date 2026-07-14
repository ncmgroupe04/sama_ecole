using System.Text;
using System.Text.Json.Serialization;
using SamaEcole.Application;
using SamaEcole.Application.Auth;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Seed;
using SamaEcole.Web.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Couches applicatives (Clean Architecture — docs/Volume_2_SDS.md) ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);

// Paramètres d'authentification (verrouillage, durée du refresh token) — ticket JGK-A04.
builder.Services.AddSingleton(
    builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings());

// --- Authentification JWT (AGENTS.md — Décision D-07) ---
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sans ceci, ASP.NET renomme `sub` en ClaimTypes.NameIdentifier : CurrentUserService, qui lit
        // le claim brut `sub`, renverrait alors toujours null (ticket JGK-A04).
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateLifetime = true, // un token expiré -> 401 (critère du ticket JGK-A04)
            ClockSkew = TimeSpan.FromSeconds(30),

            // Les claims sont émis sous leurs noms bruts par JwtTokenGenerator.
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

builder.Services
    .AddControllersWithViews() // API + vues Razor (Views/), voir docs/BACKLOG_TICKETS.md
    .AddJsonOptions(options =>
    {
        // Les énumérations circulent en CHAÎNES, pas en entiers : openapi.yaml les déclare ainsi
        // (`status: { type: string, enum: [Active, Suspended, Blocked] }`, de même pour Role).
        // Par défaut System.Text.Json sérialise un enum en nombre et REFUSE une chaîne en entrée —
        // le contrat d'API n'était donc pas respecté, ni en lecture ni en écriture.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
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

    // Comptes de démonstration. Jamais en Production : le mot de passe est public (DbSeeder).
    // Sans eux, une base fraîchement migrée n'a AUCUN utilisateur — et comme aucun endpoint de
    // création de compte n'existe encore (JGK-B01), l'écran de connexion serait infranchissable.
    //
    // Semer exige le rôle PROPRIÉTAIRE : `users` est sous RLS, et le rôle applicatif n'y accède que
    // par les fonctions du chemin de login (migration AddAuthentication). Sans chaîne « Migrations »
    // configurée, on ne sème pas — c'est le cas des tests fonctionnels, qui sèment leur propre jeu.
    var ownerConnectionString = builder.Configuration.GetConnectionString("Migrations");

    if (!string.IsNullOrWhiteSpace(ownerConnectionString))
    {
        using var scope = app.Services.CreateScope();

        await DbSeeder.SeedAsync(
            ownerConnectionString,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
    }
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
