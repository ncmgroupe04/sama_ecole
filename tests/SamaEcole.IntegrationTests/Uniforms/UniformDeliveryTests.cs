using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Application.Uniforms;
using SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;
using SamaEcole.Application.Uniforms.Commands.DeliverStudentUniform;
using SamaEcole.Application.Uniforms.Commands.ReturnStudentUniform;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 4 — la REMISE et le RETOUR d'une tenue : la brique la plus critique du module, parce qu'elle
/// fait bouger du stock et qu'un inventaire dont le journal ne recolle plus avec les compteurs n'est plus opposable.
///
/// Ce que ces tests prouvent, sur PostgreSQL réel et sous le rôle applicatif bridé :
///   • la remise écrit, dans UNE transaction, le passage de la ligne à « remise » ET la sortie de stock au journal
///     append-only (via <c>StockLedger</c>, jamais en écrivant les compteurs à la main) ;
///   • deux remises qui se disputent la DERNIÈRE unité ne réussissent jamais toutes les deux : la seconde reçoit un 409
///     (verrou xmin du lot) ou un 422 (stock déjà épuisé quand elle lit), et ne laisse AUCUNE écriture à moitié faite ;
///   • le retour écrit le mouvement inverse et rend la tenue au stock ;
///   • une dotation ne se remet pas deux fois, et un jeton périmé est refusé en 409 (règle #5).
/// </summary>
[Trait("Category", "MultiTenant")]
public class UniformDeliveryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Acteur = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateOnly Aujourdhui = new(2026, 10, 5);

    private static readonly Guid AnneeA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c1");
    private static readonly Guid FraisUniforme = Guid.Parse("f0000000-0000-0000-0000-0000000000a1");
    private static readonly Guid CategorieTenues = Guid.Parse("c0000000-0000-0000-0000-0000000000a1");

    // Trois élèves, chacun avec son inscription qui porte la ligne de frais « Uniforme ».
    private static readonly Guid[] Eleves =
    [
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000e1"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000e2"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000e3")
    ];

    private static readonly Guid[] Inscriptions =
    [
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f1"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f2"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f3")
    ];

    private static readonly Guid InscriptionB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000f1");
    private static readonly Guid ModeleB = Guid.Parse("a0000000-0000-0000-0000-0000000000b1");

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
    }

    /// <summary>Un modèle avec une taille et son lot, prêts à être remis.</summary>
    private sealed record Stocked(Guid ItemId, Guid VariantId, Guid LotId);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.FeeCategories.Add(new FeeCategory { Id = FraisUniforme, SchoolId = EcoleA, Name = "Uniforme", IsOptional = true });
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieTenues, SchoolId = EcoleA, Name = "Tenues" });

        for (var i = 0; i < Eleves.Length; i++)
        {
            owner.Students.Add(new Student
            {
                Id = Eleves[i], SchoolId = EcoleA, Matricule = $"ELEV-A-000{i + 1}", FullName = $"Élève {i + 1}",
                BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
            });
            owner.Enrollments.Add(new Enrollment
            {
                Id = Inscriptions[i], SchoolId = EcoleA, StudentId = Eleves[i], SchoolYearId = AnneeA, ClassroomId = ClasseA,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 170_000m,
                AmountPaid = 50_000m, ReceiptNumber = $"REC-A-000{i + 1}", EnrolledAt = DateTimeOffset.UtcNow
            });
            owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
            {
                SchoolId = EcoleA, EnrollmentId = Inscriptions[i], FeeCategoryId = FraisUniforme, Designation = "Uniforme",
                UnitAmount = 25_000m, Months = 1, LineTotal = 25_000m
            });
        }

        // École B : une dotation à remettre, que l'école A ne doit jamais pouvoir toucher.
        var anneeB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
        var classeB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000c1");
        var eleveB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000e1");
        var categorieB = new InventoryCategory { SchoolId = EcoleB, Name = "Tenues" };
        var lotB = new InventoryItem
        {
            SchoolId = EcoleB, Name = "Polo — 10 ans", CategoryId = categorieB.Id, IsConsumable = true,
            QuantityTotal = 5, QuantityAvailable = 5
        };
        var varianteB = Guid.Parse("b0000000-0000-0000-0000-0000000000b2");

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = anneeB, SchoolId = EcoleB, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom { Id = classeB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = eleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Modou Diop",
            BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = classeB
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = InscriptionB, SchoolId = EcoleB, StudentId = eleveB, SchoolYearId = anneeB, ClassroomId = classeB,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100_000m,
            ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.InventoryCategories.Add(categorieB);
        owner.InventoryItems.Add(lotB);
        owner.UniformItems.Add(new UniformItem { Id = ModeleB, SchoolId = EcoleB, Name = "Polo" });
        owner.UniformVariants.Add(new UniformVariant
        {
            Id = varianteB, SchoolId = EcoleB, UniformItemId = ModeleB, SizeLabel = "10 ans", InventoryItemId = lotB.Id
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ aides

    /// <summary>Un modèle facturé par « Uniforme », une taille et son lot contenant <paramref name="stock"/> unités.</summary>
    private async Task<Stocked> SeedModelAsync(string name, int stock, bool billed = true)
    {
        await using var owner = _db.NewOwnerContext();

        var lot = new InventoryItem
        {
            SchoolId = EcoleA, Name = $"{name} — 10 ans", CategoryId = CategorieTenues, IsConsumable = true,
            QuantityTotal = stock, QuantityAvailable = stock
        };
        var item = new UniformItem { SchoolId = EcoleA, Name = name, FeeCategoryId = billed ? FraisUniforme : null };
        var variant = new UniformVariant { SchoolId = EcoleA, UniformItemId = item.Id, SizeLabel = "10 ans", InventoryItemId = lot.Id };

        owner.InventoryItems.Add(lot);
        owner.UniformItems.Add(item);
        owner.UniformVariants.Add(variant);
        await owner.SaveChangesAsync(CancellationToken.None);

        return new Stocked(item.Id, variant.Id, lot.Id);
    }

    private async Task<StudentUniformDto> RequestAsync(Guid enrollmentId, Stocked model, bool withSize = true, int quantity = 1)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return (await new CreateStudentUniformsCommandHandler(ctx, new FixedTenantProvider(EcoleA)).Handle(
            new CreateStudentUniformsCommand
            {
                EnrollmentId = enrollmentId,
                Lines = [new StudentUniformRequestLine(model.ItemId, withSize ? model.VariantId : null, quantity)]
            },
            CancellationToken.None)).Single();
    }

    private DeliverStudentUniformCommandHandler Deliver(ApplicationDbContext ctx) =>
        new(ctx, new TestCurrentUser(Acteur, Role.Secretariat), new FixedClock(Aujourdhui));

    private ReturnStudentUniformCommandHandler Return(ApplicationDbContext ctx) =>
        new(ctx, new TestCurrentUser(Acteur, Role.Secretariat), new FixedClock(Aujourdhui));

    private async Task<StudentUniformDto> DeliverAsync(StudentUniformDto line, string? reason = null)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await Deliver(ctx).Handle(
            new DeliverStudentUniformCommand { Id = line.Id, RowVersion = line.RowVersion, NotBilledReason = reason },
            CancellationToken.None);
    }

    private async Task<(int Total, int Available, List<StockMovement> Journal)> LotStateAsync(Guid lotId)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var lot = await ctx.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == lotId);
        var journal = await ctx.StockMovements.AsNoTracking().Where(m => m.ItemId == lotId).OrderBy(m => m.CreatedAt).ToListAsync();
        return (lot.QuantityTotal, lot.QuantityAvailable, journal);
    }

    private async Task<StudentUniform> StoredAsync(Guid lineId)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.StudentUniforms.AsNoTracking().SingleAsync(u => u.Id == lineId);
    }

    // ------------------------------------------------------------------ remise

    [Fact]
    public async Task Delivering_A_Billed_Line_Marks_It_Delivered_And_Writes_One_Exit_To_The_Journal()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo);

        var delivered = await DeliverAsync(line);

        delivered.Status.Should().Be("Delivered");
        delivered.DeliveredOn.Should().Be(Aujourdhui, "la date vient de l'horloge du serveur, jamais du client");
        delivered.BillingStatus.Should().Be("Billed");
        delivered.StockAvailable.Should().Be(4);

        var (total, available, journal) = await LotStateAsync(polo.LotId);
        total.Should().Be(4, "une tenue remise sort définitivement du patrimoine : total ET disponible baissent");
        available.Should().Be(4);
        journal.Should().ContainSingle();
        journal[0].Type.Should().Be(StockMovementType.Sortie, "ce n'est pas un prêt : la tenue ne revient pas");
        journal[0].Quantity.Should().Be(1);
        journal[0].QuantityTotalAfter.Should().Be(4);
        journal[0].QuantityAvailableAfter.Should().Be(4);
        journal[0].Reason.Should().Contain("Polo").And.Contain("Élève 1");
        journal[0].AssignmentId.Should().BeNull();

        var stored = await StoredAsync(line.Id);
        stored.DeliveredByUserId.Should().Be(Acteur);
        stored.DeliveryStockMovementId.Should().Be(journal[0].Id, "la ligne désigne le mouvement qu'elle a produit");
        stored.FeeLineId.Should().NotBeNull("la preuve de facturation est la ligne de frais de l'inscription");
        stored.ItemLabel.Should().Be("Polo");
        stored.SizeLabel.Should().Be("10 ans");
        stored.StudentLabel.Should().Contain("Élève 1");
    }

    [Fact]
    public async Task Delivering_Never_Writes_To_Finance()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using var verify = _db.NewAppContext(EcoleA);
        var enrollment = await verify.Enrollments.SingleAsync(e => e.Id == Inscriptions[0]);

        enrollment.TotalDue.Should().Be(170_000m);
        enrollment.AmountPaid.Should().Be(50_000m);
        (await verify.Payments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_Not_Billed_Delivery_Needs_A_Reason_And_Writes_Nothing_Without_One()
    {
        var casquette = await SeedModelAsync("Casquette", stock: 5, billed: false);
        var line = await RequestAsync(Inscriptions[0], casquette);

        var act = () => DeliverAsync(line, reason: "  ");

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("notBilledReason");

        (await StoredAsync(line.Id)).Status.Should().Be(StudentUniformStatus.Requested);
        var (total, available, journal) = await LotStateAsync(casquette.LotId);
        (total, available).Should().Be((5, 5));
        journal.Should().BeEmpty("un refus ne laisse aucune ligne au journal");
    }

    [Fact]
    public async Task A_Not_Billed_Delivery_With_A_Reason_Freezes_It()
    {
        var casquette = await SeedModelAsync("Casquette", stock: 5, billed: false);
        var line = await RequestAsync(Inscriptions[0], casquette);

        var delivered = await DeliverAsync(line, reason: "  Dotation de la mairie  ");

        delivered.BillingStatus.Should().Be("NotBilled");

        var stored = await StoredAsync(line.Id);
        stored.NotBilledReason.Should().Be("Dotation de la mairie");
        stored.FeeLineId.Should().BeNull();
    }

    [Fact]
    public async Task A_Reason_Sent_With_A_Billed_Delivery_Is_Dropped()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo);

        await DeliverAsync(line, reason: "inutile");

        (await StoredAsync(line.Id)).NotBilledReason.Should().BeNull();
    }

    [Fact]
    public async Task A_Quantity_Beyond_The_Fee_Is_Delivered_As_Not_Billed_And_Needs_A_Reason()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo, quantity: 2); // le frais ne couvre qu'une unité

        var refused = () => DeliverAsync(line);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("notBilledReason");

        var delivered = await DeliverAsync(line, reason: "Deuxième polo offert");
        delivered.BillingStatus.Should().Be("NotBilled");
        (await LotStateAsync(polo.LotId)).Available.Should().Be(3, "deux unités sont sorties");
    }

    [Fact]
    public async Task A_Delivery_Without_A_Confirmed_Size_Is_Refused()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo, withSize: false);

        var act = () => DeliverAsync(line);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("uniformVariantId");
    }

    [Fact]
    public async Task A_Line_Cannot_Be_Delivered_Twice_And_The_Stock_Leaves_Only_Once()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo);
        var delivered = await DeliverAsync(line);

        await using var ctx = _db.NewAppContext(EcoleA);
        var again = () => Deliver(ctx).Handle(
            new DeliverStudentUniformCommand { Id = line.Id, RowVersion = delivered.RowVersion }, CancellationToken.None);

        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_NOT_DELIVERABLE");

        var (_, available, journal) = await LotStateAsync(polo.LotId);
        available.Should().Be(4);
        journal.Should().ContainSingle();
    }

    [Fact]
    public async Task Not_Enough_Stock_Is_A_422_That_Leaves_The_Line_And_The_Journal_Untouched()
    {
        var polo = await SeedModelAsync("Polo", stock: 1);
        var line = await RequestAsync(Inscriptions[0], polo, quantity: 1);
        await RequestAsync(Inscriptions[1], polo); // un second besoin sur le même lot

        await using (var owner = _db.NewOwnerContext())
        {
            // Le stock est tombé à zéro (casse, recomptage) avant la remise.
            var lot = await owner.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.Id == polo.LotId);
            lot.QuantityTotal = 0;
            lot.QuantityAvailable = 0;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = () => DeliverAsync(line, reason: null);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Values.SelectMany(m => m)
            .Should().Contain(m => m.Contains("Quantité insuffisante", StringComparison.Ordinal));

        (await StoredAsync(line.Id)).Status.Should().Be(StudentUniformStatus.Requested);
        (await LotStateAsync(polo.LotId)).Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Delivery_With_A_Stale_Line_Token_Is_Refused_And_The_Stock_Does_Not_Move()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var line = await RequestAsync(Inscriptions[0], polo);

        // Quelqu'un change la taille entre la lecture de l'écran et la remise : le jeton de la ligne est périmé.
        await using (var other = _db.NewAppContext(EcoleA))
        {
            var row = await other.StudentUniforms.SingleAsync(u => u.Id == line.Id);
            row.Quantity = 1;
            row.SizeLabel = "10 ans (modifié)";
            await other.SaveChangesAsync(CancellationToken.None);
        }

        var act = () => DeliverAsync(line);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        (await StoredAsync(line.Id)).Status.Should().Be(StudentUniformStatus.Requested);
        var (total, available, journal) = await LotStateAsync(polo.LotId);
        (total, available).Should().Be((5, 5));
        journal.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_Schools_Line_Is_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => Deliver(ctx).Handle(
            new DeliverStudentUniformCommand { Id = Guid.NewGuid(), RowVersion = 1 }, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ la dernière unité

    [Fact]
    public async Task Two_Deliveries_Racing_For_The_Last_Unit_Never_Both_Succeed()
    {
        // Plusieurs manches : une seule ne prouverait rien, la course n'étant pas déterministe. À chaque manche, un lot
        // d'UNE unité et deux élèves qui attendent la même tenue, remise en parallèle sur deux contextes.
        const int rounds = 6;

        for (var round = 1; round <= rounds; round++)
        {
            var model = await SeedModelAsync($"Polo manche {round}", stock: 1);
            var first = await RequestAsync(Inscriptions[0], model);
            var second = await RequestAsync(Inscriptions[1], model);

            await using var ctxA = _db.NewAppContext(EcoleA);
            await using var ctxB = _db.NewAppContext(EcoleA);

            var results = await Task.WhenAll(
                AttemptAsync(ctxA, first),
                AttemptAsync(ctxB, second));

            var winners = results.Count(r => r is null);
            var losers = results.Where(r => r is not null).ToList();

            winners.Should().Be(1, $"manche {round} : exactement une remise réussit, jamais deux ni aucune");
            losers.Should().ContainSingle();
            losers[0].Should().Match<Exception>(
                e => e is ConcurrencyConflictException || e is ValidationException,
                "la perdante reçoit un 409 (verrou xmin du lot) ou un 422 (stock déjà épuisé), jamais un 500");

            var (total, available, journal) = await LotStateAsync(model.LotId);
            (total, available).Should().Be((0, 0), $"manche {round} : le stock ne descend jamais sous zéro");
            journal.Should().ContainSingle(m => m.Type == StockMovementType.Sortie,
                "un seul mouvement : la perdante n'a laissé aucune écriture à moitié faite");

            await using var verify = _db.NewAppContext(EcoleA);
            (await verify.StudentUniforms.CountAsync(u => u.UniformItemId == model.ItemId && u.Status == StudentUniformStatus.Delivered))
                .Should().Be(1);
            (await verify.StudentUniforms.CountAsync(u => u.UniformItemId == model.ItemId && u.Status == StudentUniformStatus.Requested))
                .Should().Be(1, "la perdante reste à remettre, intacte");
        }
    }

    private async Task<Exception?> AttemptAsync(ApplicationDbContext ctx, StudentUniformDto line)
    {
        try
        {
            // Task.Run : les deux remises démarrent vraiment en même temps, sur deux connexions.
            await Task.Run(() => Deliver(ctx).Handle(
                new DeliverStudentUniformCommand { Id = line.Id, RowVersion = line.RowVersion }, CancellationToken.None));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task A_Delivery_Overtaken_Between_Its_Read_And_Its_Write_Is_A_Clean_409_And_Writes_Nothing()
    {
        // Le cas que la course ci-dessus ne garantit pas d'atteindre : on FORCE l'ordre. Le handler a lu le lot (1 unité
        // disponible) ; AVANT qu'il n'écrive, une autre remise sort cette unité et valide. Le stock lu est périmé : le
        // verrou xmin du lot doit refuser l'écriture, sinon le stock passerait à -1 sans que rien ne le signale.
        var model = await SeedModelAsync("Polo", stock: 1);
        var mine = await RequestAsync(Inscriptions[0], model);

        await using var ctx = _db.NewAppContext(EcoleA);
        var overtaken = false;

        ctx.SavingChanges += (_, _) =>
        {
            if (overtaken) return;
            overtaken = true;

            using var other = _db.NewAppContext(EcoleA);
            var lot = other.InventoryItems.Single(i => i.Id == model.LotId);
            other.StockMovements.Add(StockLedger.Apply(
                lot, StockMovementType.Sortie, 1, Aujourdhui, "Remise concurrente d'un autre guichet"));
            other.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        };

        var act = () => Deliver(ctx).Handle(
            new DeliverStudentUniformCommand { Id = mine.Id, RowVersion = mine.RowVersion }, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        (await StoredAsync(mine.Id)).Status.Should().Be(StudentUniformStatus.Requested, "la remise refusée n'est pas à moitié faite");
        var (total, available, journal) = await LotStateAsync(model.LotId);
        (total, available).Should().Be((0, 0));
        journal.Should().ContainSingle("seul le mouvement de l'autre guichet existe");
        journal[0].Reason.Should().Contain("autre guichet");
    }

    // ------------------------------------------------------------------ retour

    [Fact]
    public async Task Returning_A_Delivered_Line_Writes_The_Inverse_Entry_And_Gives_The_Unit_Back()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var delivered = await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var returned = await Return(ctx).Handle(
                new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = "  Mauvaise taille  " },
                CancellationToken.None);

            returned.Status.Should().Be("Returned");
            returned.StockAvailable.Should().Be(5);
        }

        var (total, available, journal) = await LotStateAsync(polo.LotId);
        (total, available).Should().Be((5, 5), "la tenue est revenue au stock");
        journal.Select(m => m.Type).Should().Equal(StockMovementType.Sortie, StockMovementType.Entree);
        journal[1].Quantity.Should().Be(1);
        journal[1].Reason.Should().Contain("Mauvaise taille");

        var stored = await StoredAsync(delivered.Id);
        stored.ReturnReason.Should().Be("Mauvaise taille");
        stored.ReturnStockMovementId.Should().Be(journal[1].Id);
        stored.DeliveryStockMovementId.Should().Be(journal[0].Id, "la remise d'origine reste attachée à la ligne");

        // Le journal raconte l'histoire complète : le rejouer redonne exactement les compteurs.
        var replayed = journal.Sum(m => m.Type == StockMovementType.Entree ? m.Quantity : -m.Quantity);
        (5 + replayed).Should().Be(available);
    }

    [Fact]
    public async Task After_A_Return_The_Student_Can_Receive_The_Item_Again_In_Another_Size()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var delivered = await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await Return(ctx).Handle(
                new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = "Échange" },
                CancellationToken.None);
        }

        // Un échange = retour puis nouvelle ligne : la ligne retournée n'occupe plus la place « active ».
        var again = await RequestAsync(Inscriptions[0], polo);
        again.Status.Should().Be("Requested");
    }

    [Fact]
    public async Task A_Return_Needs_A_Reason_And_A_Delivered_Line()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var requested = await RequestAsync(Inscriptions[0], polo);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var notDelivered = () => Return(ctx).Handle(
                new ReturnStudentUniformCommand { Id = requested.Id, RowVersion = requested.RowVersion, Reason = "motif" },
                CancellationToken.None);
            (await notDelivered.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_NOT_RETURNABLE");
        }

        var delivered = await DeliverAsync(requested);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var noReason = () => Return(ctx).Handle(
                new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = " " },
                CancellationToken.None);
            (await noReason.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("reason");
        }

        (await LotStateAsync(polo.LotId)).Journal.Should().ContainSingle("aucun retour n'a été enregistré");
    }

    [Fact]
    public async Task A_Line_Cannot_Be_Returned_Twice_And_The_Stock_Comes_Back_Only_Once()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var delivered = await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        uint afterReturn;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            afterReturn = (await Return(ctx).Handle(
                new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = "Échange" },
                CancellationToken.None)).RowVersion;
        }

        await using var second = _db.NewAppContext(EcoleA);
        var again = () => Return(second).Handle(
            new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = afterReturn, Reason = "Encore" }, CancellationToken.None);

        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_NOT_RETURNABLE");
        (await LotStateAsync(polo.LotId)).Available.Should().Be(5);
    }

    [Fact]
    public async Task A_Return_With_A_Stale_Token_Is_Refused_And_The_Stock_Does_Not_Move()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        var delivered = await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using (var other = _db.NewAppContext(EcoleA))
        {
            var row = await other.StudentUniforms.SingleAsync(u => u.Id == delivered.Id);
            row.StudentLabel = "Libellé corrigé par un autre";
            await other.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => Return(ctx).Handle(
            new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = "Échange" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        (await StoredAsync(delivered.Id)).Status.Should().Be(StudentUniformStatus.Delivered);
        var (_, available, journal) = await LotStateAsync(polo.LotId);
        available.Should().Be(4);
        journal.Should().ContainSingle();
    }

    [Fact]
    public async Task A_Return_Still_Works_After_The_Size_Was_Archived()
    {
        // Le lot est retrouvé par le MOUVEMENT de remise, pas par la taille : archiver la taille après coup n'empêche pas
        // de reprendre une tenue déjà remise.
        var polo = await SeedModelAsync("Polo", stock: 5);
        var delivered = await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.UniformVariants.IgnoreQueryFilters().SingleAsync(v => v.Id == polo.VariantId)).SoftDelete("test");
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var returned = await Return(ctx).Handle(
            new ReturnStudentUniformCommand { Id = delivered.Id, RowVersion = delivered.RowVersion, Reason = "Taille abandonnée" },
            CancellationToken.None);

        returned.Status.Should().Be("Returned");
        (await LotStateAsync(polo.LotId)).Available.Should().Be(5);
    }

    [Fact]
    public async Task The_Journal_Stays_Append_Only_For_The_Application_Role()
    {
        var polo = await SeedModelAsync("Polo", stock: 5);
        await DeliverAsync(await RequestAsync(Inscriptions[0], polo));

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE "stock_movements" SET "Quantity" = 99 WHERE "SchoolId" = @ecole;""";
        command.Parameters.AddWithValue("ecole", EcoleA);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState
            .Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege, "une erreur se corrige par un mouvement inverse, jamais par une rature");
    }
}
