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
using SamaEcole.Web.Filters;
using SamaEcole.Web.HealthChecks;
using SamaEcole.Web.Middleware;
using SamaEcole.Web.RateLimiting;
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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddSingleton(
    builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings());

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSigningKey = jwtSection["SigningKey"];

if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey est manquant (voir .env.example — Jwt__SigningKey).");
}

if (!builder.Environment.IsDevelopment()
    && (jwtSigningKey is "DEV_ONLY_INSECURE_KEY_CHANGE_ME_MINIMUM_32_CHARS" or "REMPLACER_PAR_UNE_CLE_ALEATOIRE_256_BITS"))
{
    throw new InvalidOperationException("Jwt:SigningKey utilise une clé de développement hors Development.");
}

if (Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey fait moins de 256 bits.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(GradingPolicies.CanManageGradingScale, policy =>
        policy.Requirements.Add(new CanManageGradingScaleRequirement()));
    options.AddPolicy(FinancePolicies.CanModifyFees, policy =>
        policy.Requirements.Add(new CanModifyFeesRequirement()));
    options.AddPolicy(FinancePolicies.CanDeleteFees, policy =>
        policy.Requirements.Add(new CanDeleteFeesRequirement()));
    options.AddPolicy(FinancePolicies.CanCancelPayments, policy =>
        policy.Requirements.Add(new CanCancelPaymentsRequirement()));
    options.AddPolicy(FinancePolicies.CanCancelPayslips, policy =>
        policy.Requirements.Add(new CanCancelPayslipsRequirement()));
    options.AddPolicy(FinancePolicies.CanViewMonthlyStatus, policy =>
        policy.Requirements.Add(new CanViewMonthlyStatusRequirement()));
    options.AddPolicy(SchoolResourcePolicies.CanAccessSchoolResource, policy =>
        policy.Requirements.Add(new SchoolResourceRequirement()));
});

builder.Services.AddScoped<IAuthorizationHandler, CanManageGradingScaleHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanModifyFeesHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanDeleteFeesHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanCancelPaymentsHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanCancelPayslipsHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CanViewMonthlyStatusHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SchoolResourceAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, FeaturePolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, FeatureAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ModuleAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, FeatureAuthorizationResultHandler>();

builder.Services
    .AddControllersWithViews(options =>
    {
        options.Filters.Add<EmptyFileResultGuardFilter>();
        options.Filters.Add<PdfPreviewDispositionFilter>();
        options.Filters.Add<PageModuleFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Unikol API", Version = "v1" });
    options.AddServer(new Microsoft.OpenApi.Models.OpenApiServer
    {
        Url = "http://localhost:5000",
        Description = "Development (port 5000)"
    });
    options.CustomSchemaIds(type => type.FullName);
});

var registrationPermitLimit = builder.Configuration.GetValue("RateLimiting:Registration:PermitLimit", 5);
var registrationWindowMinutes = builder.Configuration.GetValue("RateLimiting:Registration:WindowMinutes", 5);
var registrationStatusPermitLimit = builder.Configuration.GetValue("RateLimiting:RegistrationStatus:PermitLimit", 20);
var registrationStatusWindowMinutes = builder.Configuration.GetValue("RateLimiting:RegistrationStatus:WindowMinutes", 5);
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:Login:PermitLimit", 10);
var loginWindowMinutes = builder.Configuration.GetValue("RateLimiting:Login:WindowMinutes", 5);
var passwordResetPermitLimit = builder.Configuration.GetValue("RateLimiting:PasswordReset:PermitLimit", 5);
var passwordResetWindowMinutes = builder.Configuration.GetValue("RateLimiting:PasswordReset:WindowMinutes", 15);
var publicDirectoryPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicDirectory:PermitLimit", 120);
var publicDirectoryWindowMinutes = builder.Configuration.GetValue("RateLimiting:PublicDirectory:WindowMinutes", 1);
var reportCardPermitLimit = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:PermitLimit", 20);
var reportCardWindowMinutes = builder.Configuration.GetValue("RateLimiting:ReportCardGeneration:WindowMinutes", 1);
var webhookPermitLimit = builder.Configuration.GetValue("RateLimiting:Webhooks:PermitLimit", 120);
var webhookWindowMinutes = builder.Configuration.GetValue("RateLimiting:Webhooks:WindowMinutes", 1);

builder.Services.Configure<SamaEcole.Web.Security.LegalOptions>(
    builder.Configuration.GetSection(SamaEcole.Web.Security.LegalOptions.SectionName));
builder.Services.Configure<SamaEcole.Web.Security.TurnstileOptions>(
    builder.Configuration.GetSection(SamaEcole.Web.Security.TurnstileOptions.SectionName));
