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
    /// Une école « après une phase d'essai » : de la configuration (classe, année, trimestre, matière,
    /// compte utilisateur) ET des données saisies (élève, inscription, paiement, note, compteur de
    /// matricules), dont un élève déjà supprimé logiquement.
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

        owner.Users.Add(new User
        {
            SchoolId = schoolId,
            Email = $"directeur-{schoolId:N}@example.sn",
            PasswordHash = "hash",
            FullName = "Le Directeur",
            Role = Role.Directeur
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

        // Patrimoine (catégorie + lot) : CONSERVÉ par la purge. Seule la fiche de prêt, qui lie un
        // bien à l'élève, doit partir avec lui.
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
    public async Task The_Configuration_Of_The_School_Must_Survive_The_Purge()
    {
        await PurgeEcoleAAsync();

        await using var owner = _db.NewOwnerContext();

        (await owner.Schools.CountAsync(s => s.Id == EcoleA)).Should().Be(1);
        (await owner.Users.IgnoreQueryFilters().CountAsync(u => u.SchoolId == EcoleA)).Should().Be(1,
            "le Directeur doit pouvoir se reconnecter après avoir remis son école à neuf");
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.SchoolId == EcoleA)).Should().Be(1,
            "l'année scolaire active est conservée (critère de la Zone de danger)");
        (await owner.Terms.IgnoreQueryFilters().CountAsync(t => t.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Classrooms.IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Subjects.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(1);
        (await owner.Set<InventoryCategory>().IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA)).Should().Be(1,
            "le patrimoine (inventaire) est configuré, pas saisi pendant l'essai : il survit à la purge");
        (await owner.Set<InventoryItem>().IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(1);
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

        // 2 élèves (dont un supprimé logiquement) + 1 inscription + 1 paiement + 1 note + 1 compteur
        // + 1 session d'examen + 1 dossier + 1 résultat + 1 certificat de mutation + 1 prêt = 11.
        summary.TotalRowsDeleted.Should().Be(11);
        summary.Entries.Should().Contain(e => e.Label == "Élèves" && e.RowsDeleted == 2);
        summary.Entries.Should().Contain(e => e.Label == "Paiements" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Dossiers de candidature aux examens" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Certificats de mutation" && e.RowsDeleted == 1);
        summary.Entries.Should().Contain(e => e.Label == "Affectations de matériel" && e.RowsDeleted == 1);
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

    /// <summary>Passe une école en mode réel, comme le ferait GoLiveCommand.</summary>
    private async Task MarkAsLiveAsync(Guid schoolId)
    {
        await using var owner = _db.NewOwnerContext();

        var school = await owner.Schools.IgnoreQueryFilters().SingleAsync(s => s.Id == schoolId);
        school.WentLiveAt = new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero);

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
    public async Task Reverting_To_Test_Mode_Must_Make_The_Purge_Possible_Again()
    {
        // Le garde lit l'état COURANT de l'école, il ne se souvient de rien : repasser en mode test
        // (RevertToTestCommand, réservé aux environnements jetables) rouvre réellement la purge. Sans
        // ce test, un garde qui bloquerait définitivement après un premier passage en mode réel
        // passerait inaperçu — la recette ne pourrait plus se remettre à neuf.
        await MarkAsLiveAsync(EcoleA);

        await using (var owner = _db.NewOwnerContext())
        {
            var school = await owner.Schools.IgnoreQueryFilters().SingleAsync(s => s.Id == EcoleA);
            school.WentLiveAt = null;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var appA = _db.NewAppContext(EcoleA);

        var summary = await _db.NewResetSchoolDataService(appA).ResetAsync(EcoleA, CancellationToken.None);

        summary.TotalRowsDeleted.Should().Be(11);
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
