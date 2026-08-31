using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using SamaEcole.Application;
using SamaEcole.Application.Auth;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Persistence;
using SamaEcole.Persistence.Seed;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Configuration;
using SamaEcole.Web.HealthChecks;
using SamaEcole.Web.Middleware;
using SamaEcole.Web.RateLimiting;
using SamaEcole.Application.Schools.Commands.RevertToTest;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
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

// --- Bac à sable / mode réel : drapeau « le retour au mode test est-il autorisé ici ? » ---
// true en Development, OU si SAMA_RETOUR_MODE_TEST_AUTORISE=true (posé sur les seuls environnements
// jetables — dev, recette, staging). false partout ailleurs, donc en vraie production : le passage
// en mode réel y est DÉFINITIF et l'endpoint /schools/current/dev/revert-to-test n'est pas monté.
// Volontairement pas conditionné à ASPNETCORE_ENVIRONMENT : la recette tourne avec l'image de prod.
var revertToTestEnabled = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("SAMA_RETOUR_MODE_TEST_AUTORISE");
builder.Services.AddSingleton<ISandboxModeProvider>(new SandboxModeProvider(revertToTestEnabled));

// --- Authentification JWT (AGENTS.md — Décision D-07) ---
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSigningKey = jwtSection["SigningKey"];

// Garde de démarrage — même logique que RlsGuard (rôle PostgreSQL) et EmailSenderGuard (SMTP) : sans
// elle, une clé absente, faible, ou restée au sentinel de développement ne fait PAS échouer le
// démarrage (contrairement à RlsGuard/EmailSenderGuard) — l'application démarre "avec succès" mais émet
// des tokens signés avec une clé triviale ou publique (visible dans appsettings.Development.json /
// .env.example), sans qu'aucun avertissement ne le signale avant la première connexion.
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey est manquant (voir .env.example — Jwt__SigningKey). L'application refuse de " +
        "démarrer plutôt que d'échouer plus tard, au premier login.");
}

if (!builder.Environment.IsDevelopment()
    && (jwtSigningKey is "DEV_ONLY_INSECURE_KEY_CHANGE_ME_MINIMUM_32_CHARS" or "REMPLACER_PAR_UNE_CLE_ALEATOIRE_256_BITS"))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey utilise encore une clé de développement/placeholder hors Development. Générez " +
        "une clé aléatoire dédiée (256 bits minimum, jamais réutilisée entre environnements) avant de " +
        "déployer — voir .env.example.");
}

if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey fait moins de 256 bits (32 octets) : trop faible pour HMAC-SHA256 (Volume_7_Security.md §6). " +
        "Voir .env.example.");
}

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
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

// Contrôle d'accès par formule ([RequireFeature]) — les politiques « Feature:… » ne sont PAS
// déclarées une à une ci-dessus : FeaturePolicyProvider les reconstruit à la volée depuis le nom
// (patron officiel des politiques paramétrées), et délègue tout le reste au fournisseur par défaut.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, FeaturePolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, FeatureAuthorizationHandler>();

// Donne au refus « hors formule » le format d'erreur normalisé (code FEATURE_NOT_IN_PLAN) au lieu
// d'un 403 au corps vide, pour que l'interface puisse proposer la montée en gamme.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, FeatureAuthorizationResultHandler>();

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
    options.SwaggerDoc("v1", new() { Title = "Unikol API", Version = "v1" });

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

var passwordResetPermitLimit = builder.Configuration.GetValue("RateLimiting:PasswordReset:PermitLimit", 5);
var passwordResetWindowMinutes = builder.Configuration.GetValue("RateLimiting:PasswordReset:WindowMinutes", 15);

// Annuaire public (B2C) : volontairement large — feuilleter des pages de résultats est l'usage normal
// de cet écran, et une limite basse casserait la navigation d'un visiteur légitime avant de gêner un
// moissonneur. Ce plafond borne le débit, il ne prétend pas empêcher la copie d'un annuaire public.
var publicDirectoryPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicDirectory:PermitLimit", 120);
var publicDirectoryWindowMinutes = builder.Configuration.GetValue("RateLimiting:PublicDirectory:WindowMinutes", 1);
var reportCardPermitLimit = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:PermitLimit", 20);
var reportCardWindowMinutes = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:WindowMinutes", 1);