builder.Services.AddHttpClient<SamaEcole.Web.Security.ITurnstileVerifier, SamaEcole.Web.Security.CloudflareTurnstileVerifier>(
    client => client.Timeout = TimeSpan.FromSeconds(5));

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(RegistrationRateLimiting.PolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = registrationPermitLimit, Window = TimeSpan.FromMinutes(registrationWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(RegistrationRateLimiting.StatusPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = registrationStatusPermitLimit, Window = TimeSpan.FromMinutes(registrationStatusWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.LoginPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = loginPermitLimit, Window = TimeSpan.FromMinutes(loginWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.PublicDirectoryPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = publicDirectoryPermitLimit, Window = TimeSpan.FromMinutes(publicDirectoryWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.PasswordResetPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = passwordResetPermitLimit, Window = TimeSpan.FromMinutes(passwordResetWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName, httpContext =>
    {
        var partitionKey = httpContext.User.FindFirst("sub")?.Value ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = reportCardPermitLimit, Window = TimeSpan.FromMinutes(reportCardWindowMinutes), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.SchoolDataExportPolicyName, httpContext =>
    {
        var partitionKey = httpContext.User.FindFirst("sub")?.Value ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 3, Window = TimeSpan.FromHours(1), QueueLimit = 0 });
    });
    options.AddPolicy(SensitiveEndpointRateLimiting.WebhookInboundPolicyName, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = webhookPermitLimit, Window = TimeSpan.FromMinutes(webhookWindowMinutes), QueueLimit = 0 });
    });

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

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

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
                    options.KnownProxies.Add(ip);
            }
        }
        else
        {
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        }
    });
}

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        var url = $"https://{host[4..]}{ctx.Request.Path}{ctx.Request.QueryString}";
        ctx.Response.Redirect(url, permanent: true);
        return;
    }
    await next();
});

// Canaux sortants non configurés : AVERTIT sans empêcher le démarrage (voir SmsServiceGuard pour la
// justification de cette différence avec RlsGuard/EmailSenderGuard). Emis ici, et non dans
// AddInfrastructure, parce que c'est le premier endroit où un ILogger existe — sans cet appel, les
// gardes ne seraient qu'un texte que personne n'affiche jamais.
//
// AVANT la garde RLS ci-dessous, qui exige un aller-retour SQL : sur une base injoignable, ces
// avertissements de CONFIGURATION seraient sinon perdus, et le journal de démarrage ne montrerait que
// l'échec réseau — en cachant qu'un canal sortant est par ailleurs mal configuré.
foreach (var warning in new[]
{
    EmailSenderGuard.DescribeDegradedMode(app.Services.GetRequiredService<IOptions<SmtpOptions>>().Value, app.Environment.IsDevelopment()),
    SmsServiceGuard.DescribeMisconfiguration(app.Services.GetRequiredService<IOptions<SmsOptions>>().Value, app.Environment.IsDevelopment()),
    SmsServiceGuard.DescribeWhatsAppMisconfiguration(app.Services.GetRequiredService<IOptions<WhatsAppOptions>>().Value, app.Environment.IsDevelopment())
})
{
    if (warning is not null)
        app.Logger.LogWarning("{Warning}", warning);
}

app.Logger.LogInformation("Démarrage : environnement {Environment}, écoute prévue sur http://0.0.0.0:{Port}.", app.Environment.EnvironmentName, port);
await app.Services.EnsureRuntimeRoleCannotBypassRlsAsync();

if (forwardedHeadersEnabled)
    app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host.Equals("www.unikol.net", StringComparison.OrdinalIgnoreCase))
    {
        var target = $"https://unikol.net{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
        ctx.Response.Redirect(target, permanent: true);
        return;
    }
    await next();
});

app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    var ownerConnectionString = builder.Configuration["DATABASE_OWNER_CONNECTION_STRING"]
        ?? builder.Configuration.GetConnectionString("Migrations")
        ?? builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(ownerConnectionString))
    {
        using var scope = app.Services.CreateScope();
        await DbSeeder.SeedAsync(ownerConnectionString, scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
    }
}

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var staticFileContentTypes = new FileExtensionContentTypeProvider();
staticFileContentTypes.Mappings[".mjs"] = "text/javascript";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileContentTypes,
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.GetTypedHeaders();
        headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v")
            ? new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(365), Extensions = { new NameValueHeaderValue("immutable") } }
            : new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(1) };
    }
});
app.UseAuthentication();
app.UseMiddleware<SubscriptionAwaitingPaymentMiddleware>();
app.UseMiddleware<OnboardingRoutingMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
