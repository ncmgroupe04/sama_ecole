using System.Text.RegularExpressions;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.SchoolYears;

/// <summary>
/// Suppression d'une année scolaire EN MODE TEST — fonction <c>delete_school_year</c> (migration
/// AddSchoolYearDeletion).
///
/// Comme la « Zone de danger », c'est une suppression PHYSIQUE : exception bornée à la règle #6
/// d'AGENTS.md, et l'une des rares opérations où l'isolation ne repose pas sur les policies RLS —
/// un SECURITY DEFINER en est exempté, la fonction porte donc ses propres gardes. Cinq choses
/// doivent être prouvées sur une VRAIE base :
///
///   1. la suppression s'arrête à la frontière de l'ANNÉE : l'exercice voisin de la même école
///      garde tout, jusqu'à la dernière note ;
///   2. elle s'arrête à la frontière du TENANT : l'année homonyme de l'école voisine ne bouge pas ;
///   3. les ÉLÈVES survivent — ils appartiennent à l'établissement, pas à un exercice ;
///   4. elle refuse en mode réel, et pour une année qui n'est pas celle de la session ;
///   5. aucune clé étrangère RESTRICT venue d'une table NON traitée ne peut la faire échouer en 23503.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DeleteSchoolYearTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Deux exercices par école : celui qu'on supprime, et le témoin qui doit rester intact.
    private static readonly Guid AnneeSupprimeeA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid AnneeGardeeA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid AnneeSupprimeeB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        Seed(owner, EcoleA, AnneeSupprimeeA, "2025-2026", 2025, isActive: false);
        Seed(owner, EcoleA, AnneeGardeeA, "2026-2027", 2026, isActive: true);
        Seed(owner, EcoleB, AnneeSupprimeeB, "2025-2026", 2025, isActive: true);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>
    /// Un exercice complet : classe, trimestre, matière, élève, inscription, paiement, note, appel,
    /// examen et certificat de mutation — dont une inscription déjà supprimée logiquement, que le
    /// Global Query Filter masque mais que la purge doit atteindre.
    /// </summary>
    private static void Seed(
        ApplicationDbContext owner, Guid schoolId, Guid schoolYearId, string label, int year, bool isActive)
    {
        var classroomId = Guid.NewGuid();
        var termId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var sheetId = Guid.NewGuid();
        var examSessionId = Guid.NewGuid();
        var examDossierId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = schoolYearId, SchoolId = schoolId, Label = label,
            StartDate = new DateOnly(year, 10, 1), EndDate = new DateOnly(year + 1, 7, 31),
            IsActive = isActive
        });

        owner.Terms.Add(new Term
        {
            Id = termId, SchoolId = schoolId, SchoolYearId = schoolYearId, Label = "Trimestre 1",
            Order = 1, StartDate = new DateOnly(year, 10, 1), EndDate = new DateOnly(year, 12, 20)
        });

        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = schoolId, Name = $"CM2-{year}", Level = "Primaire", Capacity = 40
        });

        owner.Subjects.Add(new Subject
        {
            Id = subjectId, SchoolId = schoolId, Name = $"Mathématiques {year}", Level = "Primaire", Coefficient = 4
        });

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = schoolId, Matricule = $"ELEV-{year}-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
        });

        // Inscription déjà annulée (suppression logique) : elle doit partir avec l'année, sinon
        // l'exercice « supprimé » garderait une ligne invisible mais bien présente.
        var cancelled = new Enrollment
        {
            SchoolId = schoolId, StudentId = studentId, SchoolYearId = schoolYearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Cancelled,
            TotalDue = 150_000m, AmountPaid = 0m, ReceiptNumber = $"REC-{year}-0002",
            EnrolledAt = DateTimeOffset.UtcNow
        };
        cancelled.SoftDelete("test");

        owner.Enrollments.AddRange(
            new Enrollment
            {
                Id = enrollmentId, SchoolId = schoolId, StudentId = studentId, SchoolYearId = schoolYearId,
                ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 150_000m, AmountPaid = 50_000m, ReceiptNumber = $"REC-{year}-0001",
                EnrolledAt = DateTimeOffset.UtcNow
            },
            cancelled);

        owner.Payments.Add(new Payment
        {
            SchoolId = schoolId, EnrollmentId = enrollmentId, Amount = 50_000m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Partial, BalanceAfter = 100_000m,
            ReceiptNumber = $"PAY-{year}-0001", ReceivedByUserId = userId, PaidAt = DateTimeOffset.UtcNow
        });

        owner.Grades.Add(new Grade
        {
            SchoolId = schoolId, StudentId = studentId, SubjectId = subjectId, TermId = termId,
            EvaluationType = EvaluationType.Devoir1, Value = 14.5m
        });

        owner.AttendanceSheets.Add(new AttendanceSheet
        {
            Id = sheetId, SchoolId = schoolId, ClassroomId = classroomId, SubjectId = subjectId,
            SchoolYearId = schoolYearId, Date = new DateOnly(year, 10, 5), Period = "Matin",
            TakenByUserId = userId
        });

        owner.Set<StudentAttendance>().Add(new StudentAttendance
        {
            SchoolId = schoolId, AttendanceSheetId = sheetId, StudentId = studentId,
            Status = AttendanceStatus.Present
        });

        // L'affectation porte une FK composite (SchoolId, TeacherId) : il lui faut un vrai enseignant.
        // Lui NE sera PAS supprimé — il appartient à l'établissement, comme les élèves.
        var teacherId = Guid.NewGuid();

        owner.Teachers.Add(new Teacher
        {
            Id = teacherId, SchoolId = schoolId, Matricule = $"ENS-{year}-0001", FullName = "L'Enseignant",
            Email = $"enseignant-{year}-{schoolId:N}@example.sn", BirthDate = new DateOnly(1990, 1, 1)
        });

        owner.Set<TeacherAssignment>().Add(new TeacherAssignment
        {
            SchoolId = schoolId, TeacherId = teacherId, ClassroomId = classroomId,
            SubjectId = subjectId, SchoolYearId = schoolYearId
        });

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
            AverageScore = 12.5m, DeliberatedOn = new DateOnly(year + 1, 7, 5)
        });

        owner.Set<StudentMutationCertificate>().Add(new StudentMutationCertificate
        {
            SchoolId = schoolId, StudentId = studentId, SchoolYearId = schoolYearId,
            CertificateNumber = $"MUT-{year}-0001",
            // Unique GLOBAL (pas par école) et borné à 30 caractères : préfixe court de l'école, puis
            // l'année — deux écoles et deux exercices sont semés, les quatre codes doivent différer.
            VerificationCode = $"{schoolId.ToString("N")[..8]}{year}".ToUpperInvariant(),
            ClassroomNameSnapshot = "CM2", Reason = StudentMutationReason.Demenagement,
            IssuedOn = new DateOnly(year + 1, 6, 1), WasFinanciallyClear = true
        });
    }

    private async Task<int> DeleteYearAsync(Guid schoolId, Guid schoolYearId)
    {
        await using var app = _db.NewAppContext(schoolId);

        var summary = await _db.NewSchoolYearPurgeService(app).PurgeAsync(schoolId, schoolYearId, CancellationToken.None);

        return summary.TotalRowsDeleted;
    }

    [Fact]
    public async Task Deleting_A_Year_Must_Leave_The_Other_Year_Of_The_Same_School_Intact()
    {
        await DeleteYearAsync(EcoleA, AnneeSupprimeeA);

        await using var owner = _db.NewOwnerContext();

        // L'exercice visé a disparu, chaîne financière et pédagogique comprise.
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeA)).Should().Be(0);
        (await owner.Terms.IgnoreQueryFilters().CountAsync(t => t.SchoolYearId == AnneeSupprimeeA)).Should().Be(0);
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolYearId == AnneeSupprimeeA)).Should().Be(0,
            "y compris l'inscription déjà supprimée logiquement, que le Global Query Filter masque");
        (await owner.AttendanceSheets.IgnoreQueryFilters().CountAsync(s => s.SchoolYearId == AnneeSupprimeeA)).Should().Be(0);
        (await owner.ExamSessions.IgnoreQueryFilters().CountAsync(s => s.SchoolYearId == AnneeSupprimeeA)).Should().Be(0);
        (await owner.StudentMutationCertificates.IgnoreQueryFilters()
            .CountAsync(c => c.SchoolYearId == AnneeSupprimeeA)).Should().Be(0);

        // L'exercice VOISIN de la même école n'a pas bougé — c'est tout l'enjeu d'une suppression
        // ciblée par année plutôt que par école.
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeGardeeA)).Should().Be(1);
        (await owner.Terms.IgnoreQueryFilters().CountAsync(t => t.SchoolYearId == AnneeGardeeA)).Should().Be(1);
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolYearId == AnneeGardeeA)).Should().Be(2);
        (await owner.Payments.IgnoreQueryFilters().CountAsync(p => p.SchoolId == EcoleA)).Should().Be(1,
            "le paiement de l'année supprimée part, celui de l'année gardée reste");
        (await owner.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == EcoleA)).Should().Be(1);
    }

    [Fact]
    public async Task The_Students_Must_Survive_The_Deletion_Of_A_Year()
    {
        await DeleteYearAsync(EcoleA, AnneeSupprimeeA);

        await using var owner = _db.NewOwnerContext();

        // Un élève appartient à l'ÉTABLISSEMENT, pas à un exercice : supprimer une année ne raye
        // personne des effectifs, elle défait seulement son inscription à cette année-là.
        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(2,
            "un élève par exercice a été semé, et aucun ne doit disparaître avec l'année");
    }

    [Fact]
    public async Task Deleting_A_Year_Must_Not_Touch_The_Same_Year_Of_Another_School()
    {
        await DeleteYearAsync(EcoleA, AnneeSupprimeeA);

        await using var owner = _db.NewOwnerContext();

        // Les deux écoles ont une année « 2025-2026 » : celle de l'École B doit être intacte.
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeB)).Should().Be(1);
        (await owner.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolYearId == AnneeSupprimeeB)).Should().Be(2);
        (await owner.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleB)).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_A_Year_Of_Another_School_Must_Be_Refused_By_The_Database()
    {
        // LE test de sécurité : delete_school_year est SECURITY DEFINER, donc exemptée de RLS. Sans sa
        // garde interne, une session de l'École A viderait un exercice de l'École B.
        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewSchoolYearPurgeService(appA)
            .PurgeAsync(EcoleB, AnneeSupprimeeB, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeB)).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_A_Year_That_Belongs_To_Another_School_Must_Be_Refused_Even_With_The_Right_Tenant()
    {
        // Tenant correct, mais année d'un confrère : la fonction vérifie AUSSI l'appartenance, sinon
        // connaître un identifiant d'année suffirait à vider l'exercice d'une autre école.
        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewSchoolYearPurgeService(appA)
            .PurgeAsync(EcoleA, AnneeSupprimeeB, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.NoDataFound);

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeB)).Should().Be(1);
    }

    [Fact]
    public async Task A_Session_Without_Tenant_Must_Not_Be_Able_To_Delete_Anything()
    {
        await using var appWithoutTenant = _db.NewAppContext(schoolId: null);

        var act = async () => await _db.NewSchoolYearPurgeService(appWithoutTenant)
            .PurgeAsync(EcoleA, AnneeSupprimeeA, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeA)).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_A_Year_In_Live_Mode_Must_Be_Refused_By_The_Database_Itself()
    {
        // En mode réel, une année porte des reçus remis aux familles et des écritures comptables : la
        // règle #6 les déclare inaltérables. Le Handler refuse bien avant (il exige une année VIDE et
        // se contente alors d'une suppression logique) ; ce test prouve que la base ne s'en remet pas
        // à lui — exactement comme pour reset_school_data.
        await using (var owner = _db.NewOwnerContext())
        {
            var school = await owner.Schools.IgnoreQueryFilters().SingleAsync(s => s.Id == EcoleA);
            school.WentLiveAt = new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero);
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewSchoolYearPurgeService(appA)
            .PurgeAsync(EcoleA, AnneeSupprimeeA, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.RaiseException);

        thrown.Which.MessageText.Should().Contain("SCHOOL_YEAR_DELETE_UNAVAILABLE_LIVE_MODE");

        // Et pas une ligne effacée : la fonction échoue AVANT sa première suppression.
        await using var check = _db.NewOwnerContext();
        (await check.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeSupprimeeA)).Should().Be(1);
        (await check.Enrollments.IgnoreQueryFilters().CountAsync(e => e.SchoolYearId == AnneeSupprimeeA)).Should().Be(2);
    }

    [Fact]
    public async Task A_Second_Deletion_Must_Be_Refused_Without_Side_Effect()
    {
        await DeleteYearAsync(EcoleA, AnneeSupprimeeA);

        // L'année n'existe plus : la fonction le dit (no_data_found) au lieu de supprimer « à vide ».
        // Le Handler, lui, aura déjà renvoyé 404 — il lit l'année avant d'appeler.
        await using var appA = _db.NewAppContext(EcoleA);

        var act = async () => await _db.NewSchoolYearPurgeService(appA)
            .PurgeAsync(EcoleA, AnneeSupprimeeA, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.NoDataFound);
    }

    [Fact]
    public async Task The_Deletion_Must_Work_Inside_An_Ambient_Transaction()
    {
        // LE cas d'usage réel, et le seul que les autres tests ne couvrent pas :
        // DeleteSchoolYearCommandHandler enveloppe la suppression ET la rebascule de l'année active
        // dans UNE transaction (ExecuteInTransactionAsync), pendant que le service, lui, ouvre puis
        // referme la connexion d'EF Core. Si ce va-et-vient cassait la transaction ambiante, la
        // fonctionnalité échouerait au premier appel réel — alors que tous les tests qui appellent le
        // service à nu resteraient verts.
        await using var app = _db.NewAppContext(EcoleA);
        await using var transaction = await app.Database.BeginTransactionAsync();

        var summary = await _db.NewSchoolYearPurgeService(app)
            .PurgeAsync(EcoleA, AnneeGardeeA, CancellationToken.None);

        summary.TotalRowsDeleted.Should().BeGreaterThan(0);

        // Écriture EF Core APRÈS la purge, dans la même transaction : c'est exactement la rebascule
        // de l'année active (l'année supprimée était l'année de travail de l'établissement).
        var fallback = await app.SchoolYears.FirstAsync(y => y.Id == AnneeSupprimeeA);
        fallback.IsActive = true;
        await app.SaveChangesAsync(CancellationToken.None);

        await transaction.CommitAsync();

        await using var owner = _db.NewOwnerContext();

        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.Id == AnneeGardeeA)).Should().Be(0);
        (await owner.SchoolYears.IgnoreQueryFilters().SingleAsync(y => y.Id == AnneeSupprimeeA))
            .IsActive.Should().BeTrue("les deux opérations doivent avoir été validées ensemble");
    }

    /// <summary>
    /// Garde-fou générique, dans l'esprit de celui de ResetSchoolDataTests : la fonction traite une
    /// liste FIGÉE de tables. Toute clé étrangère ON DELETE RESTRICT (ou NO ACTION) qui vise l'une
    /// d'elles depuis une table NON traitée fera échouer la suppression en 23503 dès qu'une école aura
    /// une telle ligne — c'est exactement le bug qu'ont introduit les modules livrés après
    /// reset_school_data. Le prochain module qui oubliera d'étendre delete_school_year fera échouer la
    /// CI de lui-même, sans qu'aucune liste n'ait à être maintenue à la main ici.
    /// </summary>
    [Fact]
    public async Task Every_Restrict_Foreign_Key_Into_A_Handled_Table_Must_Come_From_A_Handled_Table_Too()
    {
        var handled = await HandledTableNamesAsync();
        handled.Should().Contain("school_years", "la fonction doit au moins supprimer l'année elle-même");
        handled.Should().Contain("enrollments");

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
              AND ref.relname   = ANY(@handled)
              AND child.relname <> ALL(@handled)
            ORDER BY 1, 2;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "handled";
        parameter.Value = handled.ToArray();
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
            "toute table qui référence une table traitée par delete_school_year avec une FK RESTRICT "
            + "doit y être traitée AVANT sa cible, sinon la suppression d'une année échoue en 23503");
    }

    /// <summary>Les tables citées par la fonction, lues dans son propre corps (pg_proc.prosrc).</summary>
    private async Task<List<string>> HandledTableNamesAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT prosrc FROM pg_proc WHERE proname = 'delete_school_year'";

        var source = (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(
                "Fonction delete_school_year introuvable — la migration AddSchoolYearDeletion a-t-elle tourné ?");

        return Regex.Matches(source, @"DELETE FROM ""?([A-Za-z_]+)""?")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}
