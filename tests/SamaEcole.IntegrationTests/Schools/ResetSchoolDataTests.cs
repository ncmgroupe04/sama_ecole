using System.Text.RegularExpressions;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Schools;

/// <summary>
/// « Zone de danger » — réinitialisation des données d'essai d'un établissement.
///
/// C'est la seule opération du produit qui efface PHYSIQUEMENT des données métier (exception assumée
/// à la règle #6 d'AGENTS.md, voir IResetSchoolDataService). Quatre choses doivent donc être prouvées
/// sur une VRAIE base PostgreSQL, jamais sur un double en mémoire :
///
///   1. la purge s'arrête à la frontière du tenant — une école voisine ne perd rien ;
///   2. elle atteint AUSSI les lignes en suppression logique, sans quoi l'école ne serait pas « à neuf » ;
///   3. elle épargne ce qui a été CONFIGURÉ (utilisateurs, réglages, années, classes, matières, patrimoine) ;
///   4. elle remet les compteurs de matricules à zéro, sinon l'école garde la mémoire de ses essais ;
///   5. elle emporte les enfants de l'élève ajoutés par les modules livrés APRÈS elle (dossiers
///      d'examen, certificats de mutation, prêts de matériel) — chacun porte une FK ON DELETE
///      RESTRICT vers students, et sans leur purge la « remise à neuf » échoue en 23503 (bug corrigé
///      par la migration FixResetSchoolDataMissingChildTables). Un test générique le garantit pour
///      tout module futur.
///
/// La purge tourne ici avec le rôle applicatif bridé, exactement comme au runtime — un rôle qui n'a
/// AUCUN droit de DELETE sur ces tables. Elle passe donc par la fonction SECURITY DEFINER
/// reset_school_data (migration AddSchoolDataReset), qui s'exécute avec les droits du propriétaire et
/// se trouve de ce fait exemptée de RLS : c'est sa garde interne, et non les policies, qui borde
/// l'opération. D'où les deux tests de refus ci-dessous, qui la mettent à l'épreuve.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ResetSchoolDataTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        Seed(owner, EcoleA);
        Seed(owner, EcoleB);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>
    /// Une école « après une phase d'essai » : ce qui a été SAISI (élèves, inscriptions, paiements,
    /// notes…) comme ce qui a été PARAMÉTRÉ (classes, matières, enseignants, barème, inventaire, paie,
    /// comptes du personnel) — tout cela part depuis le 15/09/2026. Et ce qui doit SURVIVRE : le
    /// compte Directeur, la fiche et les réglages de l'école, les années et trimestres, les mentions,
    /// les bâtiments et salles, le journal d'audit.
    /// </summary>
    private static void Seed(ApplicationDbContext owner, Guid schoolId)
    {
        var classroomId = Guid.NewGuid();
        var schoolYearId = Guid.NewGuid();
        var termId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var deletedStudentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        var directeurId = Guid.NewGuid();
        var secretaireId = Guid.NewGuid();
        var enseignantUserId = Guid.NewGuid();

        owner.Users.Add(new User
        {
            Id = directeurId,
            SchoolId = schoolId,
            Email = $"directeur-{schoolId:N}@example.sn",
            PasswordHash = "hash",
            FullName = "Le Directeur",
            Role = Role.Directeur
        });

        // Personnel : deux comptes non-Directeur, supprimés par la purge.
        owner.Users.AddRange(
            new User
            {
                Id = secretaireId,
                SchoolId = schoolId,
                Email = $"secretaire-{schoolId:N}@example.sn",
                PasswordHash = "hash",
                FullName = "La Secrétaire",
                Role = Role.Secretariat
            },
            new User
            {
                Id = enseignantUserId,
                SchoolId = schoolId,
                Email = $"enseignant-{schoolId:N}@example.sn",
                PasswordHash = "hash",
                FullName = "L'Enseignant",
                Role = Role.Enseignant
            });

        // Jetons et historique pendus à ces comptes : sans leur purge, la suppression des comptes
        // échouerait en violation de clé étrangère (23503).
        owner.Set<RefreshToken>().Add(new RefreshToken
        {
            UserId = secretaireId,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });

        owner.Set<UserStatusHistory>().Add(new UserStatusHistory
        {
            SchoolId = schoolId,
            UserId = secretaireId,
            PreviousStatus = EntityStatus.Active,
            NewStatus = EntityStatus.Suspended,
            Reason = "Test",
            ChangedByUserId = directeurId,
            ChangedAt = DateTimeOffset.UtcNow
        });

        // Journal d'audit : CONSERVÉ. Celle du Directeur garde son auteur, celle de la secrétaire
        // doit se retrouver DÉTACHÉE (UserId à NULL) — l'action reste tracée, le compte non.
        owner.AuditLogs.AddRange(
            new AuditLog
            {
                SchoolId = schoolId, UserId = directeurId, Module = "Schools", Action = "GoLive",
                Success = true, OccurredAt = DateTimeOffset.UtcNow
            },
            new AuditLog
            {
                SchoolId = schoolId, UserId = secretaireId, Module = "Finance", Action = "RecordPayment",
                Success = true, OccurredAt = DateTimeOffset.UtcNow
            });

        // Mentions, bâtiments et salles : réglages de l'établissement, CONSERVÉS par la purge.
        owner.Set<Mention>().Add(new Mention { SchoolId = schoolId, Label = "Très bien", MinAverage = 16m });

        var buildingId = Guid.NewGuid();
        owner.Set<Building>().Add(new Building { Id = buildingId, SchoolId = schoolId, Name = "Bâtiment A" });
        owner.Set<Room>().Add(new Room
        {
            SchoolId = schoolId, BuildingId = buildingId, Name = "Salle 1", Capacity = 40
        });

        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = schoolId, Name = "CM2", Level = "Primaire", Capacity = 40
        });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = schoolYearId, SchoolId = schoolId, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });

        owner.Terms.Add(new Term
        {
            Id = termId, SchoolId = schoolId, SchoolYearId = schoolYearId, Label = "Trimestre 1",
            Order = 1, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
        });

        owner.Subjects.Add(new Subject
        {
            Id = subjectId, SchoolId = schoolId, Name = "Mathématiques", Level = "Primaire", Coefficient = 4
        });

        // Sous-matière : subjects.ParentSubjectId est une FK ON DELETE RESTRICT vers subjects
        // elle-même. Sans la mise à NULL que fait la fonction avant sa boucle, la purge des matières
        // buterait sur cette ligne.
        owner.Subjects.Add(new Subject
        {
            SchoolId = schoolId, Name = "Géométrie", Level = "Primaire", Coefficient = 2,
            ParentSubjectId = subjectId
        });

        // Enseignant, son compte, ses matières, son affectation et son créneau : chacun porte une FK
        // vers teachers, classrooms ou subjects, et doit donc partir AVANT eux.
        var teacherId = Guid.NewGuid();

        owner.Teachers.Add(new Teacher
        {
            Id = teacherId, SchoolId = schoolId, Matricule = "ENS-2026-0001", FullName = "L'Enseignant",
            Email = $"enseignant-{schoolId:N}@example.sn", BirthDate = new DateOnly(1990, 1, 1),
            UserId = enseignantUserId
        });

        owner.Set<TeacherSubject>().Add(new TeacherSubject
        {
            SchoolId = schoolId, TeacherId = teacherId, SubjectId = subjectId
        });

        owner.Set<TeacherAssignment>().Add(new TeacherAssignment
        {
            SchoolId = schoolId, TeacherId = teacherId, ClassroomId = classroomId,
            SubjectId = subjectId, SchoolYearId = schoolYearId
        });

        owner.Set<ScheduleSlot>().Add(new ScheduleSlot
        {
            SchoolId = schoolId, TeacherId = teacherId, ClassroomId = classroomId, SubjectId = subjectId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
        });

        // Paie : fiche_paies et employee_contract_histories portent une FK RESTRICT vers le contrat.
        var contractId = Guid.NewGuid();

        owner.Set<EmployeeContract>().Add(new EmployeeContract
        {
            Id = contractId, SchoolId = schoolId, TeacherId = teacherId, Type = ContractType.Permanent,
            BaseSalary = 200_000m
        });

        owner.Set<FichePaie>().Add(new FichePaie
        {
            SchoolId = schoolId, EmployeeContractId = contractId, Month = 10, Year = 2026,
            GrossSalary = 200_000m, NetSalary = 180_000m
        });

        // Trésorerie & fiscalité.
        owner.Set<TaxeDeclaration>().Add(new TaxeDeclaration
        {
            SchoolId = schoolId, Month = 10, Year = 2026, TotalDueToState = 45_000m
        });

        // Barème des frais : fee_change_history → class_fees → fee_categories, et payment_breakdowns
        // (déjà purgé) référence fee_categories.
        var feeCategoryId = Guid.NewGuid();

        owner.Set<FeeCategory>().Add(new FeeCategory
        {
            Id = feeCategoryId, SchoolId = schoolId, Name = "Mensualité", IsRecurring = true
        });

        owner.Set<ClassFee>().Add(new ClassFee
        {
            SchoolId = schoolId, FeeCategoryId = feeCategoryId, ClassroomId = classroomId, Amount = 15_000m
        });

        // Élève déjà « supprimé » depuis l'interface : invisible à l'écran, bien présent en base.
        var deletedStudent = new Student
        {
            Id = deletedStudentId, SchoolId = schoolId, Matricule = "ELEV-2026-0002", FullName = "Eleve retire",
            BirthDate = new DateOnly(2015, 5, 1), BirthPlace = "Thiès", Gender = "M", ClassroomId = classroomId
        };
        deletedStudent.SoftDelete("test");

        owner.Students.AddRange(
            new Student
            {
                Id = studentId, SchoolId = schoolId, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
            },
            deletedStudent);

        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = schoolId, StudentId = studentId, SchoolYearId = schoolYearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 150_000m, AmountPaid = 50_000m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        owner.Payments.Add(new Payment
        {
            SchoolId = schoolId, EnrollmentId = enrollmentId, Amount = 50_000m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Partial, BalanceAfter = 100_000m,
            ReceiptNumber = "PAY-2026-0001", ReceivedByUserId = Guid.NewGuid(), PaidAt = DateTimeOffset.UtcNow
        });

        owner.Grades.Add(new Grade
        {
            SchoolId = schoolId, StudentId = studentId, SubjectId = subjectId, TermId = termId,
            EvaluationType = EvaluationType.Devoir1, Value = 14.5m
        });

        // Modules livrés APRÈS reset_school_data (Examens, Intégration étatique, Inventaire) : chacun
        // rattache une ligne à l'élève par une FK ON DELETE RESTRICT. Sans leur purge, DELETE FROM
        // students lève une violation de clé étrangère et toute la « remise à neuf » est annulée.
        var examSessionId = Guid.NewGuid();
        var examDossierId = Guid.NewGuid();

        owner.Set<ExamSession>().Add(new ExamSession
        {
            Id = examSessionId, SchoolId = schoolId, SchoolYearId = schoolYearId,
            ExamType = ExamType.CFEE, Status = ExamSessionStatus.EnPreparation
        });

        owner.Set<ExamDossier>().Add(new ExamDossier
        {
            Id = examDossierId, SchoolId = schoolId, ExamSessionId = examSessionId,
            StudentId = studentId, ClassroomId = classroomId, Status = ExamDossierStatus.Incomplet
        });

        owner.Set<ExamResult>().Add(new ExamResult
        {
            SchoolId = schoolId, ExamDossierId = examDossierId, IsAdmitted = true,
            AverageScore = 12.5m, DeliberatedOn = new DateOnly(2027, 7, 5)
        });

        owner.Set<StudentMutationCertificate>().Add(new StudentMutationCertificate
        {
            SchoolId = schoolId, StudentId = studentId, SchoolYearId = schoolYearId,
            CertificateNumber = "MUT-2026-0001",
            // Unique GLOBAL (pas par école) : Seed() tourne pour EcoleA et EcoleB, d'où le SchoolId.
            VerificationCode = schoolId.ToString("N").ToUpperInvariant(),
            ClassroomNameSnapshot = "CM2", Reason = StudentMutationReason.Demenagement,
            IssuedOn = new DateOnly(2027, 6, 1), WasFinanciallyClear = true
        });

        // Patrimoine (catégorie + lot + journal de stock) : purgé depuis le 15/09/2026 — un inventaire
        // fictif n'a pas plus sa place dans une école remise à neuf qu'un élève fictif.
        var inventoryCategoryId = Guid.NewGuid();
        var inventoryItemId = Guid.NewGuid();

        owner.Set<InventoryCategory>().Add(new InventoryCategory
        {
            Id = inventoryCategoryId, SchoolId = schoolId, Name = "Manuels scolaires"
        });

        owner.Set<InventoryItem>().Add(new InventoryItem
        {
            Id = inventoryItemId, SchoolId = schoolId, CategoryId = inventoryCategoryId,
            Name = "Manuel de lecture CM2", QuantityTotal = 30, QuantityAvailable = 29,
            Condition = ItemCondition.Bon
        });

        owner.Set<ItemAssignment>().Add(new ItemAssignment
        {
            SchoolId = schoolId, ItemId = inventoryItemId, Quantity = 1,
            BeneficiaryType = AssignmentBeneficiaryType.Eleve, StudentId = studentId,
            BeneficiaryLabel = "Awa Fall", AssignedOn = new DateOnly(2026, 10, 5),
            Status = AssignmentStatus.EnCours
        });

        // stock_movements est APPEND-ONLY pour le rôle applicatif (SELECT, INSERT seulement) : la
        // fonction SECURITY DEFINER est la seule voie qui l'efface, et ce test le prouve.
        owner.Set<StockMovement>().Add(new StockMovement
        {
            SchoolId = schoolId, ItemId = inventoryItemId, Type = StockMovementType.Entree,
            Quantity = 30, MovementDate = new DateOnly(2026, 10, 1), Reason = "Dotation initiale",
            QuantityTotalAfter = 30, QuantityAvailableAfter = 30
        });

        owner.Set<MatriculeSequence>().Add(new MatriculeSequence
        {
            SchoolId = schoolId, Kind = MatriculeKind.Student, Year = 2026, LastValue = 42
        });
    }

    private async Task PurgeEcoleAAsync()
    {
        await using var appA = _db.NewAppContext(EcoleA);
        await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleA, CancellationToken.None);
    }

    [Fact]
    public async Task Purging_One_School_Must_Leave_The_Other_School_Untouched()
    {
        await PurgeEcoleAAsync();

        // Le contexte propriétaire ignore la RLS : il voit les DEUX écoles, donc il peut prouver que
        // l'une a été vidée pendant que l'autre restait intacte.
        await using var owner = _db.NewOwnerContext();

        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Payments.IgnoreQueryFilters().CountAsync(p => p.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ExamDossier>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ExamResult>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ExamSession>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<StudentMutationCertificate>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ItemAssignment>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleA)).Should().Be(0);

        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleB)).Should().Be(2,
            "purger une école ne doit jamais entamer les données d'un autre établissement");
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Payments.IgnoreQueryFilters().CountAsync(p => p.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Set<ExamDossier>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Set<StudentMutationCertificate>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Set<ItemAssignment>().IgnoreQueryFilters().CountAsync(x => x.SchoolId == EcoleB)).Should().Be(1);
    }

    [Fact]
    public async Task Soft_Deleted_Rows_Must_Be_Purged_Too()
    {
        await PurgeEcoleAAsync();

        // Le Global Query Filter d'EF Core combine tenant ET soft delete. Le DELETE partant du SQL,
        // il n'en tient pas compte — et c'est voulu : filtré, l'élève retiré aurait survécu à la
        // « remise à neuf », invisible mais bien là, son matricule toujours pris.
        await using var owner = _db.NewOwnerContext();

        var remaining = await owner.Students
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == EcoleA)
            .ToListAsync();

        remaining.Should().BeEmpty("une école « à neuf » ne garde aucune ligne, pas même supprimée logiquement");
    }

    [Fact]
    public async Task The_Identity_Of_The_School_Must_Survive_The_Purge()
    {
        await PurgeEcoleAAsync();

        await using var owner = _db.NewOwnerContext();

        (await owner.Schools.CountAsync(s => s.Id == EcoleA)).Should().Be(1);
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.SchoolId == EcoleA)).Should().Be(1,
            "l'année scolaire active est conservée : c'est le cadre de l'établissement, pas une donnée d'essai");
        (await owner.Terms.IgnoreQueryFilters().CountAsync(t => t.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Set<Mention>().IgnoreQueryFilters().CountAsync(m => m.SchoolId == EcoleA)).Should().Be(1,
            "les mentions sont un réglage de notation, pas une saisie");
        (await owner.Set<Building>().IgnoreQueryFilters().CountAsync(b => b.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Set<Room>().IgnoreQueryFilters().CountAsync(r => r.SchoolId == EcoleA)).Should().Be(1);
    }

    /// <summary>
    /// Le périmètre élargi du 15/09/2026 : la configuration MÉTIER part elle aussi. Une école « à
    /// neuf » ne garde ni classe, ni matière, ni enseignant, ni barème, ni inventaire, ni paie fictifs.
    /// </summary>
    [Fact]
    public async Task The_Business_Configuration_Must_Be_Purged_Too()
    {
        await PurgeEcoleAAsync();

        await using var owner = _db.NewOwnerContext();

        (await owner.Classrooms.IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Subjects.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(0,
            "les matières partent AVEC leur sous-matière : la FK auto-référencée ne doit pas bloquer la purge");
        (await owner.Teachers.IgnoreQueryFilters().CountAsync(t => t.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<TeacherSubject>().IgnoreQueryFilters().CountAsync(t => t.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<TeacherAssignment>().IgnoreQueryFilters().CountAsync(a => a.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ScheduleSlot>().IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<EmployeeContract>().IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<FichePaie>().IgnoreQueryFilters().CountAsync(f => f.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<TaxeDeclaration>().IgnoreQueryFilters().CountAsync(d => d.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<FeeCategory>().IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<ClassFee>().IgnoreQueryFilters().CountAsync(f => f.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<InventoryCategory>().IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<InventoryItem>().IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(0);
        (await owner.Set<StockMovement>().IgnoreQueryFilters().CountAsync(m => m.SchoolId == EcoleA)).Should().Be(0,
            "stock_movements est append-only pour l'application : seule la fonction SECURITY DEFINER l'efface");

        // Et l'école voisine n'a rien perdu de sa propre configuration.
        (await owner.Classrooms.IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Teachers.IgnoreQueryFilters().CountAsync(t => t.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Set<InventoryItem>().IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleB)).Should().Be(1);
    }

    /// <summary>
    /// Les comptes du personnel sont SUPPRIMÉS (arbitrage du 15/09/2026), le Directeur reste — il doit
    /// pouvoir se reconnecter —, et le journal d'audit n'est pas purgé : les entrées des comptes
    /// disparus sont DÉTACHÉES (UserId à NULL), jamais effacées.
    /// </summary>
    [Fact]
    public async Task Staff_Accounts_Must_Be_Deleted_And_Their_Audit_Trail_Detached()
    {
        await PurgeEcoleAAsync();

        await using var owner = _db.NewOwnerContext();

        var remaining = await owner.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == EcoleA)
            .ToListAsync();

        remaining.Should().ContainSingle().Which.Role.Should().Be(Role.Directeur,
            "le Directeur doit pouvoir se reconnecter après avoir remis son école à neuf");

        (await owner.Set<RefreshToken>().IgnoreQueryFilters().CountAsync()).Should().Be(1,
            "les sessions du personnel supprimé partent ; celle de l'École B reste");
        (await owner.Set<UserStatusHistory>().IgnoreQueryFilters().CountAsync(h => h.SchoolId == EcoleA)).Should().Be(0);

        var logs = await owner.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.SchoolId == EcoleA)
            .ToListAsync();

        logs.Should().HaveCount(2, "le journal d'audit n'est JAMAIS purgé");
        logs.Should().ContainSingle(a => a.Action == "RecordPayment" && a.UserId == null,
            "l'entrée d'un compte supprimé est détachée, pas effacée");
        logs.Should().ContainSingle(a => a.Action == "GoLive" && a.UserId != null,
            "celle du Directeur garde son auteur");

        // Et le personnel de l'école voisine est intact.
        (await owner.Users.IgnoreQueryFilters().CountAsync(u => u.SchoolId == EcoleB)).Should().Be(3);
    }

    /// <summary>
    /// Le personnel déjà connecté garde un jeton d'accès valide jusqu'à 15 minutes après la purge
    /// (Auth.AccessTokenMinutes) : sa première action journalisée viserait alors un compte disparu.
    /// L'écriture d'audit doit se DÉTACHER plutôt que d'échouer — sans quoi une violation de clé
    /// étrangère ferait perdre à la fois l'action et sa trace (migration AllowAuditLogsWithoutActor).
    /// </summary>
    [Fact]
    public async Task An_Action_By_An_Account_Deleted_Mid_Session_Must_Still_Be_Journalised()
    {
        Guid secretaireId;

        await using (var before = _db.NewOwnerContext())
        {
            secretaireId = await before.Users.IgnoreQueryFilters()
                .Where(u => u.SchoolId == EcoleA && u.Role == Role.Secretariat)
                .Select(u => u.Id)
                .SingleAsync();
        }

        await PurgeEcoleAAsync();

        await using (var app = _db.NewAppContext(EcoleA))
        {
            await app.Database.OpenConnectionAsync();

            await using var command = app.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT append_audit_log($1, $2, 'Finance', 'RecordPayment', true, NULL, NULL, NOW())";

            foreach (var value in new object[] { EcoleA, secretaireId })
            {
                var parameter = command.CreateParameter();
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            await command.ExecuteNonQueryAsync();
        }

        await using var owner = _db.NewOwnerContext();

        var written = await owner.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.SchoolId == EcoleA && a.Action == "RecordPayment")
            .ToListAsync();

        written.Should().HaveCount(2, "l'entrée d'avant la purge est détachée, celle d'après est ajoutée");
        written.Should().OnlyContain(a => a.UserId == null,
            "un compte supprimé ne peut plus être l'auteur de rien — mais l'action reste tracée");
    }

    /// <summary>
    /// Un compte rattaché AUSSI à un autre établissement (groupe scolaire, table user_schools) n'est
    /// jamais supprimé : la purge d'une école ne doit rien retirer à une autre — qui peut être, elle,
    /// en mode réel. Seul son rattachement à l'école purgée disparaît.
    /// </summary>
    [Fact]
    public async Task A_Staff_Account_Shared_With_Another_School_Must_Survive()
    {
        var partageId = Guid.NewGuid();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Users.Add(new User
            {
                Id = partageId,
                SchoolId = EcoleA,
                Email = "comptable-groupe@example.sn",
                PasswordHash = "hash",
                FullName = "Comptable du groupe",
                Role = Role.Finance
            });

            owner.UserSchools.Add(new UserSchool { UserId = partageId, SchoolId = EcoleB });

            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await PurgeEcoleAAsync();

        await using var check = _db.NewOwnerContext();

        (await check.Users.IgnoreQueryFilters().CountAsync(u => u.Id == partageId)).Should().Be(1,
            "supprimer ce compte retirerait un utilisateur à l'École B, qui n'a rien demandé");
        (await check.UserSchools.IgnoreQueryFilters().CountAsync(us => us.UserId == partageId)).Should().Be(1,
            "son rattachement à l'École B est conservé");
    }

    [Fact]
    public async Task Matricule_Counters_Must_Restart_From_One_After_A_Purge()
    {
        await PurgeEcoleAAsync();

        // Sans purge des compteurs, le premier élève recréé porterait ELEV-2026-0043 : l'école
        // paraîtrait neuve tout en gardant la mémoire de ses essais.
        await using var appAfter = _db.NewAppContext(EcoleA);

        var matricule = await _db.NewGenerator(appAfter)
            .GenerateNextStudentMatriculeAsync(EcoleA, CancellationToken.None);

        matricule.Should().EndWith("0001");
    }

    [Fact]
    public async Task The_Summary_Must_Report_What_Was_Actually_Deleted()
    {
        await using var appA = _db.NewAppContext(EcoleA);

        var summary = await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleA, CancellationToken.None);

        // Le compte rendu est ce que le Directeur LIT après la purge : chaque domaine doit y figurer
        // avec son vrai nombre de lignes. Assertions par LIBELLÉ plutôt que sur le total : un total en
        // dur se périme à chaque ligne ajoutée au jeu d'essai, sans rien prouver de plus.
        summary.Entries.Should().Contain(e => e.Label == "Élèves" && e.RowsDeleted == 2);
        summary.Entries.Should().Contain(e => e.Label == "Paiements" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Dossiers de candidature aux examens" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Certificats de mutation" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Affectations de matériel" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Classes et niveaux" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Matières" && e.RowsDeleted == 2);
        summary.Entries.Should().Contain(e => e.Label == "Enseignants" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Comptes du personnel" && e.RowsDeleted == 2);
        summary.TotalRowsDeleted.Should().Be(summary.Entries.Sum(e => e.RowsDeleted));
    }

    [Fact]
    public async Task Purging_Another_School_Than_The_Session_Must_Be_Refused_By_The_Database()
    {
        // LE test de sécurité de cette fonctionnalité. reset_school_data est SECURITY DEFINER : elle
        // s'exécute avec les droits du propriétaire des tables, que PostgreSQL exempte de RLS. Sans sa
        // garde interne, une session de l'École A pourrait donc vider l'École B — c'est la seule
        // opération du produit où l'isolation ne repose pas sur les policies.
        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleB, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        // Et rien n'a bougé, ni chez la cible visée ni chez l'appelante.
        await using var owner = _db.NewOwnerContext();

        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleB)).Should().Be(2);
        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(2);
    }

    [Fact]
    public async Task A_Session_Without_Tenant_Must_Not_Be_Able_To_Purge_Anything()
    {
        // Une session sans app.current_school_id (tâche de fond, appel non authentifié) ne doit pas
        // pouvoir purger « au choix » : la fonction échoue en FERMETURE, comme les policies RLS.
        await using var appWithoutTenant = _db.NewAppContext(schoolId: null);

        var act = async () => await _db.NewResetSchoolDataService(appWithoutTenant)
            .ResetAsync(EcoleA, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        await using var owner = _db.NewOwnerContext();
        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(2);
    }

    [Fact]
    public async Task A_Second_Purge_Must_Be_Harmless()
    {
        await using var appA = _db.NewAppContext(EcoleA);
        var service = _db.NewResetSchoolDataService(appA);

        await service.ResetAsync(EcoleA, CancellationToken.None);
        var second = await service.ResetAsync(EcoleA, CancellationToken.None);

        second.TotalRowsDeleted.Should().Be(0,
            "un double clic, ou un Directeur qui recommence, ne doit produire ni erreur ni effet de bord");
    }

    /// <summary>Passe une école en mode réel, comme le ferait GoLiveCommand — verrou permanent inclus.</summary>
    private async Task MarkAsLiveAsync(Guid schoolId)
    {
        await using var owner = _db.NewOwnerContext();

        var school = await owner.Schools.IgnoreQueryFilters().SingleAsync(s => s.Id == schoolId);
        school.WentLiveAt = new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero);
        school.HasEverGoneLive = true;

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Purging_A_School_In_Live_Mode_Must_Be_Refused_By_The_Database_Itself()
    {
        // Le contrôle du mode réel existe AUSSI dans ResetSchoolDataCommandHandler. Ce test prouve
        // qu'il ne repose plus sur lui SEUL : on appelle le service directement, en court-circuitant
        // le Handler exactement comme le ferait un futur second appelant (commande d'administration,
        // tâche de reprise, script) qui aurait oublié de rejouer la règle. La base refuse d'elle-même.
        await MarkAsLiveAsync(EcoleA);

        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleA, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.RaiseException);

        thrown.Which.MessageText.Should().Contain("RESET_UNAVAILABLE_LIVE_MODE",
            "le jeton d'erreur doit être le MÊME des deux côtés — c'est celui que l'API renvoie déjà au client");

        // Et surtout : pas une ligne effacée. La fonction échoue AVANT sa boucle de suppression.
        await using var owner = _db.NewOwnerContext();

        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(2);
        (await owner.Payments.IgnoreQueryFilters().CountAsync(p => p.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == EcoleA)).Should().Be(1);
    }

    [Fact]
    public async Task The_Tenant_Guard_Must_Take_Precedence_Over_The_Live_Mode_Guard()
    {
        // Ordre des gardes dans la fonction : tenant D'ABORD, état métier ENSUITE. Sans cet ordre, une
        // session de l'École A qui vise l'École B apprendrait, à la seule lecture du message d'erreur,
        // si B est passée en mode réel — une information sur un établissement qui ne la regarde pas.
        await MarkAsLiveAsync(EcoleB);

        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleB, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        thrown.Which.MessageText.Should().NotContain("RESET_UNAVAILABLE_LIVE_MODE",
            "le refus doit porter sur le tenant, sans rien divulguer de l'état de l'école visée");
    }

    [Fact]
    public async Task Reverting_To_Test_Mode_Must_Not_Reopen_The_Purge()
    {
        // Le garde lit désormais HasEverGoneLive (verrou PERMANENT), pas WentLiveAt (l'affichage
        // courant, que RevertToTestCommand remet à null pour rejouer la bascule vers le mode réel).
        // Sans ce test, un garde qui reviendrait sur WentLiveAt passerait inaperçu — et un Directeur
        // pourrait rouvrir la purge de données devenues comptables en repassant en mode test
        // (AGENTS.md règle #6), exactement ce que ce verrou existe pour empêcher.
        await MarkAsLiveAsync(EcoleA);

        await using (var owner = _db.NewOwnerContext())
        {
            var school = await owner.Schools.IgnoreQueryFilters().SingleAsync(s => s.Id == EcoleA);
            school.WentLiveAt = null;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleA, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.RaiseException);

        thrown.Which.MessageText.Should().Contain("RESET_UNAVAILABLE_LIVE_MODE");

        await using var owner2 = _db.NewOwnerContext();
        (await owner2.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(2,
            "rien n'a dû être effacé : le retour en mode test ne rouvre pas la purge");
    }

    /// <summary>
    /// Le garde-fou générique — dans l'esprit de RlsCoverageTests. reset_school_data supprime ses
    /// tables dans un ordre FIGÉ dans son propre corps ; toute clé étrangère ON DELETE RESTRICT (ou
    /// NO ACTION) qui vise l'une d'elles depuis une table NON purgée fera échouer la « Zone de
    /// danger » en violation de clé étrangère (SQLSTATE 23503) dès qu'une école a une telle ligne —
    /// c'est exactement le bug qu'ont introduit les modules Examens, Intégration étatique et
    /// Inventaire, livrés après la fonction.
    ///
    /// Ce test n'énumère aucune liste à la main : il lit les tables purgées dans le corps de la
    /// fonction (pg_proc.prosrc) et confronte le schéma réel (pg_constraint). Le prochain module qui
    /// oubliera d'étendre v_targets fera échouer la CI de lui-même.
    /// </summary>
    [Fact]
    public async Task Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too()
    {
        var purged = await PurgedTableNamesAsync();
        purged.Should().Contain("students",
            "reset_school_data doit au moins purger les élèves — sinon ce test ne teste rien");

        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ref.relname AS referenced, child.relname AS referencing, con.conname AS fk
            FROM pg_constraint con
            JOIN pg_class child ON child.oid = con.conrelid
            JOIN pg_class ref   ON ref.oid   = con.confrelid
            JOIN pg_namespace n ON n.oid = child.relnamespace
            WHERE con.contype = 'f'
              AND n.nspname = 'public'
              AND con.confdeltype IN ('a', 'r')   -- NO ACTION / RESTRICT : la FK bloque le DELETE
              AND ref.relname   = ANY(@purged)    -- la cible est purgée...
              AND child.relname <> ALL(@purged)   -- ...mais pas la table qui la référence
            ORDER BY 1, 2;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "purged";
        parameter.Value = purged.ToArray();
        command.Parameters.Add(parameter);

        var offenders = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                offenders.Add($"{reader.GetString(1)} → {reader.GetString(0)} ({reader.GetString(2)})");
            }
        }

        offenders.Should().BeEmpty(
            "toute table qui référence une table purgée par reset_school_data avec une FK RESTRICT " +
            "doit être ajoutée à v_targets AVANT sa cible (voir la migration " +
            "FixResetSchoolDataMissingChildTables) — sinon « Réinitialiser les données » échoue en 23503");
    }

    /// <summary>Les tables listées dans v_targets, lues dans le corps même de reset_school_data.</summary>
    private async Task<List<string>> PurgedTableNamesAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT prosrc FROM pg_proc WHERE proname = 'reset_school_data'";

        var source = (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(
                "Fonction reset_school_data introuvable — la migration AddSchoolDataReset a-t-elle tourné ?");

        // Chaque cible est le premier élément d'un couple ['table', 'libellé'] du tableau v_targets.
        return Regex.Matches(source, @"\[\s*'([A-Za-z_]+)'\s*,")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
