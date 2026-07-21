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
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Logging structuré (docs/Volume_9_Deployment_Operations.md §10 : "centralisé, ex. via Serilog"). JSON
// compact hors Development pour un puits centralisé (ELK/Loki/Datadog… derrière le driver de logs du
// conteneur, Volume_9 §2.1) ; console lisible en Development. Niveaux pilotés par appsettings*.json
// (section "Serilog", ReadFrom.Configuration) — mêmes clés que l'ancienne section "Logging" qu'elle
// remplace. Aucun enrichisseur de PII : les seules données identifiantes déjà journalisées par
// l'application (UserId, SchoolId, adresse IP sur les échecs d'authentification) le sont explicitement
// par le code appelant, jamais ajoutées automatiquement ici.
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();

    if (context.HostingEnvironment.IsDevelopment())
    {
        loggerConfig.WriteTo.Console();
    }
    else
    {
        loggerConfig.WriteTo.Console(new CompactJsonFormatter());
    }
});

// --- Couches applicatives (Clean Architecture — docs/Volume_2_SDS.md) ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
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

    // Matrice d'autorisation "Photoshop" — délégation Finance, au choix de CHAQUE Directeur
    // (SchoolSettings.AllowFinanceToModifyFees / AllowFinanceToDeleteFees), même mécanique que
    // CanManageGradingScale ci-dessus.
    options.AddPolicy(FinancePolicies.CanModifyFees, policy =>
        policy.Requirements.Add(new CanModifyFeesRequirement()));
    options.AddPolicy(FinancePolicies.CanDeleteFees, policy =>
        policy.Requirements.Add(new CanDeleteFeesRequirement()));

    // Resource-based (audit BOLA/IDOR abonnements) — voir SchoolResourceAuthorizationHandler.
    options.AddPolicy(SchoolResourcePolicies.CanAccessSchoolResource, policy =>
        policy.Requirements.Add(new SchoolResourceRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, CanManageGradingScaleHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanModifyFeesHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanDeleteFeesHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SchoolResourceAuthorizationHandler>();

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

    // Aligner Swagger sur le port 5000 (même que l'application)
    options.AddServer(new Microsoft.OpenApi.Models.OpenApiServer
    {
        Url = "http://localhost:5000",
        Description = "Development (port 5000)"
    });

    // Deux Queries distinctes peuvent légitimement porter le même nom de DTO (ex. SubjectGradeDto) —
    // Swashbuckle génère par défaut un schemaId à partir du seul nom de classe, sans son namespace, et
    // plante au démarrage dès que deux types différents partagent ce nom. Le nom complet lève
    // l'ambiguïté définitivement, quel que soit le nombre de doublons futurs.
    options.CustomSchemaIds(type => type.FullName);
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

// Durcissement production — login (credential stuffing distribué) et génération de bulletin PDF
// (coût CPU par appel, aucun cache). Partitionnées séparément de l'inscription publique ci-dessus :
// des seuils différents, une volumétrie légitime différente.
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
var loginWindowMinutes = builder.Configuration.GetValue("RateLimiting:Login:WindowMinutes", 5);

var reportCardPermitLimit = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:PermitLimit", 20);
var reportCardWindowMinutes = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:WindowMinutes", 1);

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

    options.AddPolicy(SensitiveEndpointRateLimiting.LoginPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromMinutes(loginWindowMinutes),
            QueueLimit = 0
        });
    });

    options.AddPolicy(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName, httpContext =>
    {
        // Partitionné par utilisateur (claim sub), pas par IP : l'endpoint exige déjà [Authorize].
        var partitionKey = httpContext.User.FindFirst("sub")?.Value ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = reportCardPermitLimit,
            Window = TimeSpan.FromMinutes(reportCardWindowMinutes),
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

// HSTS — durée longue + sous-domaines + éligible au préchargement navigateur (l'app est 100 % en
// ligne, AGENTS.md, aucun sous-domaine n'est censé être servi en clair). Effectif seulement en
// production via app.UseHsts() plus bas.
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
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

// Une ligne structurée par requête (méthode, chemin, code, durée) — jamais le corps ni les en-têtes,
// donc aucun risque de journaliser un jeton ou un mot de passe. Placé après ExceptionHandlingMiddleware
// pour capturer le code HTTP réellement renvoyé, y compris sur les requêtes en erreur.
app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    // HSTS : force HTTPS côté navigateur pour les requêtes SUIVANTES (contrairement à
    // UseHttpsRedirection, qui ne redirige que la requête courante). Désactivé en Development —
    // le profil local n'a pas de certificat de confiance publique et HSTS "collerait" au navigateur
    // au-delà de la durée de vie du process de dev.
    app.UseHsts();
}

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

try
{
    app.Run();
}
finally
{
    // Vide les sinks (ex. fichier/réseau) avant l'arrêt du process — sans cela, les dernières lignes
    // journalisées pendant l'arrêt peuvent être perdues.
    Log.CloseAndFlush();
}

// Rendu accessible aux tests fonctionnels (WebApplicationFactory<Program>).
public partial class Program;
