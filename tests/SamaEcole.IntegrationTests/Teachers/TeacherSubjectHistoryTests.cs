using FluentAssertions;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Teachers.Commands.UpdateTeacher;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Teachers;

/// <summary>
/// Une qualification révoquée ne doit pas faire disparaître la matière déjà notée de la consultation
/// historique de sa période. La lecture d'historique réimpose SchoolId après IgnoreQueryFilters.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherSubjectHistoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("21111111-1111-1111-1111-111111111111");
    private static readonly Guid Enseignant = Guid.Parse("2aaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Directeur = Guid.Parse("2ddddddd-0000-0000-0000-0000000000d1");
    private static readonly Guid Matiere = Guid.Parse("2eeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Classe = Guid.Parse("2ccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Eleve = Guid.Parse("2bbbbbbb-0000-0000-0000-0000000000b1");
    private static readonly Guid Annee = Guid.Parse("23333333-0000-0000-0000-000000000001");
    private static readonly Guid Periode = Guid.Parse("24444444-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École historique" });
        owner.Classrooms.Add(new Classroom
        {
            Id = Classe, SchoolId = Ecole, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, ClassroomId = Classe, Matricule = "HIST-001", FullName = "Awa Historique",
            BirthDate = new DateOnly(2012, 1, 1), BirthPlace = "Dakar", Gender = "F"
        });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Periode, SchoolId = Ecole, SchoolYearId = Annee, Label = "Trimestre 1", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Histoire", Level = "Collège", Coefficient = 3 });
        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant, SchoolId = Ecole, Matricule = "ENS-HIST-001", FullName = "Moussa Diallo",
            Email = "moussa@example.test", BirthDate = new DateOnly(1988, 1, 1), BirthPlace = "Dakar", Status = EntityStatus.Active
        });
        owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = Matiere });
        owner.Grades.Add(new Grade
        {
            SchoolId = Ecole, StudentId = Eleve, SubjectId = Matiere, TermId = Periode,
            EvaluationType = EvaluationType.Devoir1, Value = 15
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Revoking_A_Teacher_Subject_Preserves_The_Period_Subject_And_Historical_Link()
    {
        uint rowVersion;
        await using (var read = _db.NewAppContext(Ecole))
        {
            rowVersion = await read.Teachers.Where(t => t.Id == Enseignant)
                .Select(t => EF.Property<uint>(t, "xmin")).SingleAsync();
        }

        await using (var app = _db.NewAppContext(Ecole))
        {
            await new UpdateTeacherCommandHandler(app, new TestCurrentUser(Directeur)).Handle(
                new UpdateTeacherCommand(Enseignant, "Moussa Diallo", "moussa@example.test", null,
                    new DateOnly(1988, 1, 1), "Dakar", null, null, [], rowVersion),
                CancellationToken.None);

            var periodSummary = await new GetGradeSummaryQueryHandler(
                app, new CoefficientOverrideLoader(app), new SubjectFollowScope(app))
                .Handle(new GetGradeSummaryQuery(Eleve, Periode), CancellationToken.None);

            periodSummary.Subjects.Should().ContainSingle(s => s.SubjectId == Matiere && s.SubjectName == "Histoire",
                "une révocation de qualification future ne retire pas la matière déjà évaluée de la période");
        }

        await using var owner = _db.NewOwnerContext();
        var historicalLink = await owner.TeacherSubjects.IgnoreQueryFilters()
            .SingleAsync(ts => ts.SchoolId == Ecole && ts.TeacherId == Enseignant && ts.SubjectId == Matiere);
        historicalLink.IsDeleted.Should().BeTrue("la relation est conservée pour les consultations historiques de cette école");
    }
}
