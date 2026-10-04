using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Uniforms;
using SamaEcole.Application.Uniforms.Commands.CancelStudentUniform;
using SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;
using SamaEcole.Application.Uniforms.Commands.UpdateStudentUniform;
using SamaEcole.Application.Uniforms.Queries.GetStudentUniforms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 3 — les dotations (besoins) d'un élève : création, modification, annulation et lecture, sur
/// PostgreSQL réel et sous le rôle applicatif bridé.
///
/// Ce que ces tests protègent :
///   • le verrou optimiste xmin (règle #5) : deux personnes qui modifient la même ligne ne s'écrasent pas ;
///   • la portée tenant : l'inscription, le modèle ou la taille d'une autre école sont introuvables ;
///   • l'atomicité : une demande à plusieurs lignes est enregistrée en entier ou pas du tout ;
///   • la facturation ATTENDUE, lue dans les lignes de frais de l'inscription sans jamais rien écrire dans Finance ;
///   • le badge de solde, qui informe sans jamais bloquer (arbitrage 2).
/// </summary>
[Trait("Category", "MultiTenant")]
public class UniformRequestsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid AnneeA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000e1");
    private static readonly Guid InscriptionA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f1");
    private static readonly Guid InscriptionAnnuleeA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f2");

    private static readonly Guid FraisUniforme = Guid.Parse("f0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid FraisSport = Guid.Parse("f0000000-0000-0000-0000-0000000000a2");

    private static readonly Guid Polo = Guid.Parse("a0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Jupe = Guid.Parse("a0000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Culotte = Guid.Parse("a0000000-0000-0000-0000-0000000000a3");
    private static readonly Guid Survetement = Guid.Parse("a0000000-0000-0000-0000-0000000000a4");
    private static readonly Guid Casquette = Guid.Parse("a0000000-0000-0000-0000-0000000000a5");

    private static readonly Guid Polo8 = Guid.Parse("b0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Polo10 = Guid.Parse("b0000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Jupe8 = Guid.Parse("b0000000-0000-0000-0000-0000000000a3");

    private static readonly Guid EleveB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000e1");
    private static readonly Guid InscriptionB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000f1");
    private static readonly Guid PoloB = Guid.Parse("a0000000-0000-0000-0000-0000000000b1");

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        SeedSchoolA(owner);
        SeedSchoolB(owner);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static void SeedSchoolA(ApplicationDbContext owner)
    {
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom
        {
            Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40
        });
        owner.Students.Add(new Student
        {
            Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
        });

        owner.Enrollments.AddRange(
            new Enrollment
            {
                Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 170_000m, AmountPaid = 50_000m, ReceiptNumber = "REC-A-0001", EnrolledAt = DateTimeOffset.UtcNow
            },
            new Enrollment
            {
                Id = InscriptionAnnuleeA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Cancelled,
                TotalDue = 0m, AmountPaid = 0m, ReceiptNumber = "REC-A-0002", EnrolledAt = DateTimeOffset.UtcNow
            });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = FraisUniforme, SchoolId = EcoleA, Name = "Uniforme", IsOptional = true },
            new FeeCategory { Id = FraisSport, SchoolId = EcoleA, Name = "Tenue de sport", IsOptional = true });

        // La famille a coché « Uniforme » à l'inscription, et décoché « Tenue de sport » : une seule ligne de frais.
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = EcoleA, EnrollmentId = InscriptionA, FeeCategoryId = FraisUniforme, Designation = "Uniforme",
            UnitAmount = 25_000m, Months = 1, LineTotal = 25_000m
        });

        var categorie = new InventoryCategory { SchoolId = EcoleA, Name = "Tenues" };
        owner.InventoryCategories.Add(categorie);

        InventoryItem Lot(string name, int available) => new()
        {
            SchoolId = EcoleA, Name = name, CategoryId = categorie.Id, IsConsumable = true,
            QuantityTotal = available, QuantityAvailable = available
        };

        var lotPolo8 = Lot("Polo — 8 ans", 3);
        var lotPolo10 = Lot("Polo — 10 ans", 5);
        var lotJupe8 = Lot("Jupe — 8 ans", 0);
        owner.InventoryItems.AddRange(lotPolo8, lotPolo10, lotJupe8);

        owner.UniformItems.AddRange(
            new UniformItem { Id = Polo, SchoolId = EcoleA, Name = "Polo", FeeCategoryId = FraisUniforme, QuantityIncluded = 2 },
            new UniformItem { Id = Jupe, SchoolId = EcoleA, Name = "Jupe", Gender = UniformGender.Fille, FeeCategoryId = FraisUniforme },
            new UniformItem { Id = Culotte, SchoolId = EcoleA, Name = "Culotte", Gender = UniformGender.Garcon, FeeCategoryId = FraisUniforme },
            new UniformItem { Id = Survetement, SchoolId = EcoleA, Name = "Survêtement", Kind = UniformKind.TenueSport, FeeCategoryId = FraisSport },
            new UniformItem { Id = Casquette, SchoolId = EcoleA, Name = "Casquette", Kind = UniformKind.Accessoire });

        owner.UniformVariants.AddRange(
            new UniformVariant { Id = Polo8, SchoolId = EcoleA, UniformItemId = Polo, SizeLabel = "8 ans", SortOrder = 1, InventoryItemId = lotPolo8.Id },
            new UniformVariant { Id = Polo10, SchoolId = EcoleA, UniformItemId = Polo, SizeLabel = "10 ans", SortOrder = 2, InventoryItemId = lotPolo10.Id },
            new UniformVariant { Id = Jupe8, SchoolId = EcoleA, UniformItemId = Jupe, SizeLabel = "8 ans", SortOrder = 1, InventoryItemId = lotJupe8.Id });
    }

    private static void SeedSchoolB(ApplicationDbContext owner)
    {
        var annee = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
        var classe = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000c1");

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = annee, SchoolId = EcoleB, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom { Id = classe, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Modou Diop",
            BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = InscriptionB, SchoolId = EcoleB, StudentId = EleveB, SchoolYearId = annee, ClassroomId = classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100_000m,
            ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.UniformItems.Add(new UniformItem { Id = PoloB, SchoolId = EcoleB, Name = "Polo" });
    }

    // ------------------------------------------------------------------ aides

    private static StudentUniformRequestLine Line(Guid itemId, Guid? variantId = null, int quantity = 1) =>
        new(itemId, variantId, quantity);

    private async Task<IReadOnlyList<StudentUniformDto>> RequestAsync(
        Guid enrollmentId, params StudentUniformRequestLine[] lines)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new CreateStudentUniformsCommandHandler(ctx, new FixedTenantProvider(EcoleA)).Handle(
            new CreateStudentUniformsCommand { EnrollmentId = enrollmentId, Lines = lines }, CancellationToken.None);
    }

    private async Task<StudentUniformsResult> ReadAsync(Guid schoolId, Guid studentId)
    {
        await using var ctx = _db.NewAppContext(schoolId);
        return await new GetStudentUniformsQueryHandler(ctx).Handle(new GetStudentUniformsQuery(studentId), CancellationToken.None);
    }

    // ------------------------------------------------------------------ création

    [Fact]
    public async Task Requests_Are_Created_For_The_Enrollment_With_The_Student_And_Year_Copied_And_Billing_Expected()
    {
        var lines = await RequestAsync(InscriptionA,
            Line(Polo, Polo10, quantity: 1),
            Line(Survetement),
            Line(Casquette));

        lines.Should().HaveCount(3);
        lines.Should().OnlyContain(l => l.Status == "Requested" && l.EnrollmentId == InscriptionA && l.SchoolYearId == AnneeA);

        var polo = lines.Single(l => l.UniformItemId == Polo);
        polo.ItemName.Should().Be("Polo");
        polo.SizeLabel.Should().Be("10 ans");
        polo.StockAvailable.Should().Be(5);
        polo.BillingStatus.Should().Be("Billed", "la famille a coché « Uniforme » : l'inscription porte sa ligne de frais");
        polo.RowVersion.Should().BeGreaterThan(0u);

        lines.Single(l => l.UniformItemId == Survetement).BillingStatus.Should().Be("NotBilled",
            "« Tenue de sport » a été décoché à l'inscription : aucune ligne de frais");
        lines.Single(l => l.UniformItemId == Casquette).BillingStatus.Should().Be("NotBilled", "aucun frais n'est rattaché à ce modèle");
        lines.Single(l => l.UniformItemId == Survetement).UniformVariantId.Should().BeNull("taille à confirmer");

        await using var verify = _db.NewAppContext(EcoleA);
        var stored = await verify.StudentUniforms.SingleAsync(u => u.UniformItemId == Polo);
        stored.StudentId.Should().Be(EleveA);
        stored.ItemLabel.Should().Be("Polo");
        stored.StudentLabel.Should().Contain("Awa Fall");
    }

    [Fact]
    public async Task A_Quantity_Beyond_What_The_Fee_Covers_Is_Expected_Not_Billed()
    {
        // Le frais « Uniforme » couvre 2 polos : le troisième n'est pas facturé.
        var lines = await RequestAsync(InscriptionA, Line(Polo, Polo10, quantity: 3));

        lines.Single().BillingStatus.Should().Be("NotBilled");
    }

    [Fact]
    public async Task Creating_Requests_Never_Writes_To_Finance()
    {
        await RequestAsync(InscriptionA, Line(Polo, Polo10), Line(Casquette));

        await using var verify = _db.NewAppContext(EcoleA);
        var enrollment = await verify.Enrollments.SingleAsync(e => e.Id == InscriptionA);

        enrollment.TotalDue.Should().Be(170_000m, "Finance ne bouge jamais : règle #4");
        enrollment.AmountPaid.Should().Be(50_000m);
        (await verify.EnrollmentFeeLines.CountAsync(l => l.EnrollmentId == InscriptionA)).Should().Be(1);
        (await verify.Payments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Second_Active_Request_For_The_Same_Item_Is_Refused_And_The_Whole_Batch_Is_Rolled_Back()
    {
        await RequestAsync(InscriptionA, Line(Polo, Polo10));

        var act = () => RequestAsync(InscriptionA, Line(Casquette), Line(Polo, Polo8));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_ALREADY_REQUESTED");

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.StudentUniforms.CountAsync()).Should().Be(1,
            "la casquette de la même demande n'est pas enregistrée non plus : tout ou rien");
    }

    [Fact]
    public async Task A_Size_Of_Another_Item_Is_Refused()
    {
        var act = () => RequestAsync(InscriptionA, Line(Polo, Jupe8));

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("UniformVariantId");
    }

    [Fact]
    public async Task Another_Schools_Item_Or_Enrollment_Is_Not_Found()
    {
        var foreignItem = () => RequestAsync(InscriptionA, Line(PoloB));
        (await foreignItem.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("UniformItemId");

        var foreignEnrollment = () => RequestAsync(InscriptionB, Line(Polo));
        await foreignEnrollment.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_Cancelled_Enrollment_Takes_No_Request()
    {
        var act = () => RequestAsync(InscriptionAnnuleeA, Line(Polo));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ENROLLMENT_CANCELLED");
    }

    // ------------------------------------------------------------------ modification (xmin)

    [Fact]
    public async Task A_Requested_Line_Can_Change_Size_And_Quantity_But_Not_With_A_Stale_Token()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using (var first = _db.NewAppContext(EcoleA))
        {
            var updated = await new UpdateStudentUniformCommandHandler(first).Handle(
                new UpdateStudentUniformCommand
                {
                    Id = created.Id, UniformVariantId = Polo10, Quantity = 2, RowVersion = created.RowVersion
                },
                CancellationToken.None);

            updated.SizeLabel.Should().Be("10 ans");
            updated.Quantity.Should().Be(2);
            updated.RowVersion.Should().NotBe(created.RowVersion);
        }

        // Une autre personne, avec le jeton d'AVANT : refus franc, jamais un écrasement silencieux (règle #5).
        await using var second = _db.NewAppContext(EcoleA);
        var act = () => new UpdateStudentUniformCommandHandler(second).Handle(
            new UpdateStudentUniformCommand
            {
                Id = created.Id, UniformVariantId = Polo8, Quantity = 1, RowVersion = created.RowVersion
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.StudentUniforms.SingleAsync()).UniformVariantId.Should().Be(Polo10);
    }

    [Fact]
    public async Task The_Size_Can_Be_Cleared_Back_To_To_Be_Confirmed()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using var ctx = _db.NewAppContext(EcoleA);
        var updated = await new UpdateStudentUniformCommandHandler(ctx).Handle(
            new UpdateStudentUniformCommand
            {
                Id = created.Id, UniformVariantId = null, Quantity = 1, RowVersion = created.RowVersion
            },
            CancellationToken.None);

        updated.UniformVariantId.Should().BeNull();
        updated.SizeLabel.Should().BeNull();
    }

    [Fact]
    public async Task An_Update_Cannot_Move_A_Line_To_A_Size_Of_Another_Item()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new UpdateStudentUniformCommandHandler(ctx).Handle(
            new UpdateStudentUniformCommand
            {
                Id = created.Id, UniformVariantId = Jupe8, Quantity = 1, RowVersion = created.RowVersion
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ------------------------------------------------------------------ annulation

    [Fact]
    public async Task Cancelling_A_Requested_Line_Frees_The_Place_For_A_New_Request()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new CancelStudentUniformCommandHandler(ctx).Handle(
                new CancelStudentUniformCommand { Id = created.Id, RowVersion = created.RowVersion }, CancellationToken.None);
        }

        await using (var verify = _db.NewAppContext(EcoleA))
        {
            (await verify.StudentUniforms.SingleAsync()).Status.Should().Be(StudentUniformStatus.Cancelled,
                "une dotation s'annule par son statut : jamais d'effacement");
        }

        var again = await RequestAsync(InscriptionA, Line(Polo, Polo10));
        again.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Line_That_Is_Not_Requested_Can_Be_Neither_Cancelled_Nor_Edited()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new CancelStudentUniformCommandHandler(ctx).Handle(
                new CancelStudentUniformCommand { Id = created.Id, RowVersion = created.RowVersion }, CancellationToken.None);
        }

        uint current;
        await using (var read = _db.NewAppContext(EcoleA))
        {
            current = await read.StudentUniforms.AsNoTracking().Select(u => EF.Property<uint>(u, "xmin")).SingleAsync();
        }

        await using var cancel = _db.NewAppContext(EcoleA);
        var again = () => new CancelStudentUniformCommandHandler(cancel).Handle(
            new CancelStudentUniformCommand { Id = created.Id, RowVersion = current }, CancellationToken.None);
        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_NOT_CANCELLABLE");

        await using var edit = _db.NewAppContext(EcoleA);
        var update = () => new UpdateStudentUniformCommandHandler(edit).Handle(
            new UpdateStudentUniformCommand { Id = created.Id, UniformVariantId = Polo10, Quantity = 1, RowVersion = current },
            CancellationToken.None);
        (await update.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_NOT_EDITABLE");
    }

    [Fact]
    public async Task Cancelling_With_A_Stale_Token_Is_Refused()
    {
        var created = (await RequestAsync(InscriptionA, Line(Polo, Polo8))).Single();

        await using (var first = _db.NewAppContext(EcoleA))
        {
            await new UpdateStudentUniformCommandHandler(first).Handle(
                new UpdateStudentUniformCommand { Id = created.Id, UniformVariantId = Polo10, Quantity = 1, RowVersion = created.RowVersion },
                CancellationToken.None);
        }

        await using var stale = _db.NewAppContext(EcoleA);
        var act = () => new CancelStudentUniformCommandHandler(stale).Handle(
            new CancelStudentUniformCommand { Id = created.Id, RowVersion = created.RowVersion }, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ------------------------------------------------------------------ lecture

    [Fact]
    public async Task The_Student_View_Shows_The_Balance_Badge_Lines_And_Billed_Proposals_Only()
    {
        var before = await ReadAsync(EcoleA, EleveA);

        before.StudentName.Should().Be("Awa Fall");
        var enrollment = before.Enrollments.Single(e => e.EnrollmentId == InscriptionA);

        enrollment.SchoolYearLabel.Should().Be("2026-2027");
        enrollment.IsActiveYear.Should().BeTrue();
        enrollment.ClassroomName.Should().Be("CM2 A");
        enrollment.TotalDue.Should().Be(170_000m);
        enrollment.BalanceDue.Should().Be(120_000m);
        enrollment.HasUnpaidBalance.Should().BeTrue("le badge informe ; il ne bloque jamais la remise");
        enrollment.Lines.Should().BeEmpty();

        // Propositions : ce que la famille a RÉELLEMENT payé (ligne de frais « Uniforme ») et qui convient à l'élève.
        // Polo (mixte) et Jupe (fille) oui ; Culotte (garçon) non ; Survêtement (frais décoché) et Casquette (sans frais) non.
        enrollment.Proposals.Select(p => p.ItemName).Should().BeEquivalentTo("Polo", "Jupe");

        await RequestAsync(InscriptionA, Line(Polo, Polo10));

        var after = (await ReadAsync(EcoleA, EleveA)).Enrollments.Single(e => e.EnrollmentId == InscriptionA);

        after.Lines.Should().ContainSingle(l => l.ItemName == "Polo");
        after.Proposals.Select(p => p.ItemName).Should().BeEquivalentTo(["Jupe"], "le polo est déjà demandé");
    }

    [Fact]
    public async Task A_Fully_Paid_Enrollment_Shows_No_Balance_Badge_And_A_Cancelled_One_Is_Not_Listed()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var enrollment = await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == InscriptionA);
            enrollment.AmountPaid = enrollment.TotalDue;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var view = await ReadAsync(EcoleA, EleveA);

        view.Enrollments.Should().ContainSingle("l'inscription annulée n'est pas listée");
        view.Enrollments.Single().HasUnpaidBalance.Should().BeFalse();
        view.Enrollments.Single().BalanceDue.Should().Be(0m);
    }

    [Fact]
    public async Task Another_Schools_Student_Is_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new GetStudentUniformsQueryHandler(ctx).Handle(new GetStudentUniformsQuery(EleveB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
