using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using SamaEcole.Application;
using SamaEcole.Application.Auth;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Seed;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Middleware;
using SamaEcole.Web.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
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
// Ticket JGK-G02 — délégation de la notation au Secrétariat, au choix de CHAQUE Directeur
// (SchoolSettings.AllowSecretaryToManageGrading), plutôt qu'un rôle codé en dur dans l'attribut.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(GradingPolicies.CanManageGradingScale, policy =>
        policy.Requirements.Add(new CanManageGradingScaleRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, CanManageGradingScaleHandler>();

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

// Limitation de débit du formulaire PUBLIC d'inscription (ticket JGK-I01, docs/Volume_7_Security.md
// §Paiements). Partitionné par IP : un flot d'inscriptions depuis une même adresse est freiné sans
// pénaliser les autres. Fenêtre fixe simple — le but est de casser l'automatisation de masse, pas de
// lisser finement le trafic. Limite configurable (l'environnement de test la relève pour ne pas
// faire trébucher des tests qui soumettent plusieurs demandes légitimes d'affilée).
var registrationPermitLimit = builder.Configuration.GetValue("RateLimiting:Registration:PermitLimit", 5);
var registrationWindowMinutes = builder.Configuration.GetValue("RateLimiting:Registration:WindowMinutes", 5);

// Ticket JGK-I02 — suivi public par référence. Plafond distinct, plus généreux : consulter l'état de
// son propre dossier peut légitimement se faire plusieurs fois (rafraîchissement manuel).
var registrationStatusPermitLimit = builder.Configuration.GetValue("RateLimiting:RegistrationStatus:PermitLimit", 20);
var registrationStatusWindowMinutes = builder.Configuration.GetValue("RateLimiting:RegistrationStatus:WindowMinutes", 5);

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(RegistrationRateLimiting.PolicyName, httpContext =>
    {
        // Clé de partition = IP source. Absente (proxy mal configuré, test) -> une partition commune
        // « unknown » : mieux vaut regrouper prudemment que de laisser passer sans limite.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = registrationPermitLimit,
            Window = TimeSpan.FromMinutes(registrationWindowMinutes),
            QueueLimit = 0
        });
    });

    options.AddPolicy(RegistrationRateLimiting.StatusPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = registrationStatusPermitLimit,
            Window = TimeSpan.FromMinutes(registrationStatusWindowMinutes),
            QueueLimit = 0
        });
    });

    // Réponse de dépassement au format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4) — jamais
    // la page 429 brute d'ASP.NET (AGENTS.md règle #9).
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        var payload = new
        {
            code = "RATE_LIMITED",
            message = "Trop de demandes envoyées depuis cette adresse. Réessayez dans quelques minutes.",
            details = (object?)null,
            traceId = context.HttpContext.TraceIdentifier
        };

        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(payload), cancellationToken);
    };
});

var app = builder.Build();

// Refuse de démarrer si l'application se connecte à PostgreSQL avec un rôle qui contourne la RLS
// (superutilisateur, BYPASSRLS, ou propriétaire des tables) : l'isolation multi-tenant serait
// silencieusement inopérante (ticket JGK-A03, AGENTS.md règle #2).
await app.Services.EnsureRuntimeRoleCannotBypassRlsAsync();

// Ticket JGK-F01 — en-têtes de sécurité (nosniff, X-Frame-Options, CSP…) sur TOUTES les réponses, y
// compris Swagger UI ci-dessous. Posé EN TOUT PREMIER dans le pipeline : UseSwaggerUI et
// UseStaticFiles COURT-CIRCUITENT la requête pour leurs propres routes (ils ne rappellent jamais
// next()) — un middleware ajouté après eux n'aurait donc jamais vu passer ces réponses.
app.UseMiddleware<SecurityHeadersMiddleware>();

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

// Ticket JGK-I04 — après UseAuthentication (il lui faut context.User déjà résolu pour lire le claim
// schoolId), avant UseAuthorization/MapControllers (le blocage doit précéder toute logique métier).
app.UseMiddleware<SubscriptionAwaitingPaymentMiddleware>();

app.UseAuthorization();

// Après l'authentification : le endpoint (donc sa politique [EnableRateLimiting]) est déjà résolu, et
// la limite du formulaire public s'applique avant que le contrôleur ne soit invoqué.
app.UseRateLimiter();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // Vues Razor — voir docs/BACKLOG_TICKETS.md

app.Run();

// Rendu accessible aux tests fonctionnels (WebApplicationFactory<Program>).
public partial class Program;
