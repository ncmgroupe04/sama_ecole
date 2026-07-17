using System.Net.Http;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.Infrastructure.Media;
using SamaEcole.Infrastructure.Multitenancy;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SamaEcole.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Authentification (ticket JGK-A04).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Mot de passe initial du Directeur (ticket JGK-B01).
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

        // ⚠️ N'ENVOIE RIEN : journalise l'e-mail. L'adaptateur SMTP réel (MailKit) est le ticket
        // JGK-G03. À remplacer avant toute exploitation réelle — voir LoggingEmailSender.
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Génération PDF des reçus (inscription JGK-E02, paiement JGK-F02). Sans état : des singletons suffisent.
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddSingleton<IPaymentReceiptPdfGenerator, PaymentReceiptPdfGenerator>();

        // Bulletin de notes PDF (ticket JGK-G03) — même moteur QuestPDF, même convention.
        services.AddSingleton<IReportCardPdfGenerator, ReportCardPdfGenerator>();

        // Récupération du logo de l'établissement pour le reçu (JGK-E02). Client HTTP dédié :
        //  * garde anti-SSRF au moment de la connexion (l'URL vient du Directeur — cf. SsrfSafeConnect) ;
        //  * aucune redirection auto : une 3xx pourrait rebondir d'une URL publique vers un service interne ;
        //  * timeout court : le logo ne doit jamais retarder l'émission d'un reçu.
        services.AddHttpClient(HttpSchoolLogoProvider.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SamaEcole-Recu/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = SsrfSafeConnect.ConnectAsync
            });
        services.AddSingleton<ISchoolLogoProvider, HttpSchoolLogoProvider>();

        return services;
    }
}
