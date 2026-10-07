using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

/// <summary>
/// Contrat HÉRITÉ d'affectation (<c>POST /api/v1/internat/assignments/{enrollmentId}</c>) — mêmes entrées, même sortie, mais
/// désormais écrit sur les séjours du modèle Pavillon/Lit (lot C). <c>RoomId</c> est un <c>DormitoryRoom.Id</c> ; le jeton
/// <c>RowVersion</c> reste le xmin de l'INSCRIPTION.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ChangeBoardingAssignmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("66666666-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("66666666-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("66666666-0000-0000-0000-00000000000b");
    private static readonly Guid Pavillon = Guid.Parse("66666666-0000-0000-0000-00000000000c");
    private static readonly Guid ChambreA = Guid.Parse("66666666-0000-0000-0000-00000000000d");
    private static readonly Guid ChambreB = Guid.Parse("66666666-0000-0000-0000-00000000000e");
    private static readonly Guid CatPension = Guid.Parse("66666666-0000-0000-0000-00000000000f");
    private static readonly Guid Eleve = Guid.Parse("66666666-0000-0000-0000-000000000010");
    private static readonly Guid Inscription = Guid.Parse("66666666-0000-0000-0000-000000000011");
    private static readonly Guid LitA = Guid.Parse("66666666-0000-0000-0000-0000000000a1");
    private static readonly Guid LitB = Guid.Parse("66666666-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Dormitories.Add(new Dormitory { Id = Pavillon, SchoolId = EcoleA, Name = "Pavillon A", Gender = DormitoryGender.Mixte });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = ChambreA, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre A" },
            new DormitoryRoom { Id = ChambreB, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre B" });
        // Chaque chambre n'a qu'UN lit (l'ancienne capacité de 1) : le dernier lit est le cas limite des tests.
        owner.Beds.AddRange(
            new Bed { Id = LitA, SchoolId = EcoleA, DormitoryRoomId = ChambreA, BedNumber = 1 },
            new Bed { Id = LitB, SchoolId = EcoleA, DormitoryRoomId = ChambreB, BedNumber = 1 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 15_000m, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<uint> CurrentRowVersionAsync(ApplicationDbContext db) =>
        await db.Enrollments.Where(e => e.Id == Inscription).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync();

    private static ChangeBoardingAssignmentCommandHandler HandlerFor(ApplicationDbContext db) =>
        new(db, new BoardingAssignmentService(db, TimeProvider.System), TimeProvider.System);

    private async Task<EnrollmentBoardingDto> ChangeAsync(
        Guid? room, BoardingStatus status, bool fee = false, uint? rowVersion = null, Guid? enrollment = null)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var token = rowVersion ?? await CurrentRowVersionAsync(db);
        return await HandlerFor(db).Handle(
            new ChangeBoardingAssignmentCommand(enrollment ?? Inscription, room, status, fee, token), CancellationToken.None);
    }

    /// <summary>Occupe l'unique lit d'une chambre avec un autre élève (séjour actif).</summary>
    private async Task OccupyAsync(Guid bed)
    {
        await using var setup = _db.NewOwnerContext();
        var autreEleve = Guid.NewGuid();
        var autreInscription = Guid.NewGuid();
        setup.Students.Add(new Student { Id = autreEleve, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Moussa Diop", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        setup.Enrollments.Add(new Enrollment { Id = autreInscription, SchoolId = EcoleA, StudentId = autreEleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow });
        setup.BoardingEnrollments.Add(new BoardingEnrollment { SchoolId = EcoleA, StudentId = autreEleve, EnrollmentId = autreInscription, Regime = BoardingRegime.Interne, BedId = bed, StartDate = new DateOnly(2026, 9, 15), IsActive = true });
        await setup.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Assigning_A_Room_Adds_The_Pension_Line_Once()
    {
        var result1 = await ChangeAsync(ChambreA, BoardingStatus.Interne, fee: true);

        result1.TotalDue.Should().Be(15_000m + 20_000m * 9);

        // Second changement de chambre (toujours Interne) : la pension ne doit PAS être doublée. Le RowVersion utilisé ici est
        // celui RENVOYÉ par le premier appel : un DTO qui renverrait un placeholder ferait échouer cet appel en conflit.
        var result2 = await ChangeAsync(ChambreB, BoardingStatus.Interne, fee: true, rowVersion: result1.RowVersion);

        result2.TotalDue.Should().Be(15_000m + 20_000m * 9);   // inchangé, pas de doublon
        result2.RoomId.Should().Be(ChambreB);
        result2.BoardingStatus.Should().Be(BoardingStatus.Interne);
    }

    [Fact]
    public async Task Releasing_A_Student_Keeps_The_Pension_Line_Due_And_Frees_The_Bed()
    {
        var assigned = await ChangeAsync(ChambreA, BoardingStatus.Interne, fee: true);

        var released = await ChangeAsync(null, BoardingStatus.Externe, rowVersion: assigned.RowVersion);

        released.RoomId.Should().BeNull();
        released.BoardingStatus.Should().Be(BoardingStatus.Externe);
        released.TotalDue.Should().Be(15_000m + 20_000m * 9);   // la pension déjà facturée reste due (spec du 18/09, décision #7)

        await using var check = _db.NewAppContext(EcoleA);
        var stay = await check.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.EnrollmentId == Inscription);
        (stay.IsActive, stay.BedId).Should().Be((false, (Guid?)null));
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422()
    {
        await OccupyAsync(LitA);

        var act = () => ChangeAsync(ChambreA, BoardingStatus.Interne);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors["RoomId"]
            .Should().Contain("Cette chambre a atteint sa capacité maximale.");
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Rejected_With_Concurrency_Conflict()
    {
        await using var db1 = _db.NewAppContext(EcoleA);
        var staleRowVersion = await CurrentRowVersionAsync(db1);

        // Un autre changement passe en premier : l'inscription est « touchée », son xmin avance.
        await ChangeAsync(ChambreA, BoardingStatus.Interne);

        // Le premier appelant, avec le jeton PÉRIMÉ, doit être rejeté en conflit — jamais un écrasement silencieux.
        var act = () => ChangeAsync(ChambreB, BoardingStatus.Interne, rowVersion: staleRowVersion);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Every_Assignment_Moves_The_Enrollment_Token_Even_When_Nothing_Else_Changes()
    {
        var first = await ChangeAsync(ChambreA, BoardingStatus.Interne);

        var again = await ChangeAsync(ChambreA, BoardingStatus.Interne, rowVersion: first.RowVersion);

        again.RowVersion.Should().NotBe(first.RowVersion, "l'ancien écran s'appuie sur ce jeton pour détecter un dossier modifié");
    }

    [Fact]
    public async Task Reassigning_The_Same_Student_To_The_Same_Full_Room_Is_Not_Rejected()
    {
        // La chambre n'a qu'un lit. Confirmer la même chambre pour le MÊME élève ne doit jamais être rejeté pour « chambre
        // complète » : il occupe déjà ce lit.
        var result1 = await ChangeAsync(ChambreA, BoardingStatus.Interne);

        var act = () => ChangeAsync(ChambreA, BoardingStatus.Interne, rowVersion: result1.RowVersion);

        await act.Should().NotThrowAsync();
        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().CountAsync(b => b.EnrollmentId == Inscription && b.IsActive)).Should().Be(1);
    }

    [Fact]
    public async Task Externe_With_A_RoomId_Is_Rejected_With_422()
    {
        var act = () => ChangeAsync(ChambreA, BoardingStatus.Externe);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task Interne_Without_A_RoomId_Is_Rejected_With_422()
    {
        var act = () => ChangeAsync(null, BoardingStatus.Interne);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task A_Half_Boarder_Without_A_Room_Is_Accepted_And_Takes_No_Bed()
    {
        var result = await ChangeAsync(null, BoardingStatus.DemiPensionnaire);

        (result.BoardingStatus, result.RoomId).Should().Be((BoardingStatus.DemiPensionnaire, (Guid?)null));
        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.EnrollmentId == Inscription)).BedId.Should().BeNull();
    }

    [Fact]
    public async Task A_Room_Sent_With_The_Half_Board_Regime_Is_Ignored()
    {
        // L'ancien écran envoie encore une chambre : elle est ignorée, le demi-pensionnaire n'occupe aucun lit.
        await OccupyAsync(LitA);

        var result = await ChangeAsync(ChambreA, BoardingStatus.DemiPensionnaire);

        (result.BoardingStatus, result.RoomId).Should().Be((BoardingStatus.DemiPensionnaire, (Guid?)null));
    }

    [Fact]
    public async Task Moving_To_Another_Room_Frees_The_Previous_Bed()
    {
        var first = await ChangeAsync(ChambreA, BoardingStatus.Interne);
        await ChangeAsync(ChambreB, BoardingStatus.Interne, rowVersion: first.RowVersion);

        await OccupyAsync(LitA);   // le lit de l'ancienne chambre est de nouveau libre

        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.EnrollmentId == Inscription)).BedId.Should().Be(LitB);
    }

    [Fact]
    public async Task A_Room_Created_By_The_New_Api_Can_Be_Assigned_Through_The_Legacy_Contract()
    {
        var pavillonApi = Guid.NewGuid();
        var chambreApi = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Dormitories.Add(new Dormitory { Id = pavillonApi, SchoolId = EcoleA, Name = "Pavillon API", Gender = DormitoryGender.Filles });
            owner.DormitoryRooms.Add(new DormitoryRoom { Id = chambreApi, SchoolId = EcoleA, DormitoryId = pavillonApi, Name = "Chambre API" });
            owner.Beds.Add(new Bed { SchoolId = EcoleA, DormitoryRoomId = chambreApi, BedNumber = 1 });
            await owner.SaveChangesAsync();
        }

        var result = await ChangeAsync(chambreApi, BoardingStatus.Interne);

        result.RoomId.Should().Be(chambreApi);
    }

    [Fact]
    public async Task A_Boy_Cannot_Be_Assigned_Through_The_Legacy_Contract_To_A_Girls_Dormitory()
    {
        var pavillonFilles = Guid.NewGuid();
        var chambreFilles = Guid.NewGuid();
        var garcon = Guid.NewGuid();
        var inscriptionGarcon = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Dormitories.Add(new Dormitory { Id = pavillonFilles, SchoolId = EcoleA, Name = "Pavillon Filles", Gender = DormitoryGender.Filles });
            owner.DormitoryRooms.Add(new DormitoryRoom { Id = chambreFilles, SchoolId = EcoleA, DormitoryId = pavillonFilles, Name = "Chambre F" });
            owner.Beds.Add(new Bed { SchoolId = EcoleA, DormitoryRoomId = chambreFilles, BedNumber = 1 });
            owner.Students.Add(new Student { Id = garcon, SchoolId = EcoleA, Matricule = "ELEV-0009", FullName = "Moussa Ba", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
            owner.Enrollments.Add(new Enrollment { Id = inscriptionGarcon, SchoolId = EcoleA, StudentId = garcon, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0009", EnrolledAt = DateTimeOffset.UtcNow });
            await owner.SaveChangesAsync();
        }

        var act = async () =>
        {
            await using var db = _db.NewAppContext(EcoleA);
            var token = await db.Enrollments.Where(e => e.Id == inscriptionGarcon).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync();
            return await HandlerFor(db).Handle(
                new ChangeBoardingAssignmentCommand(inscriptionGarcon, chambreFilles, BoardingStatus.Interne, false, token),
                CancellationToken.None);
        };

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));
    }

    [Fact]
    public async Task The_Legacy_Enrollment_Columns_Are_Never_Written()
    {
        await ChangeAsync(ChambreA, BoardingStatus.Interne);

        await using var check = _db.NewAppContext(EcoleA);
        var enrollment = await check.Enrollments.AsNoTracking().SingleAsync(e => e.Id == Inscription);
#pragma warning disable CS0618
        (enrollment.BoardingStatus, enrollment.RoomId).Should().Be((BoardingStatus.Externe, (Guid?)null));
#pragma warning restore CS0618
    }
}
