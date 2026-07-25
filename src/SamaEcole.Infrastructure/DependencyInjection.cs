using System.Net.Http;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
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
    /// <param name="isDevelopment">
    /// Vient de <c>IHostEnvironment.IsDevelopment()</c> (résolu dans Program.cs, seul endroit qui
    /// connaît l'environnement d'hébergement) : jamais dérivé d'une variable de configuration
    /// modifiable, pour qu'aucun déploiement Staging/Production mal configuré ne puisse activer
    /// DevPaymentService (voir sa condition d'enregistrement plus bas).
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Aucun cache applicatif (Redis) n'est encore consommé en code à ce jour — enregistré par
        // anticipation (Volume_6_Dev_Guide.md : Redis prévu dès la V1) pour qu'un futur ajout de cache
        // n'ait JAMAIS à composer une clé "à la main" : voir ITenantCacheKeyFactory.
        services.AddScoped<ITenantCacheKeyFactory, TenantCacheKeyFactory>();

        // Authentification (ticket JGK-A04).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Mot de passe initial du Directeur (ticket JGK-B01).
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

        // Référence de suivi d'une demande d'inscription self-service (ticket JGK-I01).
        services.AddSingleton<IRegistrationReferenceGenerator, RegistrationReferenceGenerator>();

        services.AddSingleton<IQrCodeService, SamaEcole.Infrastructure.Services.QrCodeService>();


        // Envoi d'e-mails transactionnels (ticket JGK-G03, incl. le mot de passe provisoire du
        // Directeur — JGK-B01). Development : LoggingEmailSender, qui journalise en clair (pratique en
        // local, voir sa doc). Hors Development : SmtpEmailSender réel, et l'absence de configuration
        // SMTP fait ÉCHOUER LE DÉMARRAGE plutôt que de retomber silencieusement sur l'adaptateur qui
        // journalise les mots de passe — même logique que RlsGuard / PayDunyaOptions.IsConfigured.
        var smtpOptions = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        if (isDevelopment)
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddSingleton<IWhatsAppSender, LoggingWhatsAppSender>();
        }
        else
        {
            EmailSenderGuard.EnsureEmailSenderIsConfigured(smtpOptions, isDevelopment);
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
            // Fallback pour WhatsApp en production tant que Twilio n'est pas implémenté
            services.AddSingleton<IWhatsAppSender, LoggingWhatsAppSender>();
        }

        // Génération PDF des reçus (inscription JGK-E02, paiement JGK-F02) et certificat d'inscription (Axe 2). Sans état : des singletons suffisent.
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddSingleton<IPaymentReceiptPdfGenerator, PaymentReceiptPdfGenerator>();
        services.AddSingleton<IDailyCashRegisterPdfGenerator, DailyCashRegisterPdfGenerator>();
        services.AddSingleton<IDailyClosingReportPdfGenerator, SamaEcole.Infrastructure.Documents.DailyClosingReportPdfGenerator>();
        services.AddSingleton<ISchoolCardPdfGenerator, SamaEcole.Infrastructure.Documents.SchoolCardPdfGenerator>();
        services.AddSingleton<IEnrollmentCertificatePdfGenerator, EnrollmentCertificatePdfGenerator>();

        // Billet d'entrée en classe A5 (module Surveillance) — même moteur QuestPDF, sans état.
        services.AddSingleton<IEntryTicketPdfGenerator, EntryTicketPdfGenerator>();

        // Bulletin de notes PDF (ticket JGK-G03) — même moteur QuestPDF, même convention.
        services.AddSingleton<IReportCardPdfGenerator, ReportCardPdfGenerator>();

        // Bulletins de classe fusionnés en un seul PDF, pour l'impression en lot — même moteur QuestPDF.
        services.AddSingleton<IClassBulletinsPdfGenerator, ClassBulletinsPdfGenerator>();
        services.AddSingleton<IClassDeliberationPdfGenerator, ClassDeliberationPdfGenerator>();

        // Rapport d'assiduité PDF (ticket JGK-R03) — même moteur QuestPDF, sans état.
        services.AddSingleton<IAttendanceReportPdfGenerator, AttendanceReportPdfGenerator>();

        // Import de notes par fichier CSV/Excel (ClosedXML, Volume_2_SDS.md « bibliothèques »).
        // Sans état : un singleton suffit, comme les générateurs de documents ci-dessus.
        services.AddSingleton<IGradeImportFileParser, GradeImportFileParser>();

        // Import d'élèves par fichier CSV/Excel (même bibliothèque ClosedXML, aucune nouvelle dépendance).
        services.AddSingleton<IStudentImportFileParser, StudentImportFileParser>();
        services.AddSingleton<IStudentImportTemplateGenerator, StudentImportTemplateGenerator>();

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
        var payDunyaSection = configuration.GetSection(PayDunyaOptions.SectionName);
        services.Configure<PayDunyaOptions>(payDunyaSection);
        services.Configure<SubscriptionPricingOptions>(configuration.GetSection(SubscriptionPricingOptions.SectionName));

        // Toujours enregistré (même hors Development) : DevPaymentSimulationController en dépend pour
        // afficher son faux guichet, et sa propre garde IWebHostEnvironment.IsDevelopment() suffit à en
        // interdire l'usage ailleurs — un DevPaymentService inutilisé ne coûte rien.
        services.AddSingleton<DevPaymentService>();

        // Bascule Dev-only : Environment=Development ET clés absentes/au sentinel "REMPLACER" (les DEUX
        // conditions, jamais l'une sans l'autre — un compte marchand configuré en Development doit
        // continuer à passer par le vrai PayDunya, et aucune configuration Staging/Production ne doit
        // jamais pouvoir déclencher l'auto-confirmation, même par erreur). Sans elle, l'écran
        // /abonnement/paiement reste bloqué pour quiconque n'a pas de compte PayDunya (sandbox ou prod)
        // sous la main en local.
        var payDunyaOptions = payDunyaSection.Get<PayDunyaOptions>() ?? new PayDunyaOptions();
        if (isDevelopment && !payDunyaOptions.IsConfigured)
        {
            services.AddSingleton<IPaymentService>(provider => provider.GetRequiredService<DevPaymentService>());
        }
        else
        {
            services.AddHttpClient(PayDunyaPaymentService.HttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PayDunyaOptions>>().Value;
                client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddScoped<IPaymentService, PayDunyaPaymentService>();
        }

        services.AddSingleton<ISubscriptionPricingProvider, ConfiguredSubscriptionPricingProvider>();

        return services;
    }
}
