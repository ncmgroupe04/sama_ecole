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
///   3. elle épargne ce qui a été CONFIGURÉ (utilisateurs, réglages, années, classes, matières) ;
///   4. elle remet les compteurs de matricules à zéro, sinon l'école garde la mémoire de ses essais.
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

        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleB)).Should().Be(2,
            "purger une école ne doit jamais entamer les données d'un autre établissement");
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Payments.IgnoreQueryFilters().CountAsync(p => p.SchoolId == EcoleB)).Should().Be(1);
        (await owner.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == EcoleB)).Should().Be(1);
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

        // 2 élèves (dont un supprimé logiquement) + 1 inscription + 1 paiement + 1 note + 1 compteur.
        summary.TotalRowsDeleted.Should().Be(6);
        summary.Entries.Should().Contain(e => e.Label == "Élèves" && e.RowsDeleted == 2);
        summary.Entries.Should().Contain(e => e.Label == "Paiements" && e.RowsDeleted == 1);
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
}
