using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Finance.Queries.GetPayrollTeacherOptions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>Sélecteur des enseignants de l'écran Paie : isolé par école, trié, et sans donnée personnelle.</summary>
public class PayrollTeacherOptionsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid EcoleB = Guid.Parse("b1000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Teachers.AddRange(
            new Teacher { SchoolId = EcoleA, Matricule = "ENS-002", FullName = "Moussa Diop", Email = "m@a.sn", BirthDate = new DateOnly(1985, 1, 1) },
            new Teacher { SchoolId = EcoleA, Matricule = "ENS-001", FullName = "Awa Fall", Email = "a@a.sn", BirthDate = new DateOnly(1988, 5, 5) },
            new Teacher { SchoolId = EcoleB, Matricule = "ENS-001", FullName = "Ibra Sow", Email = "i@b.sn", BirthDate = new DateOnly(1980, 2, 2) });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_Only_The_Current_Schools_Teachers_Sorted_By_Name_With_Id_Matricule_And_Name_Only()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var options = await new GetPayrollTeacherOptionsQueryHandler(db).Handle(new GetPayrollTeacherOptionsQuery(), default);

        options.Select(o => o.FullName).Should().Equal("Awa Fall", "Moussa Diop");
        options.Select(o => o.Matricule).Should().Equal("ENS-001", "ENS-002");
        typeof(PayrollTeacherOptionDto).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Id", "Matricule", "FullName"], "aucun téléphone, e-mail ni adresse ne sort de cette route");
    }

    [Fact]
    public async Task A_Soft_Deleted_Teacher_Is_Not_Offered()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var teacher = owner.Teachers.IgnoreQueryFilters().First(t => t.SchoolId == EcoleA && t.Matricule == "ENS-002");
            teacher.SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);
        var options = await new GetPayrollTeacherOptionsQueryHandler(db).Handle(new GetPayrollTeacherOptionsQuery(), default);

        options.Select(o => o.FullName).Should().Equal("Awa Fall");
    }
}
