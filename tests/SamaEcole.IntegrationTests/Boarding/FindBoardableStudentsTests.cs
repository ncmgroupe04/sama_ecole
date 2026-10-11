using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Boarders.FindBoardableStudents;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Recherche des élèves à affecter sous /api/v1/boarding (lot G, tâche 6) — handler + PostgreSQL réel. Le séjour (et non l'inscription) est décrit :
/// la modale appelle assign-bed, qui exige le jeton xmin du SÉJOUR pour un transfert.
/// </summary>
[Trait("Category", "MultiTenant")]
public class FindBoardableStudentsTests : LeaveTestBase
{
    private async Task<IReadOnlyList<BoardableStudentCandidateDto>> FindAsync(string? term, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new FindBoardableStudentsQueryHandler(ctx).Handle(new FindBoardableStudentsQuery(term), CancellationToken.None);
    }

    /// <summary>Un élève sans séjour (« externe »), inscrit à l'année demandée.</summary>
    private async Task AddStudentAsync(string key, Guid year, EnrollmentStatus status = EnrollmentStatus.Confirmed)
    {
        await using var owner = Db.NewOwnerContext();
        var classroom = await owner.Classrooms.IgnoreQueryFilters().FirstAsync(c => c.SchoolId == EcoleA);
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, Matricule = $"M-{key}", FullName = $"Élève {key}",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom.Id
        };
        owner.Students.Add(student);
        owner.Enrollments.Add(new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom.Id,
            Type = EnrollmentType.NewEnrollment, Status = status, TotalDue = 15_000m, ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
        });
        await owner.SaveChangesAsync();
    }

    [Fact]
    public async Task A_Boarder_Is_Described_By_Its_Stay_Bed_Room_And_Dormitory()
    {
        var result = await FindAsync("m1");

        var m1 = result.Should().ContainSingle(r => r.Matricule == "M-m1").Subject;
        (m1.BoarderId, m1.Regime, m1.BedNumber, m1.RoomName, m1.DormitoryName)
            .Should().Be((Stays["m1"], BoardingRegime.Interne, 1, "Chambre 101", "Pavillon Oustaz Ahmad"));
        (m1.FullName, m1.ClassroomName).Should().Be(("Élève m1", "CM2"));
        m1.BedId.Should().NotBeNull();
    }

    [Fact]
    public async Task The_Row_Version_Is_The_Stays_Token_Not_The_Enrollments()
    {
        var m1 = (await FindAsync("m1")).Single(r => r.Matricule == "M-m1");

        // Le jeu de données insère tout dans UNE transaction : séjour et inscription partagent le même xmin. On fait avancer celui de l'inscription
        // dans une transaction séparée, sinon le test ne distinguerait pas le bon jeton du mauvais.
        await using (var owner = Db.NewOwnerContext())
        {
            var enrollment = await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == m1.EnrollmentId);
            enrollment.TotalDue += 1m;
            await owner.SaveChangesAsync();
        }

        await using var ctx = Db.NewAppContext(EcoleA);
        var stayToken = await ctx.BoardingEnrollments.Where(b => b.Id == Stays["m1"]).Select(b => EF.Property<uint>(b, "xmin")).SingleAsync();
        var enrollmentToken = await ctx.Enrollments.Where(e => e.Id == m1.EnrollmentId).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync();

        m1.RowVersion.Should().Be(stayToken);
        // Le test n'a de sens que si les deux jetons diffèrent : sinon on ne distinguerait pas le bon du mauvais.
        stayToken.Should().NotBe(enrollmentToken);
    }

    [Fact]
    public async Task A_Half_Boarder_Has_A_Stay_But_No_Bed_Room_Or_Dormitory()
    {
        var demi = (await FindAsync("demi")).Single();

        (demi.Regime, demi.BoarderId).Should().Be((BoardingRegime.DemiPensionnaire, Stays["demi"]));
        (demi.BedId, demi.BedNumber, demi.RoomName, demi.DormitoryName).Should().Be(((Guid?)null, (int?)null, (string?)null, (string?)null));
        demi.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Boarder_Awaiting_A_Bed_Is_An_Interne_Without_A_Bed()
    {
        var waiting = (await FindAsync("attente")).Single();

        (waiting.Regime, waiting.BedId, waiting.RoomName).Should().Be((BoardingRegime.Interne, (Guid?)null, (string?)null));
    }

    [Fact]
    public async Task A_Student_Without_An_Active_Stay_Is_An_External_One()
    {
        await AddStudentAsync("externe", AnneeA);

        var externe = (await FindAsync("externe")).Single();

        (externe.BoarderId, externe.Regime, externe.BedId, externe.RowVersion)
            .Should().Be(((Guid?)null, (BoardingRegime?)null, (Guid?)null, (uint?)null));
    }

    [Fact]
    public async Task An_Ended_Stay_Does_Not_Count_The_Student_Is_External_Again()
    {
        var closed = (await FindAsync("clos")).Single();

        (closed.BoarderId, closed.Regime).Should().Be(((Guid?)null, (BoardingRegime?)null));
    }

    [Fact]
    public async Task Cancelled_And_Previous_Year_Enrollments_Are_Not_Offered()
    {
        await AddStudentAsync("annule", AnneeA, EnrollmentStatus.Cancelled);
        await AddStudentAsync("ancien", AnneeAPassee);

        (await FindAsync("annule")).Should().BeEmpty();
        (await FindAsync("ancien")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("m")]
    [InlineData(" m ")]
    public async Task Less_Than_Two_Characters_Returns_An_Empty_List(string? term)
    {
        (await FindAsync(term)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_Search_Matches_Name_Or_Matricule_Ignoring_Case_And_Is_Sorted_By_Name()
    {
        // Le terme est mis en minuscules puis comparé à lower(colonne). Les cas ci-dessous ne dépendent pas de la collation de la base
        // (une majuscule accentuée comme « É » ne se minusculise pas de la même façon partout) : casse ASCII et sous-chaîne accentuée en minuscules.
        var byName = await FindAsync("lève M");
        var byMatricule = await FindAsync("M-M2");

        byName.Select(r => r.FullName).Should().BeInAscendingOrder().And.HaveCountGreaterThan(1);
        byMatricule.Should().ContainSingle().Which.Matricule.Should().Be("M-m2");
    }

    [Fact]
    public async Task At_Most_Twenty_Results_Are_Returned()
    {
        for (var i = 0; i < 22; i++)
        {
            await AddStudentAsync($"mass{i:00}", AnneeA);
        }

        (await FindAsync("mass")).Should().HaveCount(20);
    }

    [Fact]
    public async Task Another_Schools_Students_Are_Never_Returned()
    {
        (await FindAsync("autre")).Should().BeEmpty("l'élève « autre » est inscrit à l'école B");
        (await FindAsync("autre", school: EcoleB)).Should().ContainSingle(r => r.Matricule == "M-autre");
        (await FindAsync("m1", school: EcoleB)).Should().BeEmpty();
    }
}
