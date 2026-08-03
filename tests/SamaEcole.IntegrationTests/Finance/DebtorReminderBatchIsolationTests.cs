using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;
using SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;
using SamaEcole.Application.Notifications;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Étape 5 — le job d'ancienneté (DebtorAgingHostedService) itère TOUTES les écoles sous
/// TenantProvider.RunAsSchoolAsync, un mécanisme neuf qui contourne la résolution JWT habituelle
/// (voir sa doc). Ces tests exercent GenerateDebtorReminderBatchesCommand/SendDebtorReminderBatchCommand
/// exactement comme le fait ce job — via un ITenantProvider fixé à une école, contre un vrai
/// PostgreSQL sous le rôle applicatif — pour prouver que la RLS + le Global Query Filter empêchent
/// structurellement un lot ou un débiteur de fuiter d'une école à l'autre.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DebtorReminderBatchIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly SchoolYearStart = Today.AddMonths(-6);

    private Guid _enrollmentA;
    private Guid _enrollmentB;
    private Guid _studentA;
    private Guid _studentB;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        foreach (var schoolId in new[] { EcoleA, EcoleB })
        {
            owner.Subscriptions.Add(new Subscription
            {
                SchoolId = schoolId,
                Plan = SubscriptionPlan.Premium,
                Status = SubscriptionStatus.Active,
                ExpiresAt = new DateOnly(2027, 1, 1)
            });

            owner.SchoolSettings.Add(new SchoolSettings
            {
                SchoolId = schoolId,
                SmsCreditBalance = 100,
                SmsOnDuesReminder = true,
                DebtorReminderThresholdDays = 1
            });
        }

        (_enrollmentA, _studentA) = SeedDebtor(owner, EcoleA, "Awa Débitrice", "+221770000001");
        (_enrollmentB, _studentB) = SeedDebtor(owner, EcoleB, "Baba Débiteur", "+221770000002");

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static (Guid EnrollmentId, Guid StudentId) SeedDebtor(
        Persistence.ApplicationDbContext owner, Guid schoolId, string studentName, string guardianPhone)
    {
        var classroomId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var feeCategoryId = Guid.NewGuid();

        owner.FeeCategories.Add(new FeeCategory
        {
            Id = feeCategoryId, SchoolId = schoolId, Name = "Mensualité", IsRecurring = true
        });

        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = schoolId, Name = "CM2", Level = "Primaire", Capacity = 40
        });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = yearId, SchoolId = schoolId, Label = "2026-2027",
            StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
        });

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = schoolId, Matricule = $"ELEV-{schoolId.ToString()[..4]}",
            FullName = studentName, BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar",
            Gender = "F", ClassroomId = classroomId, GuardianPhone = guardianPhone
        });

        // 6 mois de mensualité à 15 000, jamais payés : toutes les échéances passées sont en retard.
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = schoolId, StudentId = studentId, SchoolYearId = yearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 90_000m, AmountPaid = 0m, ReceiptNumber = $"REC-{schoolId.ToString()[..4]}",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = schoolId, EnrollmentId = enrollmentId, FeeCategoryId = feeCategoryId,
            Designation = "Mensualité", IsRecurring = true, UnitAmount = 15_000m, Months = 6,
            LineTotal = 90_000m
        });

        return (enrollmentId, studentId);
    }

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private async Task<int> GenerateBatchesAsync(Guid schoolId)
    {
        await using var context = _db.NewAppContext(schoolId);
        var handler = new GenerateDebtorReminderBatchesCommandHandler(
            context, new FixedTenantProvider(schoolId), TimeProvider.System,
            NullLogger<GenerateDebtorReminderBatchesCommandHandler>.Instance);

        return await handler.Handle(new GenerateDebtorReminderBatchesCommand(), CancellationToken.None);
    }

    [Fact]
    public async Task Generating_Batches_For_One_School_Creates_A_Draft_With_Only_That_Schools_Debtors()
    {
        var createdForA = await GenerateBatchesAsync(EcoleA);

        createdForA.Should().Be(1);

        await using var owner = _db.NewOwnerContext();

        var batches = await owner.DebtorReminderBatches.IgnoreQueryFilters().ToListAsync();
        batches.Should().ContainSingle().Which.SchoolId.Should().Be(EcoleA);

        var items = await owner.DebtorReminderBatchItems.IgnoreQueryFilters().ToListAsync();
        items.Should().ContainSingle().Which.EnrollmentId.Should().Be(_enrollmentA);
        items[0].StudentId.Should().Be(_studentA);
        items[0].SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Generating_Batches_Sequentially_For_Both_Schools_Never_Mixes_Their_Debtors()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var owner = _db.NewOwnerContext();

        var batches = await owner.DebtorReminderBatches.IgnoreQueryFilters().ToListAsync();
        batches.Should().HaveCount(2);
        batches.Select(b => b.SchoolId).Should().BeEquivalentTo([EcoleA, EcoleB]);

        var itemsByBatch = await owner.DebtorReminderBatchItems.IgnoreQueryFilters()
            .ToDictionaryAsync(i => i.DebtorReminderBatchId);

        foreach (var batch in batches)
        {
            var item = itemsByBatch[batch.Id];
            item.SchoolId.Should().Be(batch.SchoolId, "un débiteur ne doit jamais apparaître dans le lot d'une autre école");
        }
    }

    [Fact]
    public async Task Tenant_Scoped_List_Query_Never_Returns_The_Other_Schools_Batch()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var contextA = _db.NewAppContext(EcoleA);
        var listForA = await new GetDebtorReminderBatchesQueryHandler(contextA)
            .Handle(new GetDebtorReminderBatchesQuery(Status: null), CancellationToken.None);

        listForA.Should().ContainSingle();
        listForA[0].Items.Should().ContainSingle().Which.StudentId.Should().Be(_studentA);
    }

    [Fact]
    public async Task Sending_A_Batch_Only_Queues_Sms_For_Its_Own_Schools_Debtors()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var contextA = _db.NewAppContext(EcoleA);
        var batchIdA = await contextA.DebtorReminderBatches.Select(b => b.Id).SingleAsync();

        var handler = new SendDebtorReminderBatchCommandHandler(
            contextA,
            new FixedTenantProvider(EcoleA),
            new FixedCurrentUser(Guid.NewGuid()),
            new SmsDispatcher(contextA, TimeProvider.System, NullLogger<SmsDispatcher>.Instance),
            TimeProvider.System,
            NullLogger<SendDebtorReminderBatchCommandHandler>.Instance);

        var result = await handler.Handle(new SendDebtorReminderBatchCommand(batchIdA), CancellationToken.None);

        result.QueuedCount.Should().Be(1);

        await using var owner = _db.NewOwnerContext();
        var messages = await owner.SmsMessages.IgnoreQueryFilters().ToListAsync();
        messages.Should().ContainSingle().Which.SchoolId.Should().Be(EcoleA);

        var sentBatch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.Id == batchIdA);
        sentBatch.Status.Should().Be(DebtorReminderBatchStatus.Sent);

        // Le lot de l'école B n'a jamais été touché : toujours Draft.
        var otherBatch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.SchoolId == EcoleB);
        otherBatch.Status.Should().Be(DebtorReminderBatchStatus.Draft);
    }

    [Fact]
    public async Task A_School_Cannot_Send_Or_Dismiss_Another_Schools_Batch()
    {
        await GenerateBatchesAsync(EcoleA);
        await GenerateBatchesAsync(EcoleB);

        await using var ownerLookup = _db.NewOwnerContext();
        var batchIdB = await ownerLookup.DebtorReminderBatches.IgnoreQueryFilters()
            .Where(b => b.SchoolId == EcoleB).Select(b => b.Id).SingleAsync();

        // Sous le tenant A, le Global Query Filter rend le lot de B structurellement introuvable —
        // jamais un 403 métier, un 404 pur et simple, comme partout ailleurs dans le module Finance.
        await using var contextA = _db.NewAppContext(EcoleA);

        var sendHandler = new SendDebtorReminderBatchCommandHandler(
            contextA, new FixedTenantProvider(EcoleA), new FixedCurrentUser(Guid.NewGuid()),
            new SmsDispatcher(contextA, TimeProvider.System, NullLogger<SmsDispatcher>.Instance),
            TimeProvider.System, NullLogger<SendDebtorReminderBatchCommandHandler>.Instance);

        var sendAct = () => sendHandler.Handle(new SendDebtorReminderBatchCommand(batchIdB), CancellationToken.None);
        await sendAct.Should().ThrowAsync<KeyNotFoundException>();

        await using var contextA2 = _db.NewAppContext(EcoleA);
        var dismissHandler = new DismissDebtorReminderBatchCommandHandler(contextA2);
        var dismissAct = () => dismissHandler.Handle(new DismissDebtorReminderBatchCommand(batchIdB), CancellationToken.None);
        await dismissAct.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Dismissing_A_Draft_Batch_Never_Sends_Any_Sms()
    {
        await GenerateBatchesAsync(EcoleA);

        await using var contextA = _db.NewAppContext(EcoleA);
        var batchId = await contextA.DebtorReminderBatches.Select(b => b.Id).SingleAsync();

        await new DismissDebtorReminderBatchCommandHandler(contextA)
            .Handle(new DismissDebtorReminderBatchCommand(batchId), CancellationToken.None);

        await using var owner = _db.NewOwnerContext();
        (await owner.SmsMessages.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var batch = await owner.DebtorReminderBatches.IgnoreQueryFilters().SingleAsync(b => b.Id == batchId);
        batch.Status.Should().Be(DebtorReminderBatchStatus.Dismissed);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public SamaEcole.Domain.Enums.Role? Role => SamaEcole.Domain.Enums.Role.Finance;
        public string? IpAddress => null;
    }
}
