using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using SamaEcole.Application.Subjects.Queries.GetSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

[Trait("Category", "MultiTenant")]
public class SubjectOptionalFlagTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("93333333-3333-3333-3333-333333333333");
    private static readonly Guid Domaine = Guid.Parse("93333333-0000-0000-0000-0000000000d1");
    private static readonly Guid Activite = Guid.Parse("93333333-0000-0000-0000-0000000000d2");
    private static readonly Guid Arabe = Guid.Parse("93333333-0000-0000-0000-0000000000d3");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École" });
        owner.Subjects.AddRange(
            new Subject { Id = Domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "CE1", Coefficient = 1 },
            new Subject { Id = Activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "CE1", Coefficient = 1, ParentSubjectId = Domaine },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2 });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Subject_Can_Be_Flagged_Optional_With_A_Trimmed_Group_And_The_List_Returns_It()
    {
        await using var db = _db.NewAppContext(Ecole);
        var current = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe);

        var result = await new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, current.RowVersion,
                IsOptional: true, OptionGroup: "  LV2 "), default);

        result.IsOptional.Should().BeTrue();
        result.OptionGroup.Should().Be("LV2");

        await using var reread = _db.NewAppContext(Ecole);
        var dto = (await new GetSubjectsQueryHandler(reread).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe);
        dto.IsOptional.Should().BeTrue();
        dto.OptionGroup.Should().Be("LV2");
    }

    [Fact]
    public async Task Flagging_Off_Clears_The_Group()
    {
        await using var db = _db.NewAppContext(Ecole);
        var rv = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe).RowVersion;
        var on = await new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, rv, IsOptional: true, OptionGroup: "LV2"), default);

        await using var db2 = _db.NewAppContext(Ecole);
        var off = await new UpdateSubjectCommandHandler(db2).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, on.RowVersion, IsOptional: false, OptionGroup: "LV2"), default);

        off.IsOptional.Should().BeFalse();
        off.OptionGroup.Should().BeNull("le groupe n'a de sens que pour une option");
    }

    [Fact]
    public async Task A_Domain_That_Carries_Activities_Cannot_Become_Optional()
    {
        await using var db = _db.NewAppContext(Ecole);
        var rv = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Domaine).RowVersion;

        var act = () => new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Domaine, "Lang & Com.", "CE1", 1, rv, IsOptional: true), default);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