// Webhooks entrants (paiement, accusés SMS) : plafond anti-flood large sur deux endpoints publics
// non authentifiés. La signature HMAC reste la garde réelle ; ceci borne juste le volume.
var webhookPermitLimit = builder.Configuration.GetValue("RateLimiting:Webhooks:PermitLimit", 120);
var webhookWindowMinutes = builder.Configuration.GetValue("RateLimiting:Webhooks:WindowMinutes", 1);

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

    options.AddPolicy(SensitiveEndpointRateLimiting.PublicDirectoryPolicyName, httpContext =>
    {
        // Par IP : l'annuaire est anonyme, il n'y a aucun utilisateur sur qui partitionner.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = publicDirectoryPermitLimit,
            Window = TimeSpan.FromMinutes(publicDirectoryWindowMinutes),
            QueueLimit = 0
        });
    });

    options.AddPolicy(SensitiveEndpointRateLimiting.PasswordResetPolicyName, httpContext =>
    {
        // Par IP : ces routes sont anonymes, il n'y a aucun utilisateur sur qui partitionner.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = passwordResetPermitLimit,
            Window = TimeSpan.FromMinutes(passwordResetWindowMinutes),
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

    options.AddPolicy(SensitiveEndpointRateLimiting.WebhookInboundPolicyName, httpContext =>
    {
        // Par IP : l'émetteur est un service tiers sans identité JWT. Derrière le proxy, l'IP réelle
        // n'est vue que si ForwardedHeaders est actif (voir plus haut) — sinon tous les webhooks
        // partagent la partition du proxy, ce qui reste un plafond acceptable pour ce trafic.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = webhookPermitLimit,
            Window = TimeSpan.FromMinutes(webhookWindowMinutes),
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

// Derrière le reverse proxy qui termine TLS (Nginx, docs/Volume_9_Deployment_Operations.md §2) :
// sans ceci, HttpContext.Connection.RemoteIpAddress vaut l'IP DU PROXY pour toutes les requêtes — les
// limiteurs de débit partitionnés par IP (login/credential stuffing, inscription publique de masse,
// réinitialisation de mot de passe) s'effondrent alors sur une partition unique — et Request.Scheme
// reste « http », ce qui fait boucler UseHttpsRedirection.
//
// Opt-in par configuration : ForwardedHeaders:Enabled=true en Staging/Production (voir .env.example),
// absent en Development qui n'a pas de proxy.
//
// KnownProxies / KnownNetworks vidés : on fait confiance à l'en-tête X-Forwarded-* de n'importe quel
// émetteur. C'est le mode recommandé par Microsoft quand l'application tourne DERRIÈRE un proxy dans
// un réseau clos et que son port n'est JAMAIS exposé directement (équivalent de
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true). Si le port applicatif peut être atteint autrement que
// par le proxy, renseigner plutôt ForwardedHeaders:KnownProxies (IP du/des proxys) et retirer le
// Clear() — sinon un client pourrait usurper son IP source et contourner les limiteurs par IP.
var forwardedHeadersEnabled = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;

        var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        if (knownProxies.Length > 0)
        {
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in knownProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }
        }
        else
        {
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
    });
}

// Sondes de santé (docs/Volume_9_Deployment_Operations.md §2, §4 étape 10, §9) : le load balancer et
// le rolling update en ont besoin pour ne router du trafic que vers une instance réellement prête.
// "/health/live" = process vivant (sans dépendance) ; "/health/ready" = PostgreSQL joignable.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// Refuse de démarrer si l'application se connecte à PostgreSQL avec un rôle qui contourne la RLS
// (superutilisateur, BYPASSRLS, ou propriétaire des tables) : l'isolation multi-tenant serait
// silencieusement inopérante (ticket JGK-A03, AGENTS.md règle #2).
await app.Services.EnsureRuntimeRoleCannotBypassRlsAsync();

