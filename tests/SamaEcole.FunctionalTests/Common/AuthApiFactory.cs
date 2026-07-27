using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Démarre l'API réelle contre un PostgreSQL réel (ticket JGK-A04).
///
/// L'application se connecte avec le RÔLE APPLICATIF (NOSUPERUSER/NOBYPASSRLS) : c'est la seule
/// configuration où RlsGuard accepte de démarrer, et la seule où les policies RLS mordent vraiment.
/// Les migrations, elles, tournent avec le propriétaire.
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRole = "sama_ecole_app";
    private const string AppPassword = "app_password_for_tests";

    public const string SigningKey = "cle_de_test_suffisamment_longue_pour_hmac_sha256_0123456789";
    public const string Issuer = "https://api.sama-ecole.sn";
    public const string Audience = "sama-ecole-clients";

    public const string DirecteurEmail = "directeur@sama-ecole.sn";
    public const string DirecteurPassword = "Motdepasse!Solide2026";

    public const string SecretaireEmail = "secretaire@sama-ecole.sn";
    public const string SecretairePassword = "AutreMotdepasse!2026";

    public const string FinanceEmail = "finance@sama-ecole.sn";
    public const string FinancePassword = "FinanceMotdepasse!2026";

    public const string EnseignantEmail = "enseignant@sama-ecole.sn";
    public const string EnseignantPassword = "EnseignantMotdepasse!2026";

    // Super Admin : SchoolId NULL, il n'appartient à aucun établissement (ticket JGK-B01).
    public const string SuperAdminEmail = "superadmin@sama-ecole.sn";
    public const string SuperAdminPassword = "SuperMotdepasse!2026";

    public static readonly Guid EcoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DirecteurId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid SecretaireId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    public static readonly Guid SuperAdminId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    public static readonly Guid FinanceId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
    public static readonly Guid EnseignantId = Guid.Parse("ffffffff-0000-0000-0000-000000000006");

    /// <summary>Capture les e-mails sortants : c'est le seul canal par lequel passe le mot de passe initial.</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Remplace PayDunyaPaymentService (ticket JGK-I05) : aucune clé réelle dans les tests.</summary>
    public FakePaymentService Payments { get; } = new();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string OwnerConnectionString => _postgres.GetConnectionString();

    private string AppConnectionString =>
        new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // AVANT toute création du host (elle est déclenchée par le premier CreateClient()/Services).
        ApplyEnvironment();

        // Le rôle doit exister AVANT les migrations : ce sont elles qui lui posent ses GRANT,
        // y compris EXECUTE sur les fonctions d'authentification.
        await ExecuteAsOwnerAsync($"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            """);

        await using var owner = NewOwnerContext();
        await owner.Database.MigrateAsync();

        // Les comptes de test sont semés avec un VRAI hash Identity : le login doit le vérifier
        // réellement, pas court-circuiter le hachage.
        var hasher = new IdentityPasswordHasher();

        owner.Schools.Add(new School { Id = EcoleId, Name = "École de test" });

        owner.Users.AddRange(
            new User
            {
                Id = DirecteurId,
                SchoolId = EcoleId,
                Email = DirecteurEmail,
                PasswordHash = hasher.Hash(DirecteurPassword),
                FullName = "Directeur de test",
                Role = Role.Directeur,
                Status = EntityStatus.Active
            },
            // Cible des suspensions (ticket JGK-A05) : un Directeur ne peut pas modifier son PROPRE
            // statut, il faut donc un second compte dans la même école.
            new User
            {
                Id = SecretaireId,
                SchoolId = EcoleId,
                Email = SecretaireEmail,
                PasswordHash = hasher.Hash(SecretairePassword),
                FullName = "Secrétaire de test",
                Role = Role.Secretariat,
                Status = EntityStatus.Active
            },
            // Rôle Finance : encaisse, mais ne compose JAMAIS un montant dû à l'inscription (règle #4).
            // Sert au test d'autorisation négatif obligatoire de JGK-E01 (POST /enrollments → 403).
            new User
            {
                Id = FinanceId,
                SchoolId = EcoleId,
                Email = FinanceEmail,
                PasswordHash = hasher.Hash(FinancePassword),
                FullName = "Comptable de test",
                Role = Role.Finance,
                Status = EntityStatus.Active
            },
            // Rôle Enseignant (ticket JGK-G01) : saisit les notes, mais ne fixe ni les frais ni les
            // inscriptions.
            new User
            {
                Id = EnseignantId,
                SchoolId = EcoleId,
                Email = EnseignantEmail,
                PasswordHash = hasher.Hash(EnseignantPassword),
                FullName = "Enseignant de test",
                Role = Role.Enseignant,
                Status = EntityStatus.Active
            },
            // Aucun SchoolId : sa session n'a pas de tenant, la RLS lui ferme donc toutes les tables
            // d'école. C'est précisément pourquoi la création d'un Directeur exige une fonction
            // SECURITY DEFINER (ticket JGK-B01).
            new User
            {
                Id = SuperAdminId,
                SchoolId = null,
                Email = SuperAdminEmail,
                PasswordHash = hasher.Hash(SuperAdminPassword),
                FullName = "Super Admin de test",
                Role = Role.SuperAdmin,
                Status = EntityStatus.Active
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        ClearEnvironment();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // ConfigureTestServices s'exécute APRÈS les enregistrements de Program.cs : le remplacement de
        // service fonctionne, lui (contrairement à ConfigureAppConfiguration — voir plus bas).
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.RemoveAll<IPaymentService>();
            services.AddSingleton<IPaymentService>(Payments);
        });
    }

    /// <summary>
    /// La configuration passe par des VARIABLES D'ENVIRONNEMENT, et non par ConfigureAppConfiguration.
    ///
    /// Piège du minimal hosting : Program.cs lit builder.Configuration pendant l'enregistrement des
    /// services (AddPersistence(builder.Configuration), AddJwtBearer(...)), alors que les sources
    /// ajoutées par WebApplicationFactory.ConfigureAppConfiguration ne sont appliquées qu'au Build().
    /// Elles arrivent donc TROP TARD : l'API de test se connectait à la base de développement réelle
    /// au lieu du conteneur, et cherchait le compte de test dans la mauvaise base.
    ///
    /// WebApplication.CreateBuilder lit AddEnvironmentVariables() d'entrée de jeu : ces valeurs-là
    /// sont visibles dès la première ligne de Program.cs.
    ///
    /// Contrepartie : les variables d'environnement sont globales au processus. D'où la
    /// désactivation du parallélisme dans AssemblyInfo.cs, sans quoi deux fabriques concurrentes se
    /// voleraient leur chaîne de connexion.
    /// </summary>
    private void ApplyEnvironment()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", AppConnectionString);

        // NEUTRALISE la chaîne « Migrations ». L'API de test tourne en environnement Development et
        // charge donc appsettings.Development.json, où cette chaîne pointe vers la base de
        // développement RÉELLE du poste (localhost:5432). Or Program.cs s'en sert pour semer les
        // comptes de démonstration au démarrage : sans cette ligne, chaque exécution des tests
        // fonctionnels écrivait dans la vraie base du développeur au lieu de son conteneur — un
        // effet de bord invisible tant que les deux schémas coïncidaient.
        //
        // Vidée, elle fait sauter le seeder (Program.cs ignore une chaîne vide) : ces tests posent
        // eux-mêmes le jeu de données dont ils ont besoin, et rien d'autre.
        Environment.SetEnvironmentVariable("ConnectionStrings__Migrations", string.Empty);
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Auth__MaxFailedAttempts", "5");
        Environment.SetEnvironmentVariable("Auth__LockoutMinutes", "15");
        Environment.SetEnvironmentVariable("Auth__RefreshTokenDays", "14");

        // La limite par défaut (5 requêtes / 5 min, appsettings.json) protège le formulaire public
        // d'inscription (JGK-I01) en production, mais TestServer ne renseigne aucune IP source
        // (Connection.RemoteIpAddress est null en transport in-memory) : toutes les requêtes de TOUS
        // les tests de la classe partagent donc la même partition « unknown ». Sans ce relèvement,
        // une poignée de tests suffirait à déclencher un 429 et à faire échouer les suivants.
        Environment.SetEnvironmentVariable("RateLimiting__Registration__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Registration__WindowMinutes", "5");

        // Même raisonnement que la limite d'inscription ci-dessus : TestServer ne renseigne aucune IP
        // source, donc TOUTES les connexions de TOUS les tests d'une classe partagent la même
        // partition « unknown ». La limite de production (10 / 5 min, appsettings.json) protège contre
        // le credential stuffing — elle n'a rien à prouver ici, où chaque test se reconnecte
        // volontairement à chaque fois par clarté et isolation plutôt que de partager un jeton.
        Environment.SetEnvironmentVariable("RateLimiting__Login__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Login__WindowMinutes", "5");

        // Même raison encore : la limite de production est ici volontairement BASSE (5 / 15 min — une
        // réinitialisation est un geste rare), donc la partition « unknown » partagée la ferait sauter
        // dès le sixième appel de la suite. Le comportement de refus lui-même n'a pas à être prouvé
        // ici : c'est de la configuration ASP.NET, pas du code applicatif.
        Environment.SetEnvironmentVariable("RateLimiting__PasswordReset__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__PasswordReset__WindowMinutes", "5");

        // COUPE le dépilage de la file SMS. Sans cela, SmsQueueHostedService tournerait en tâche de
        // fond pendant toute la suite et ferait passer un message de Pending à Sent entre l'action
        // d'un test et son assertion : l'issue dépendrait du moment où le minuteur se déclenche.
        // Le worker est testé pour lui-même, en pilotant un tour explicitement (SmsQueueTests).
        Environment.SetEnvironmentVariable("Sms__Queue__Enabled", "false");

        // Même raison : COUPE le calcul quotidien des lots de relance de débiteurs
        // (DebtorAgingHostedService, Étape 5). Le worker est testé pour lui-même en invoquant
        // GenerateDebtorReminderBatchesCommand explicitement.
        Environment.SetEnvironmentVariable("Finance__DebtorAging__Enabled", "false");
    }

    private static void ClearEnvironment()
    {
        foreach (var key in new[]
                 {
                     "ConnectionStrings__Default", "ConnectionStrings__Migrations",
                     "Jwt__Issuer", "Jwt__Audience", "Jwt__SigningKey",
                     "Jwt__AccessTokenMinutes", "Auth__MaxFailedAttempts", "Auth__LockoutMinutes",
                     "Auth__RefreshTokenDays", "RateLimiting__Registration__PermitLimit",
                     "RateLimiting__Registration__WindowMinutes", "RateLimiting__Login__PermitLimit",
                     "RateLimiting__Login__WindowMinutes", "RateLimiting__PasswordReset__PermitLimit",
                     "RateLimiting__PasswordReset__WindowMinutes", "Sms__Queue__Enabled",
                     "Finance__DebtorAging__Enabled"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Remet les comptes de test à neuf entre deux tests (ticket JGK-A05, puis la réinitialisation de
    /// mot de passe). Les tests de statut bloquent et débloquent les mêmes comptes, et ceux de mot de
    /// passe le RÉÉCRIVENT en dur : sans remise à zéro du hash ici aussi, le premier test qui change le
    /// mot de passe de la secrétaire ferait échouer tous les suivants qui se connectent avec
    /// SecretairePassword — l'ordre d'exécution deviendrait significatif.
    ///
    /// Exécuté par le PROPRIÉTAIRE : le rôle applicatif n'a volontairement pas le droit de purger
    /// user_status_history (journal append-only).
    /// </summary>
    public async Task ResetTestUsersAsync()
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlRawAsync("DELETE FROM user_status_history;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM audit_logs;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM refresh_tokens;");

        // Demandes d'inscription self-service (ticket JGK-I01) : table PLATEFORME, sans SchoolId à
        // rattacher — sans cette purge, une demande laissée par un test fausserait le décompte du
        // suivant (ex. « lister les demandes en attente » du futur JGK-I03).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_registration_requests;");

        var hasher = new IdentityPasswordHasher();
        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE users SET "PasswordHash" = {hasher.Hash(DirecteurPassword)} WHERE "Id" = {DirecteurId};""");
        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE users SET "PasswordHash" = {hasher.Hash(SecretairePassword)} WHERE "Id" = {SecretaireId};""");
        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE users SET "PasswordHash" = {hasher.Hash(FinancePassword)} WHERE "Id" = {FinanceId};""");
        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE users SET "PasswordHash" = {hasher.Hash(EnseignantPassword)} WHERE "Id" = {EnseignantId};""");
        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE users SET "PasswordHash" = {hasher.Hash(SuperAdminPassword)} WHERE "Id" = {SuperAdminId};""");

        // Écoles et comptes créés PAR les tests (ticket JGK-B01) : sans cette purge, une école créée
        // dans un test resterait provisionnée et fausserait le suivant. Les utilisateurs d'abord :
        // ils référencent les écoles.
        await owner.Database.ExecuteSqlRawAsync(
            $"""
             DELETE FROM users
             WHERE "Id" NOT IN ('{DirecteurId}', '{SecretaireId}', '{SuperAdminId}', '{FinanceId}', '{EnseignantId}');
             """);

        // Encaissements (ticket JGK-F02) : ils référencent l'inscription en Restrict, donc AVANT elle.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM payments;");

        // Sessions de caisse (module Caisse) : payments.CashierSessionId les référence, donc APRÈS la
        // purge des encaissements. Sans cette ligne, la session ouverte par le compte Finance/Directeur
        // dans un test resterait Open pour le suivant — qui se ferait refuser l'ouverture d'une nouvelle
        // session (« déjà une session ouverte », 422) sur ce même compte partagé entre tous les tests.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM cashier_sessions;");

        // Échéanciers personnalisés et lots de relance (Étape 5) : ils référencent inscriptions ET
        // classes en Restrict, donc AVANT elles — les tables enfants (items/échéances) d'abord.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM debtor_reminder_batch_items;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_installments;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM debtor_reminder_batches;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_installment_plans;");

        // Inscriptions (ticket JGK-E01), AVANT les tables qu'elles référencent en Restrict (élèves,
        // classes, années, catégories de frais). Les lignes de frais d'abord : elles pointent l'inscription.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollment_fee_lines;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollments;");

        // Avant les écoles : school_settings les référence en Restrict, et un PUT de test aurait
        // laissé une ligne de réglages sur l'école semée (ticket JGK-B02).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_settings;");

        // Observations du conseil (bulletins, JGK-G03) : référencent élèves ET trimestres en Restrict,
        // donc AVANT l'un ou l'autre — sinon un bulletin annoté par un test laisse une ligne qui bloque
        // la purge des trimestres du test suivant (FK_report_card_remarks_terms_SchoolId_TermId).
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM report_card_remarks;");

        // Notes (ticket JGK-G01), AVANT les tables qu'elles référencent en Restrict (élèves, matières,
        // trimestres) — et les trimestres eux-mêmes AVANT les années scolaires qu'ils référencent.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM grades;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM terms;");

        // Mentions (ticket JGK-G02) : une mention personnalisée créée par un test ferait recevoir au
        // suivant SA liste au lieu des 5 valeurs par défaut (GetMentionsQueryHandler ne renvoie les
        // défauts que si la table est VIDE) — l'ordre d'exécution deviendrait significatif.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM mentions;");

        // Présences (ticket JGK-D06) : student_attendances référence attendance_sheets ET students ;
        // attendance_sheets référence classrooms, subjects et school_years. Donc les lignes élève
        // d'abord, la fiche ensuite, le tout AVANT les tables référencées plus bas.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_attendances;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM attendance_sheets;");

        // Attributions enseignant (ticket JGK-D04) : référence teachers, classrooms, subjects ET
        // school_years en Restrict, donc AVANT chacune d'entre elles.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teacher_assignments;");

        // Idem pour les années scolaires (ticket JGK-C01) — et il ne s'agit pas seulement des écoles
        // créées par les tests : une école n'a droit qu'à UNE année active. Sans cette purge, la
        // première année créée par un test resterait active et le suivant, croyant créer sa première
        // année, obtiendrait une année inactive. L'ordre d'exécution deviendrait significatif.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM school_years;");

        // Enseignants (ticket JGK-D03) : teacher_subjects référence teachers ET subjects en Restrict,
        // donc avant l'un ou l'autre. Sans cette purge, un enseignant laissé par un test fausserait
        // le décompte de la liste du test suivant.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teacher_subjects;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM teachers;");

        // Matières (ticket JGK-C03) : un test qui crée « Maths / Primaire » ferait échouer en 409 le
        // suivant qui croit créer la même matière à neuf.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subjects;");

        // Frais (ticket JGK-F01), dans l'ordre des dépendances : l'historique référence le barème, le
        // barème référence classes et catégories. Le journal est append-only pour le rôle applicatif,
        // mais CE contexte est le propriétaire des tables — il peut le purger.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_change_history;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM class_fees;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM fee_categories;");

        // Élèves et classes : class_fees et students les référencent, donc APRÈS eux. Sans cette
        // purge, une classe laissée par un test fausserait le décompte « appliquer à toutes les
        // classes » (Option 1 de JGK-F01) du test suivant.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM students;");
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM classrooms;");

        // Paiements d'abonnement (ticket JGK-I05) : référencent subscriptions (Restrict), donc AVANT eux.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subscription_payments;");

        // Abonnements créés PAR l'approbation d'une demande (ticket JGK-I03) : ils référencent schools
        // (Restrict), donc AVANT la suppression des écoles. Aucun n'est semé, on peut tout purger.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM subscriptions;");

        await owner.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM schools WHERE "Id" <> '{EcoleId}';""");

        // Remet la fiche de l'école semée à neuf : un PUT /schools/current de test la renomme, et sans
        // cette remise à zéro le test suivant hériterait du nom/adresse modifiés (ordre significatif).
        await owner.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE schools
             SET "Name" = 'École de test', "Address" = NULL, "Phone" = NULL, "LogoUrl" = NULL
             WHERE "Id" = '{EcoleId}';
             """);

        await owner.Database.ExecuteSqlRawAsync(
            """UPDATE users SET "Status" = 'Active', "AccessFailedCount" = 0, "LockoutEndAt" = NULL;""");

        Emails.Clear();
        Payments.Clear();
    }

    /// <summary>
    /// Crée un utilisateur supplémentaire DIRECTEMENT en base (ticket JGK-I04) : contourne l'API à
    /// dessein — un établissement AwaitingPayment ne peut précisément PAS créer de compte via POST
    /// /users (bloqué par SubscriptionAwaitingPaymentMiddleware), c'est ce que ce ticket vérifie. Sert
    /// à prouver que le blocage s'applique à N'IMPORTE QUEL rôle de l'école, pas seulement au Directeur.
    /// </summary>
    public async Task<Guid> CreateAdditionalUserAsync(Guid schoolId, string email, string password, Role role)
    {
        await using var owner = NewOwnerContext();
        var hasher = new IdentityPasswordHasher();

        var user = new User
        {
            SchoolId = schoolId,
            Email = email,
            PasswordHash = hasher.Hash(password),
            FullName = "Utilisateur de test JGK-I04",
            Role = role,
            Status = EntityStatus.Active
        };

        owner.Users.Add(user);
        await owner.SaveChangesAsync(CancellationToken.None);

        return user.Id;
    }

    /// <summary>
    /// Modifie directement le statut d'un abonnement (ticket JGK-I04) : simule une confirmation de
    /// paiement SANS passer par le webhook (utile pour tester I04 indépendamment de I06). Pour tester le
    /// traitement du webhook lui-même, voir ProcessPaymentWebhookEndpointsTests (JGK-I06).
    /// </summary>
    public async Task SetSubscriptionStatusAsync(Guid schoolId, SubscriptionStatus status)
    {
        await using var owner = NewOwnerContext();

        await owner.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE subscriptions SET "Status" = {status.ToString()} WHERE "SchoolId" = {schoolId};""");
    }

    /// <summary>
    /// Lit une ligne SubscriptionPayment directement en base (ticket JGK-I05) : vérifie ce que l'API ne
    /// peut pas encore exposer elle-même (aucun GET par identifiant, JGK-I07 pas encore livré) — que la
    /// transaction de paiement est bien liée à l'abonnement, avec le bon montant et le bon statut.
    /// </summary>
    public async Task<SubscriptionPayment?> GetSubscriptionPaymentAsync(Guid paymentId)
    {
        await using var owner = NewOwnerContext();

        // IgnoreQueryFilters : SubscriptionPayment implémente ITenantEntity, et NewOwnerContext() utilise
        // NoTenantProvider (CurrentSchoolId => null) — sans ceci, le Global Query Filter deviendrait
        // "SchoolId == null" et ne trouverait donc JAMAIS aucune ligne réelle.
        return await owner.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == paymentId);
    }

    /// <summary>
    /// Lit l'abonnement d'une école directement en base (ticket JGK-I06) : vérifie l'activation
    /// (Status, ExpiresAt) déclenchée par le traitement du webhook. IgnoreQueryFilters, même
    /// raisonnement que GetSubscriptionPaymentAsync : Subscription implémente ITenantEntity et
    /// NewOwnerContext() utilise NoTenantProvider (CurrentSchoolId => null) — sans ceci, le Global
    /// Query Filter deviendrait « SchoolId == null » et ne trouverait JAMAIS aucune ligne réelle. La
    /// policy RLS, elle, exempte nativement le rôle PROPRIÉTAIRE utilisé ici.
    /// </summary>
    public async Task<Subscription?> GetSubscriptionAsync(Guid schoolId)
    {
        await using var owner = NewOwnerContext();

        return await owner.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.SchoolId == schoolId);
    }

    /// <summary>
    /// Lit une classe directement en base, IgnoreQueryFilters compris (même raisonnement que
    /// GetSubscriptionPaymentAsync) — seul moyen de vérifier IsDeleted/DeletedAt/DeletedBy après un
    /// DELETE /classrooms/{id} : le Global Query Filter masquerait sinon la ligne archivée, et la RLS
    /// isolerait la requête sur un tenant que NoTenantProvider ne fournit pas.
    /// </summary>
    public async Task<Classroom?> GetClassroomAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Classrooms.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /buildings/{id}.</summary>
    public async Task<Building?> GetBuildingAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Buildings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /students/{id}.</summary>
    public async Task<Student?> GetStudentAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Students.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /subjects/{id}.</summary>
    public async Task<Subject?> GetSubjectAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Subjects.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>Même raisonnement que GetClassroomAsync, pour vérifier le soft delete de DELETE /teachers/{id}.</summary>
    public async Task<Teacher?> GetTeacherAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Teachers.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Même raisonnement que GetClassroomAsync, pour vérifier après DELETE /enrollments/{id} (annulation)
    /// que Status passe bien à Cancelled SANS que IsDeleted ne devienne true — CancelEnrollmentCommand
    /// documente explicitement ce choix (l'historique scolaire doit rester visible, jamais masqué par
    /// le Global Query Filter).
    /// </summary>
    public async Task<Enrollment?> GetEnrollmentAsync(Guid id)
    {
        await using var owner = NewOwnerContext();

        return await owner.Enrollments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// Force un UPDATE PostgreSQL sur une inscription SANS toucher un champ métier visible (aucun
    /// changement de Status, aucun Payment créé) — PostgreSQL attribue malgré tout un nouveau xmin à
    /// toute ligne réécrite (MVCC), même quand aucune valeur ne change réellement.
    ///
    /// Nécessaire UNIQUEMENT pour tester le conflit RowVersion (règle #5) de CancelEnrollmentCommand :
    /// contrairement à Classroom/Subject/Teacher/Student (qui ont chacun leur propre UpdateCommand
    /// anodin pour faire tourner xmin avant un test de conflit), la seule autre écriture connue sur
    /// Enrollment est RecordPayment — qui ferait échouer le Cancel testé pour la MAUVAISE raison (« un
    /// paiement existe déjà », business rule 409) plutôt que pour un jeton simplement périmé.
    /// </summary>
    public async Task TouchEnrollmentRowVersionAsync(Guid enrollmentId)
    {
        await using var owner = NewOwnerContext();

        var enrollment = await owner.Enrollments.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == enrollmentId);

        owner.Entry(enrollment).State = EntityState.Modified;
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Forge un token signé par la MÊME clé que l'API, mais déjà expiré.</summary>
    public string CreateExpiredAccessToken()
    {
        var generator = new JwtTokenGenerator(
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                SigningKey = SigningKey,
                AccessTokenMinutes = -5 // expiré depuis 5 minutes, bien au-delà du ClockSkew de 30 s
            }),
            TimeProvider.System);

        return generator.Generate(DirecteurId, "Directeur de test", EcoleId, Role.Directeur).Value;
    }

    /// <summary>
    /// Sème un jeu de données via le rôle PROPRIÉTAIRE (exempté de RLS), pour les scénarios où passer
    /// par l'API serait disproportionné (ticket JGK-R01 : le tableau de bord agrège élèves, inscriptions,
    /// enseignants, présences et abonnement — reconstituer tout cela par appels HTTP alourdirait le test
    /// sans rien prouver de plus). La LECTURE testée, elle, passe bien par l'API sous RLS.
    /// </summary>
    public async Task SeedAsOwnerAsync(Func<ApplicationDbContext, Task> seed)
    {
        await using var owner = NewOwnerContext();
        await seed(owner);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private ApplicationDbContext NewOwnerContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;

        return new ApplicationDbContext(options, new NoTenantProvider(), NullLogger<ApplicationDbContext>.Instance);
    }

    private async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
