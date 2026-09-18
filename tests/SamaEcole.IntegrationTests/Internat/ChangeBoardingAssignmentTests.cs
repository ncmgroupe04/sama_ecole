using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class ChangeBoardingAssignmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("66666666-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("66666666-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("66666666-0000-0000-0000-00000000000b");
    private static readonly Guid Batiment = Guid.Parse("66666666-0000-0000-0000-00000000000c");
    private static readonly Guid ChambreA = Guid.Parse("66666666-0000-0000-0000-00000000000d");
    private static readonly Guid ChambreB = Guid.Parse("66666666-0000-0000-0000-00000000000e");
    private static readonly Guid CatPension = Guid.Parse("66666666-0000-0000-0000-00000000000f");
    private static readonly Guid Eleve = Guid.Parse("66666666-0000-0000-0000-000000000010");
    private static readonly Guid Inscription = Guid.Parse("66666666-0000-0000-0000-000000000011");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}", StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = ChambreA, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre A", Type = RoomType.Dortoir, Capacity = 1 });
        owner.Rooms.Add(new Room { Id = ChambreB, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre B", Type = RoomType.Dortoir, Capacity = 1 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Externe, TotalDue = 15_000m, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<uint> CurrentRowVersionAsync(ApplicationDbContext db) =>
        await db.Enrollments.Where(e => e.Id == Inscription).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync();

    [Fact]
    public async Task Assigning_A_Room_Adds_The_Pension_Line_Once()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        var rowVersion = await CurrentRowVersionAsync(db1);
        var handler1 = new ChangeBoardingAssignmentCommandHandler(db1);

        var result1 = await handler1.Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion),
            CancellationToken.None);

        result1.TotalDue.Should().Be(15_000m + 20_000m * 9);

        // Second changement de chambre (toujours Interne) : la pension ne doit PAS être doublée.
        await using var db2 = _db.NewAppContext(EcoleA);
        var rowVersion2 = await CurrentRowVersionAsync(db2);
        var handler2 = new ChangeBoardingAssignmentCommandHandler(db2);

        var result2 = await handler2.Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreB, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion2),
            CancellationToken.None);

        result2.TotalDue.Should().Be(15_000m + 20_000m * 9); // inchangé, pas de doublon
        result2.RoomId.Should().Be(ChambreB);
    }

    [Fact]
    public async Task Releasing_A_Student_Keeps_The_Pension_Line_Due()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        var rowVersion = await CurrentRowVersionAsync(db1);
        await new ChangeBoardingAssignmentCommandHandler(db1).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion),
            CancellationToken.None);

        await using var db2 = _db.NewAppContext(EcoleA);
        var rowVersion2 = await CurrentRowVersionAsync(db2);
        var released = await new ChangeBoardingAssignmentCommandHandler(db2).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, RoomId: null, BoardingStatus.Externe, IncludeBoardingFee: false, rowVersion2),
            CancellationToken.None);

        released.RoomId.Should().BeNull();
        released.BoardingStatus.Should().Be(BoardingStatus.Externe);
        released.TotalDue.Should().Be(15_000m + 20_000m * 9); // la pension déjà facturée reste due (spec §2, décision #7)
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422()
    {
        // Occupe ChambreA avec un premier élève.
        await using var setup = _db.NewOwnerContext();
        var autreEleve = Guid.NewGuid();
        var autreInscription = Guid.NewGuid();
        setup.Students.Add(new Student { Id = autreEleve, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Moussa Diop", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        setup.Enrollments.Add(new Enrollment { Id = autreInscription, SchoolId = EcoleA, StudentId = autreEleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Interne, RoomId = ChambreA, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow });
        await setup.SaveChangesAsync(CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var rowVersion = await CurrentRowVersionAsync(db);
        var act = () => new ChangeBoardingAssignmentCommandHandler(db).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: false, rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Rejected_With_Concurrency_Conflict()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        var staleRowVersion = await CurrentRowVersionAsync(db1);

        // Un autre changement passe en premier, avance le xmin.
        await using var db2 = _db.NewAppContext(EcoleA);
        var currentRowVersion = await CurrentRowVersionAsync(db2);
        await new ChangeBoardingAssignmentCommandHandler(db2).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: false, currentRowVersion),
            CancellationToken.None);

        // Le premier appelant, avec le jeton PÉRIMÉ, doit être rejeté en conflit — jamais un écrasement silencieux.
        await using var db3 = _db.NewAppContext(EcoleA);
        var act = () => new ChangeBoardingAssignmentCommandHandler(db3).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreB, BoardingStatus.Interne, IncludeBoardingFee: false, staleRowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Externe_With_A_RoomId_Is_Rejected_With_422()
    {
        // Un élève Externe ne peut jamais pointer vers une chambre : le régime Externe gate l'accès au
        // module Internat (même garde que CreateEnrollmentCommandHandler) — sinon l'occupation compterait
        // un "occupant" fantôme jamais visible côté Internat mais jamais libéré non plus.
        await using var db = _db.NewAppContext(EcoleA);
        var rowVersion = await CurrentRowVersionAsync(db);
        var act = () => new ChangeBoardingAssignmentCommandHandler(db).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Externe, IncludeBoardingFee: false, rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task NonExterne_Without_A_RoomId_Is_Rejected_With_422()
    {
        // RoomId null n'est un état valide QUE pour Externe (sémantique "null = libération"). Un
        // Interne/DemiPensionnaire sans chambre serait un élève interne "en l'air", jamais compté nulle
        // part côté dashboard occupation.
        await using var db = _db.NewAppContext(EcoleA);
        var rowVersion = await CurrentRowVersionAsync(db);
        var act = () => new ChangeBoardingAssignmentCommandHandler(db).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, RoomId: null, BoardingStatus.Interne, IncludeBoardingFee: false, rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }
}