// Canaux sortants non configurés : AVERTIT sans empêcher le démarrage (voir SmsServiceGuard pour la
// justification de cette différence avec RlsGuard/EmailSenderGuard). Emis ici, et non dans
// AddInfrastructure, parce que c'est le premier endroit où un ILogger existe — sans cet appel, les
// gardes ne seraient qu'un texte que personne n'affiche jamais.
foreach (var warning in new[]
         {
             SmsServiceGuard.DescribeMisconfiguration(
                 app.Services.GetRequiredService<IOptions<SmsOptions>>().Value,
                 app.Environment.IsDevelopment()),
             SmsServiceGuard.DescribeWhatsAppMisconfiguration(
                 app.Services.GetRequiredService<IOptions<WhatsAppOptions>>().Value,
                 app.Environment.IsDevelopment())
         })
{
    if (warning is not null)
    {
        app.Logger.LogWarning("{Warning}", warning);
    }
}

// AVANT tout : réécrit RemoteIpAddress / Request.Scheme depuis les en-têtes X-Forwarded-* du proxy,
// pour que les limiteurs de débit par IP et la détection HTTPS voient la VRAIE requête cliente et non
// le proxy. Opt-in (ForwardedHeaders:Enabled) — inerte en Development.
if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

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
    var ownerConnectionString = builder.Configuration["DATABASE_OWNER_CONNECTION_STRING"] 
    ?? builder.Configuration.GetConnectionString("Migrations") 
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
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
    app.UseHttpsRedirection();
}
// La table MIME par défaut d'ASP.NET Core ne connaît pas .mjs (module ES) : sans cette extension, les
// GET sur wwwroot/js/vendor/pdf*.min.mjs renverraient 404 avant même d'atteindre le navigateur — le
// moteur d'aperçu PDF (pdf-preview.js) échouerait à charger PDF.js pour TOUS les documents.
var staticFileContentTypes = new FileExtensionContentTypeProvider();
staticFileContentTypes.Mappings[".mjs"] = "text/javascript";
app.UseStaticFiles(new StaticFileOptions // sert wwwroot/css/site.css compilé depuis Tailwind (Décision D-13)
{
    ContentTypeProvider = staticFileContentTypes,
    OnPrepareResponse = ctx =>
    {
        // asp-append-version="true" (posé sur tout <link>/<script> CSS/JS de _Layout.cshtml et
        // _AuthLayout.cshtml) fait varier l'URL (`?v=<hash de contenu>`) à chaque changement de
        // fichier : un cache immuable d'un an n'y est donc jamais dangereux, l'URL change d'elle-même
        // au prochain déploiement. Sans ce paramètre `v` (favicon, manifest.json, images non
        // versionnées), la même immuabilité rendrait une mise à jour invisible pendant un an — ces
        // requêtes reçoivent une politique bien plus courte.
        var headers = ctx.Context.Response.GetTypedHeaders();
        headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v")
            ? new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(365), Extensions = { new NameValueHeaderValue("immutable") } }
            : new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(1) };
    }
});
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

// Porte de SORTIE « repasser en mode test » — montée UNIQUEMENT sur les environnements jetables
// (revertToTestEnabled, voir plus haut). En vraie production, la route n'existe pas : une requête
// directe reçoit un 404 du routeur, avant tout code métier. Le Handler la revérifie malgré tout
// (double garde). Minimal API plutôt qu'une action de contrôleur : c'est justement pour que le
// mapping soit CONDITIONNEL, ce qu'un [HttpPost] sur un contrôleur ne permet pas proprement.
if (revertToTestEnabled)
{
    app.MapPost("/api/v1/schools/current/dev/revert-to-test",
            async (ISender mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new RevertToTestCommand(), cancellationToken)))
        .RequireAuthorization(policy => policy.RequireRole(nameof(Role.Directeur)));
}

// Sondes de santé, publiques et hors /api/ (donc jamais filtrées par SubscriptionAwaitingPaymentMiddleware,
// jamais soumises à un limiteur de débit) — corps minimal, aucune donnée sensible.
//   /health/live  — vivacité : le process répond. Aucune dépendance testée : une base momentanément
//                   injoignable ne doit pas provoquer un redémarrage en boucle.
//   /health/ready — préparation : PostgreSQL joignable. C'est la sonde du rolling update / load balancer.
//   /health       — agrégat de tous les contrôles.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

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
