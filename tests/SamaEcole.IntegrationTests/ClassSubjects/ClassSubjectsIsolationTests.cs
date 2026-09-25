using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.ClassSubjects;

/// <summary>
/// Évolution N°6 — les tables <c>class_subjects</c> et <c>student_subject_enrollments</c> tiennent-elles leur
/// isolation, leurs contraintes et leurs purges DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core :
/// seuls la policy RLS, les index et les fonctions de purge font foi (même méthode que
/// <c>CoefficientOverrideIsolationTests</c>).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ClassSubjectsIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("81111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("82222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("8aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("8bbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid MatiereA = Guid.Parse("8ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatiereB = Guid.Parse("8ddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid EleveA = Guid.Parse("8eeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid AnneeA1 = Guid.Parse("81111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("81111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("82222222-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Terminale L2", Level = "Lycée", Capacity = 40, Series = "L2" },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Terminale L2", Level = "Lycée", Capacity = 40, Series = "L2" });
        owner.Subjects.AddRange(
            new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "SVT", Level = "Lycée", Coefficient = 2 },
            new Subject { Id = MatiereB, SchoolId = EcoleB, Name = "SVT", Level = "Lycée", Coefficient = 2 });
        owner.Students.Add(new Student
        {
            Id = EleveA, SchoolId = EcoleA, Matricule = "A-0001", FullName = "Élève A", BirthDate = new DateOnly(2008, 1, 1),
            BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
        });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_The_Programme_Of_Another_School()
    {
        await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);
        await InsertClassSubjectAsync(EcoleB, EcoleB, ClasseB, MatiereB);

        (await CountAsync(EcoleA, "class_subjects")).Should().Be(1);
        (await CountAsync(schoolId: null, "class_subjects")).Should().Be(0, "sans tenant, rien n'est visible");
    }

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_The_Options_Of_Another_School()
    {
        var classSubject = await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);
        await InsertChoiceAsync(EcoleA, classSubject, AnneeA1);

        (await CountAsync(EcoleA, "student_subject_enrollments")).Should().Be(1);
        (await CountAsync(EcoleB, "student_subject_enrollments")).Should().Be(0);
    }

    [Fact]
    public async Task Writing_A_Programme_Line_Into_Another_School_Should_Be_Rejected()
    {
        var act = () => InsertClassSubjectAsync(sessionSchool: EcoleA, schoolId: EcoleB, ClasseB, MatiereB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_Subject_Appears_Once_In_The_Programme_Of_A_Class()
    {
        await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);

        var duplicate = () => InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);

        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_Application_Role_Cannot_Physically_Delete_A_Choice()
    {
        var classSubject = await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);
        await InsertChoiceAsync(EcoleA, classSubject, AnneeA1);

        var act = () => ExecuteAsync(EcoleA, "DELETE FROM student_subject_enrollments;");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "règle #6 : aucune suppression physique");
    }

    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Choices_And_Keeps_The_Other_Years()
    {
        var classSubject = await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);
        await InsertChoiceAsync(EcoleA, classSubject, AnneeA1);
        await InsertChoiceAsync(EcoleA, classSubject, AnneeA2);

        await ExecuteAsync(EcoleA, $"SELECT count(*) FROM delete_school_year('{EcoleA}', '{AnneeA2}');");

        (await CountAsync(EcoleA, "student_subject_enrollments")).Should().Be(1);
        (await CountAsync(EcoleA, "class_subjects")).Should().Be(1, "le programme d'une classe n'appartient à aucune année");
    }

    [Fact]
    public async Task Resetting_The_School_Data_Removes_Programme_And_Choices_Without_A_Foreign_Key_Error()
    {
        var classSubject = await InsertClassSubjectAsync(EcoleA, EcoleA, ClasseA, MatiereA);
        await InsertChoiceAsync(EcoleA, classSubject, AnneeA1);

        await ExecuteAsync(EcoleA, $"SELECT count(*) FROM reset_school_data('{EcoleA}');");

        (await CountAsync(EcoleA, "student_subject_enrollments")).Should().Be(0);
        (await CountAsync(EcoleA, "class_subjects")).Should().Be(0);
    }

    private async Task<Guid> InsertClassSubjectAsync(Guid sessionSchool, Guid schoolId, Guid classroomId, Guid subjectId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO class_subjects
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "OptionGroup", "IsCustom", "IsActive", "DisplayOrder", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @classroomId, @subjectId, 'Option scientifique', FALSE, TRUE, 0, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("classroomId", classroomId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task InsertChoiceAsync(Guid schoolId, Guid classSubjectId, Guid yearId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO student_subject_enrollments
                ("Id", "SchoolId", "StudentId", "ClassSubjectId", "SchoolYearId", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, @classSubjectId, @yearId, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", EleveA);
        command.Parameters.AddWithValue("classSubjectId", classSubjectId);
        command.Parameters.AddWithValue("yearId", yearId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(Guid schoolId, string sql)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId, string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM "{table}" WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
