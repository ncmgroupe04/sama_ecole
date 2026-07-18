using System.Net.Http;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.Infrastructure.Files;
using SamaEcole.Infrastructure.Media;
using SamaEcole.Infrastructure.Multitenancy;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Infrastructure.Payments;
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

        // Référence de suivi d'une demande d'inscription self-service (ticket JGK-I01).
        services.AddSingleton<IRegistrationReferenceGenerator, RegistrationReferenceGenerator>();

        // ⚠️ N'ENVOIE RIEN : journalise l'e-mail. L'adaptateur SMTP réel (MailKit) est le ticket
        // JGK-G03. À remplacer avant toute exploitation réelle — voir LoggingEmailSender.
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Génération PDF des reçus (inscription JGK-E02, paiement JGK-F02). Sans état : des singletons suffisent.
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddSingleton<IPaymentReceiptPdfGenerator, PaymentReceiptPdfGenerator>();

        // Bulletin de notes PDF (ticket JGK-G03) — même moteur QuestPDF, même convention.
        services.AddSingleton<IReportCardPdfGenerator, ReportCardPdfGenerator>();

        // Rapport d'assiduité PDF (ticket JGK-R03) — même moteur QuestPDF, sans état.
        services.AddSingleton<IAttendanceReportPdfGenerator, AttendanceReportPdfGenerator>();

        // Import de notes par fichier CSV/Excel (ClosedXML, Volume_2_SDS.md « bibliothèques »).
        // Sans état : un singleton suffit, comme les générateurs de documents ci-dessus.
        services.AddSingleton<IGradeImportFileParser, GradeImportFileParser>();

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

        // Paiement d'abonnement (ticket JGK-I05) — agrégateur PayDunya derrière IPaymentService
        // (agnostique, docs/Volume_1_Cahier_des_Charges.md §11.6 : « le choix définitif reste à
        // valider »). Client HTTP dédié, sans garde SSRF ici : l'URL est une constante de configuration
        // (PayDunyaOptions.ApiBaseUrl), jamais une adresse fournie par un utilisateur.
        services.Configure<PayDunyaOptions>(configuration.GetSection(PayDunyaOptions.SectionName));
        services.Configure<SubscriptionPricingOptions>(configuration.GetSection(SubscriptionPricingOptions.SectionName));
        services.AddHttpClient(PayDunyaPaymentService.HttpClientName, (provider, client) =>
        {
            var payDunyaOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PayDunyaOptions>>().Value;
            client.BaseAddress = new Uri(payDunyaOptions.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IPaymentService, PayDunyaPaymentService>();
        services.AddSingleton<ISubscriptionPricingProvider, ConfiguredSubscriptionPricingProvider>();

        return services;
    }
}
