using System.Net.Http;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Notifications;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Infrastructure.Caching;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.Infrastructure.Files;
using SamaEcole.Infrastructure.Finance;
using SamaEcole.Infrastructure.Media;
using SamaEcole.Infrastructure.Multitenancy;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Infrastructure.Payments;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Infrastructure.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SamaEcole.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Sous-section « Sms:Queue » (voir SmsQueueSettings et .env.example).</summary>
    private const string SmsQueueSettingsSection = "Sms:Queue";

    /// <summary>Sous-section « Finance:DebtorAging » (voir DebtorAgingSettings et .env.example).</summary>
    private const string DebtorAgingSettingsSection = "Finance:DebtorAging";

    /// <summary>Sous-section « Subscriptions:Lifecycle » (voir SubscriptionLifecycleSettings et .env.example).</summary>
    private const string SubscriptionLifecycleSettingsSection = "Subscriptions:Lifecycle";

    /// <summary>Sous-section « Kpi:Cache » (voir KpiCacheSettings et .env.example).</summary>
    private const string KpiCacheSettingsSection = "Kpi:Cache";

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

        // Premier consommateur réel de ITenantCacheKeyFactory : cache des agrégats KPI (dashboards
        // Finance/Directeur) derrière IMemoryCache. Le remplacement futur par Redis (IDistributedCache,
        // Volume_6_Dev_Guide.md) se limite à une nouvelle implémentation de IKpiCacheService — aucun
        // handler ne dépend de IMemoryCache directement.
        services.AddScoped<ITenantCacheKeyFactory, TenantCacheKeyFactory>();

        services.AddMemoryCache();
        var kpiCacheSettings = configuration.GetSection(KpiCacheSettingsSection).Get<KpiCacheSettings>()
                                ?? new KpiCacheSettings();
        services.AddSingleton(kpiCacheSettings);
        services.AddScoped<IKpiCacheService, MemoryKpiCacheService>();

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
        }
        else if (smtpOptions.IsConfigured)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            // Ni Development, ni SMTP configuré : la garde fait échouer le démarrage, SAUF renonciation
            // explicite (Smtp__AllowUnconfigured=true), qui bascule alors sur un adaptateur qui refuse
            // d'envoyer sans jamais journaliser de corps de message. Program.cs émet l'avertissement
            // correspondant (EmailSenderGuard.DescribeDegradedMode).
            EmailSenderGuard.EnsureEmailSenderIsConfigured(smtpOptions, isDevelopment);
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        // WhatsApp — même règle de choix que les SMS, et pour la même raison : une configuration
        // absente ne fait PAS échouer le démarrage (le canal est optionnel), mais elle ne doit pas
        // non plus laisser croire qu'un bulletin est parti. LoggingWhatsAppSender journalise en clair
        // « NON ENVOYÉ », et SmsServiceGuard avertit au démarrage hors Development.
        var whatsAppOptions = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>()
                              ?? new WhatsAppOptions();
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));

        if (!isDevelopment && whatsAppOptions.IsConfigured)
        {
            services.AddHttpClient<IWhatsAppSender, HttpWhatsAppSender>(client =>
                client.Timeout = TimeSpan.FromSeconds(30));
        }
        else
        {
            services.AddSingleton<IWhatsAppSender, LoggingWhatsAppSender>();
        }

        // Notifications SMS (offre Premium). Contrairement au SMTP, une configuration absente ne fait
        // PAS échouer le démarrage : les SMS sont optionnels et rien n'est exposé sans eux — mais
        // retomber silencieusement sur l'adaptateur qui journalise ferait croire à une école Premium
        // que ses parents sont alertés. D'où l'avertissement explicite de SmsServiceGuard.
        var smsOptions = configuration.GetSection(SmsOptions.SectionName).Get<SmsOptions>() ?? new SmsOptions();
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));

        if (!isDevelopment && smsOptions.IsConfigured)
        {
            // AddHttpClient plutôt qu'un HttpClient construit à la main : gestion du pool de
            // connexions et du recyclage DNS, comme pour tout appel sortant durable.
            services.AddHttpClient<ISmsService, HttpSmsService>(client =>
                client.Timeout = TimeSpan.FromSeconds(15));
        }
        else
        {
            services.AddSingleton<ISmsService, LoggingSmsService>();
        }

        // Lecteur d'accusés de réception. Enregistré dans TOUS les environnements, y compris
        // Development : il refuse de lui-même tout appel tant qu'aucun secret n'est configuré
        // (IsWebhookSecretConfigured), il n'y a donc pas de porte ouverte à refermer ici.
        services.AddSingleton<ISmsDeliveryReceiptReader, HmacSmsDeliveryReceiptReader>();

        // Réglages du dépilage de la file. Instance SINGLETON plutôt qu'IOptions : le service hébergé
        // en a besoin dès son démarrage, avant toute portée de requête, et ces valeurs ne changent
        // pas à chaud.
        var queueSettings = configuration.GetSection(SmsQueueSettingsSection).Get<SmsQueueSettings>()
                            ?? new SmsQueueSettings();
        services.AddSingleton(queueSettings);

        // Le dépilage ne démarre QUE s'il est activé (les tests fonctionnels le coupent : un worker
        // qui bascule un message de Pending à Sent au milieu d'une assertion rendrait la suite non
        // déterministe). En Development il tourne comme en production, avec LoggingSmsService en
        // guise de fournisseur — c'est ce qui rend la file observable en local.
        if (queueSettings.Enabled)
        {
            services.AddHostedService<SmsQueueHostedService>();
        }

        // Calcul quotidien des lots de relance de débiteurs (Étape 5 — recouvrement semi-automatique).
        // Même garde que la file SMS : les tests fonctionnels le coupent (Finance__DebtorAging__Enabled=false)
        // pour qu'un lot brouillon créé pendant l'exécution d'un test ne rende pas son résultat non
        // déterministe.
        var debtorAgingSettings = configuration.GetSection(DebtorAgingSettingsSection).Get<DebtorAgingSettings>()
                                   ?? new DebtorAgingSettings();
        services.AddSingleton(debtorAgingSettings);

        if (debtorAgingSettings.Enabled)
        {
            services.AddHostedService<DebtorAgingHostedService>();
        }

        // Alertes d'expiration 30/15/7 jours et passage automatique en lecture seule (ticket JGK-B03).
        // Même garde que les deux workers ci-dessus : les tests fonctionnels le coupent
        // (Subscriptions__Lifecycle__Enabled=false) pour qu'un abonnement basculé en ReadOnly pendant
        // l'exécution d'un test ne rende pas son résultat non déterministe.
        var subscriptionLifecycleSettings = configuration.GetSection(SubscriptionLifecycleSettingsSection)
            .Get<SubscriptionLifecycleSettings>() ?? new SubscriptionLifecycleSettings();
        services.AddSingleton(subscriptionLifecycleSettings);

        if (subscriptionLifecycleSettings.Enabled)
        {
            services.AddHostedService<SubscriptionLifecycleHostedService>();
        }

        // Génération PDF des reçus (inscription JGK-E02, paiement JGK-F02) et certificat d'inscription (Axe 2). Sans état : des singletons suffisent.
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddSingleton<IPaymentReceiptPdfGenerator, PaymentReceiptPdfGenerator>();
        services.AddSingleton<IDailyCashRegisterPdfGenerator, DailyCashRegisterPdfGenerator>();
        services.AddSingleton<IDailyClosingReportPdfGenerator, SamaEcole.Infrastructure.Documents.DailyClosingReportPdfGenerator>();
        services.AddSingleton<ISchoolCardPdfGenerator, SamaEcole.Infrastructure.Documents.SchoolCardPdfGenerator>();
        services.AddSingleton<IEnrollmentCertificatePdfGenerator, EnrollmentCertificatePdfGenerator>();

        // Module Documents administratifs (cahier des charges élite) — nouveaux documents officiels,
        // même moteur QuestPDF que ci-dessus, sans état.
        services.AddSingleton<IExeatCertificatePdfGenerator, ExeatCertificatePdfGenerator>();
        services.AddSingleton<IDisciplinaryPvPdfGenerator, DisciplinaryPvPdfGenerator>();
        services.AddSingleton<IDuesNoticePdfGenerator, DuesNoticePdfGenerator>();
        services.AddSingleton<IWorkCertificatePdfGenerator, WorkCertificatePdfGenerator>();
        services.AddSingleton<IParentNoticePdfGenerator, ParentNoticePdfGenerator>();
        services.AddSingleton<IFinancialCommitmentPdfGenerator, FinancialCommitmentPdfGenerator>();
        services.AddSingleton<IHourRecordSheetPdfGenerator, HourRecordSheetPdfGenerator>();

        // Billet d'entrée en classe A5 (module Surveillance) — même moteur QuestPDF, sans état.
        services.AddSingleton<IEntryTicketPdfGenerator, EntryTicketPdfGenerator>();
        services.AddSingleton<IExitTicketPdfGenerator, ExitTicketPdfGenerator>();

        // Bulletin de paie A4 (module Comptabilité & Fiscalité) — même moteur QuestPDF, sans état.
        services.AddSingleton<IPayslipPdfGenerator, PayslipPdfGenerator>();

        // Déclaration fiscale mensuelle A4 (module Comptabilité & Fiscalité) — même moteur QuestPDF, sans état.
        services.AddSingleton<ITaxDeclarationPdfGenerator, TaxDeclarationPdfGenerator>();

        // Bulletin de notes PDF (ticket JGK-G03) — même moteur QuestPDF, même convention.
        services.AddSingleton<IReportCardPdfGenerator, ReportCardPdfGenerator>();

        // Bulletins de classe fusionnés en un seul PDF, pour l'impression en lot — même moteur QuestPDF.
        services.AddSingleton<IClassBulletinsPdfGenerator, ClassBulletinsPdfGenerator>();
        services.AddSingleton<IClassDeliberationPdfGenerator, ClassDeliberationPdfGenerator>();

        // Rapport d'assiduité PDF (ticket JGK-R03) — même moteur QuestPDF, sans état.
        services.AddSingleton<IAttendanceReportPdfGenerator, AttendanceReportPdfGenerator>();

        // Export PDF des élèves (Volume_7_Security.md §15, matrice Élèves) — même moteur QuestPDF, sans état.
        services.AddSingleton<IStudentsExportPdfGenerator, StudentsExportPdfGenerator>();
        services.AddSingleton<ITeachersExportPdfGenerator, TeachersExportPdfGenerator>();

        // Module Inventaire : fiche d'inventaire global (A4 paysage) et fiche de décharge de matériel
        // (A5 paysage, charte ReceiptTheme). Sans état, comme tous les générateurs ci-dessus.
        services.AddSingleton<IInventoryReportPdfGenerator, InventoryReportPdfGenerator>();
        services.AddSingleton<IDischargeNotePdfGenerator, DischargeNotePdfGenerator>();

        // Import/export de la feuille de notes au format Excel large (ClosedXML, Volume_2_SDS.md
        // « bibliothèques »). Sans état : un singleton suffit, comme les générateurs de documents ci-dessus.
        services.AddSingleton<IGradeSheetImportParser, GradeSheetImportParser>();
        services.AddSingleton<IGradeSheetExcelGenerator, GradeSheetExcelGenerator>();

        // Export comptable de la consolidation des revenus (rapports financiers avancés) — même
        // bibliothèque, aucune nouvelle dépendance.
        services.AddSingleton<IRevenueReportExcelGenerator, RevenueReportExcelGenerator>();

        // Export comptable des débiteurs (Étape 5 — recouvrement) — même bibliothèque, aucune nouvelle
        // dépendance.
        services.AddSingleton<IDebtorAgingExcelGenerator, DebtorAgingExcelGenerator>();

        // Module Examens officiels : relevé d'inscription (.xlsx, ClosedXML) et fiches de candidature /
        // convocations PDF (QuestPDF, charte OfficialHeaderComponent). Sans état, comme le reste.
        services.AddSingleton<IExamRegistrationExcelGenerator, ExamRegistrationExcelGenerator>();
        services.AddSingleton<IExamCandidateFormPdfGenerator, ExamCandidateFormPdfGenerator>();
        services.AddSingleton<IExamConvocationPdfGenerator, ExamConvocationPdfGenerator>();

        // ---------------------------------------------- Module Intégration étatique (SIMEN, §23)
        //
        // Réglages liés depuis la section « StateIntegration » d'appsettings. Aucun secret n'y
        // transite : la clé d'API du SIMEN, le jour où elle existera, suivra le chemin protégé des
        // identifiants de l'agrégateur de paiement.
        var stateIntegrationSettings = configuration
            .GetSection("StateIntegration")
            .Get<StateIntegrationSettings>() ?? new StateIntegrationSettings();
        services.AddSingleton(stateIntegrationSettings);

        // Générateurs sans état, comme tous les autres documents du projet.
        services.AddSingleton<IPlaneteExportSerializer, PlaneteExportSerializer>();
        services.AddSingleton<IStateducReportPdfGenerator, StateducReportPdfGenerator>();
        services.AddSingleton<IStateducReportExcelGenerator, StateducReportExcelGenerator>();
        services.AddSingleton<IStudentMutationCertificatePdfGenerator, StudentMutationCertificatePdfGenerator>();
        services.AddSingleton<ISkillsBookletPdfGenerator, SkillsBookletPdfGenerator>();

        // Relais SIMEN : l'implémentation livrée REFUSE chaque appel, explicitement — aucune API
        // publique n'existe à ce jour. Elle sera remplacée par HttpSimenBridgeService le jour où le
        // ministère en ouvrira une. Voir UnavailableSimenBridgeService pour le raisonnement.
        services.AddSingleton<ISimenBridgeService, SamaEcole.Infrastructure.Services.UnavailableSimenBridgeService>();

        // Import d'élèves par fichier CSV/Excel (même bibliothèque ClosedXML, aucune nouvelle dépendance).
        services.AddSingleton<IStudentImportFileParser, StudentImportFileParser>();
        services.AddSingleton<IStudentImportTemplateGenerator, StudentImportTemplateGenerator>();

        // Import du corps professoral par fichier CSV/Excel — même patron que l'import d'élèves.
        services.AddSingleton<ITeacherImportFileParser, TeacherImportFileParser>();
        services.AddSingleton<ITeacherImportTemplateGenerator, TeacherImportTemplateGenerator>();

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
