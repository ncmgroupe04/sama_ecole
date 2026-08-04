using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Persistence.Seed;

/// <summary>
/// Comptes et écoles de démonstration — Development/Test UNIQUEMENT, jamais en Production
/// (l'appelant vérifie IWebHostEnvironment, voir SamaEcole.Web/Program.cs).
///
/// SE CONNECTE AVEC LE RÔLE PROPRIÉTAIRE (chaîne « Migrations »), et non avec le rôle applicatif.
/// Ce n'est pas un raccourci : depuis la migration AddAuthentication, `users` est sous policy RLS, et
/// le rôle applicatif n'y a accès QUE par les trois fonctions SECURITY DEFINER du chemin de login.
/// Un INSERT direct depuis l'application échoue donc — « new row violates row-level security policy »
/// — et c'est exactement le comportement voulu. Semer est une opération d'administration, au même
/// titre qu'une migration : elle appartient au propriétaire.
///
/// Ne lit PAS docs/seed-data.json, contrairement à ce que prévoyait le squelette : ce fichier n'est
/// pas exploitable en l'état. Ses identifiants ne sont pas des UUID valides (« u0000001-… »,
/// « s1111111-… » : u et s ne sont pas des chiffres hexadécimaux), et la plupart de ses entités
/// (classes, matières, enseignants, inscriptions) n'ont pas encore de table. On sème donc ici le
/// strict sous-ensemble qui a une entité réelle — en gardant ses écoles, e-mails et rôles, pour que
/// le jour où le fichier deviendra exploitable, rien ne change pour l'utilisateur.
///
/// Idempotent ligne à ligne : relancer l'application ne duplique ni n'écrase rien.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Mot de passe commun aux comptes de démonstration. Respecte la politique du Volume 7 §2
    /// (12 caractères minimum, majuscule, minuscule, chiffre, caractère spécial) : le login vérifie
    /// réellement le hash, un mot de passe trivial ne passerait pas la validation.
    /// </summary>
    public const string DemoPassword = "Motdepasse!Solide2026";

    public static readonly Guid BaobabsId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TerangaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedAsync(
        string ownerConnectionString,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ownerConnectionString) // Npgsql exclusivement — AGENTS.md règle #1.
            .Options;

        // Aucun tenant : ni School ni User n'est une ITenantEntity, aucun Global Query Filter ne
        // s'applique donc à ce qui est semé ici.
        await using var dbContext = new ApplicationDbContext(options, new NoTenantProvider(), NullLogger<ApplicationDbContext>.Instance);

        // Deux écoles, et pas une : c'est ce qui permet de constater à la main que l'isolation
        // multi-tenant fonctionne, sans avoir à monter un test.
        var schools = new[]
        {
            new School
            {
                Id = BaobabsId,
                Name = "École Primaire Les Baobabs",
                Address = "Rue 12, Médina, Dakar",
                Phone = "+221771234567",
                Status = EntityStatus.Active,
                InspectionAcademie = "Dakar",
                InspectionEducationFormation = "Dakar-Médina",
                NomLycee = "Les Baobabs"
            },
            new School
            {
                Id = TerangaId,
                Name = "Lycée Moderne Teranga",
                Address = "Avenue Bourguiba, Dakar",
                Phone = "+221779876543",
                Status = EntityStatus.Active,
                InspectionAcademie = "Dakar",
                InspectionEducationFormation = "Dakar-Plateau",
                NomLycee = "Teranga"
            }
        };

        foreach (var school in schools)
        {
            var existing = await dbContext.Schools.FirstOrDefaultAsync(s => s.Id == school.Id, cancellationToken);

            if (existing is null)
            {
                dbContext.Schools.Add(school);
            }
            else if (existing.InspectionAcademie is null
                     && existing.InspectionEducationFormation is null
                     && existing.NomLycee is null)
            {
                // Rattrapage : école semée AVANT la migration AddSchoolAcademicInfo. On ne comble que
                // si les TROIS champs sont vides — une valeur posée à la main par un Directeur ne se
                // fait jamais écraser par un redémarrage.
                existing.InspectionAcademie = school.InspectionAcademie;
                existing.InspectionEducationFormation = school.InspectionEducationFormation;
                existing.NomLycee = school.NomLycee;
            }
        }

        // Hash calculé une seule fois : PBKDF2 est volontairement lent, le refaire par compte
        // rallongerait le démarrage sans rien apporter.
        var passwordHash = passwordHasher.Hash(DemoPassword);

        var users = new[]
        {
            NewUser("directrice@baobabs.sn", "Fatou Ndiaye", Role.Directeur, BaobabsId, passwordHash),
            NewUser("secretariat@baobabs.sn", "Moussa Diop", Role.Secretariat, BaobabsId, passwordHash),
            NewUser("finance@baobabs.sn", "Aissatou Ba", Role.Finance, BaobabsId, passwordHash),
            NewUser("enseignant@baobabs.sn", "Ibrahima Sarr", Role.Enseignant, BaobabsId, passwordHash),
            NewUser("surveillant@baobabs.sn", "Cheikh Mbaye", Role.Surveillant, BaobabsId, passwordHash),

            // SchoolId null : le Super Admin n'appartient à aucun établissement (voir User.SchoolId).
            NewUser("superadmin@sama-ecole.sn", "Super Admin Unikol", Role.SuperAdmin, null, passwordHash)
        };

        foreach (var user in users)
        {
            if (!await dbContext.Users.AnyAsync(u => u.Email == user.Email, cancellationToken))
            {
                dbContext.Users.Add(user);
            }
        }

        // Classes des Baobabs (ticket JGK-C02). Sans elles, le formulaire de création d'élève
        // s'ouvre sur un sélecteur de classes vide : rien ne serait créable au premier démarrage.
        //
        // Les ÉLÈVES, eux, ne sont volontairement pas semés : leurs matricules seraient posés en dur
        // sans incrémenter matricule_sequences, et le premier élève créé depuis l'interface
        // réclamerait un numéro déjà pris — violation d'unicité, pour un jeu de démonstration.
        // La liste démarre donc vide, et le premier élève passe par le vrai parcours d'inscription.
        // Cycle DÉRIVÉ du niveau via ClassroomCycle.CycleFor, exactement comme le font
        // CreateClassroomCommandHandler et UpdateClassroomCommandHandler. Le poser en dur ici, ou
        // l'omettre, laisserait ces classes sur le défaut College de l'entité : « Primaire » sur le
        // papier, mais notées sur /20 avec des coefficients (voir la remarque de ClassroomCycle).
        // C'est précisément ce qui s'était produit — la migration BackfillClassroomCycleFromLevel a
        // réparé les lignes existantes, mais le seeder recréait le défaut sur toute base neuve.
        var classrooms = new[]
        {
            NewClassroom("c0000001-0000-0000-0000-000000000001", "CM2 A", "Primaire", 40),
            NewClassroom("c0000002-0000-0000-0000-000000000002", "CI B", "Primaire", 35)
        };

        foreach (var classroom in classrooms)
        {
            // IgnoreQueryFilters : Classroom est une ITenantEntity, son Global Query Filter compare
            // SchoolId au tenant courant — or le seeder n'en a AUCUN. Sans cela, le test d'existence
            // répondrait « absente » à chaque démarrage, et le second relancerait l'insertion pour se
            // heurter à l'index unique. Le rôle propriétaire, lui, voit bien la ligne côté RLS.
            var existing = await dbContext.Classrooms
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == classroom.Id, cancellationToken);

            if (existing is null)
            {
                dbContext.Classrooms.Add(classroom);
            }
            else if (existing.Cycle != classroom.Cycle)
            {
                // Rattrapage des bases de démonstration semées AVANT que le cycle ne soit dérivé ici :
                // la migration de backfill ne repassera pas (elle a déjà tourné), et ces lignes
                // resteraient sur College — donc en /20 — pour toujours. Limité aux deux classes de
                // démonstration, reconnues par leur identifiant fixe : le seeder ne touche à aucune
                // classe créée par l'utilisateur.
                existing.Cycle = classroom.Cycle;
            }
        }

        // Abonnements de démonstration — indispensables depuis le contrôle d'accès par formule
        // ([RequireFeature]) : FeatureAuthorizationHandler REFUSE par défaut une école sans
        // abonnement, et le développement se retrouverait privé de SMS et de rapports avancés. Les
        // deux écoles sont donc dotées d'une formule Premium, active un an.
        //
        // Actives (et non AwaitingPayment) : sans quoi SubscriptionAwaitingPaymentMiddleware
        // renverrait 403 sur TOUTE l'API dès la connexion à un compte de démonstration.
        foreach (var schoolId in new[] { BaobabsId, TerangaId })
        {
            var hasSubscription = await dbContext.Subscriptions
                .IgnoreQueryFilters()
                .AnyAsync(s => s.SchoolId == schoolId, cancellationToken);

            if (!hasSubscription)
            {
                dbContext.Subscriptions.Add(new Subscription
                {
                    SchoolId = schoolId,
                    Plan = SubscriptionPlan.Premium,
                    Status = SubscriptionStatus.Active,
                    ExpiresAt = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static User NewUser(string email, string fullName, Role role, Guid? schoolId, string passwordHash) =>
        new()
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            Email = email,
            FullName = fullName,
            Role = role,
            PasswordHash = passwordHash,
            Status = EntityStatus.Active
        };

    /// <summary>
    /// Classe de démonstration à identifiant FIXE (rejouable d'un démarrage à l'autre), dont le cycle
    /// est dérivé du niveau — jamais saisi à part. Voir <see cref="ClassroomCycle"/>.
    /// </summary>
    private static Classroom NewClassroom(string id, string name, string level, int capacity) =>
        new()
        {
            Id = Guid.Parse(id),
            SchoolId = BaobabsId,
            Name = name,
            Level = level,
            Cycle = ClassroomCycle.CycleFor(level),
            Capacity = capacity
        };

    private sealed class NoTenantProvider : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
