using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Notifications.Commands.SendDuesReminderSms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Notifications;

/// <summary>
/// La relance manuelle par SMS ne vise que les élèves qui ont une échéance EN RETARD, et annonce ce
/// montant en retard. Avant, tout élève avec « total payé &lt; total dû » recevait le reliquat de
/// l'année entière — y compris une famille à jour de ses mensualités ou un élève dont l'année n'a
/// pas commencé.
/// </summary>
public class DuesReminderSmsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2027, 1, 15);
    private static readonly DateTimeOffset Now = new(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

    private const decimal Mensualite = 15_000m;
    private const int TuitionMonths = 9;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École Test" });
        var classroomId = Guid.NewGuid();
        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40
        });
        var categoryId = Guid.NewGuid();
        owner.FeeCategories.Add(new FeeCategory
        {
            Id = categoryId, SchoolId = Ecole, Name = "Mensualité", IsRecurring = true
        });

        var currentYear = Guid.NewGuid();
        var futureYear = Guid.NewGuid();
        owner.SchoolYears.AddRange(
            new SchoolYear
            {
                Id = currentYear, SchoolId = Ecole, Label = "2026-2027",
                StartDate = SchoolYearStart, EndDate = SchoolYearStart.AddYears(1), IsActive = true
            },
            new SchoolYear
            {
                Id = futureYear, SchoolId = Ecole, Label = "2027-2028",
                StartDate = new DateOnly(2027, 9, 1), EndDate = new DateOnly(2028, 6, 30), IsActive = false
            });

        // En retard : rien versé, les mensualités des 01/10, 01/11, 01/12 et 01/01 sont échues (4 × 15 000).
        Seed(owner, classroomId, categoryId, currentYear, "Awa En Retard", "+221770000001", amountPaid: 0m);
        // À jour : 4 mensualités versées ; reste 75 000 mais uniquement sur des échéances à venir.
        Seed(owner, classroomId, categoryId, currentYear, "Moussa A Jour", "+221770000002", amountPaid: Mensualite * 4);
        // Année pas commencée : rien n'est échu, malgré un solde positif.
        Seed(owner, classroomId, categoryId, futureYear, "Fatou Pas Commencee", "+221770000003", amountPaid: 0m);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static void Seed(
        Persistence.ApplicationDbContext owner, Guid classroomId, Guid categoryId, Guid yearId,
        string name, string phone, decimal amountPaid)
    {
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var total = Mensualite * TuitionMonths;

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = Ecole, Matricule = $"M-{studentId.ToString()[..6]}", FullName = name,
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F",
            ClassroomId = classroomId, GuardianPhone = phone
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = Ecole, StudentId = studentId, SchoolYearId = yearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = total, AmountPaid = amountPaid, ReceiptNumber = $"REC-{enrollmentId.ToString()[..6]}",
            EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = enrollmentId, FeeCategoryId = categoryId,
            Designation = "Mensualité", IsRecurring = true, UnitAmount = Mensualite, Months = TuitionMonths,
            LineTotal = total
        });
    }

    [Fact]
    public async Task Only_Students_With_An_Overdue_Installment_Are_Reminded_For_The_Overdue_Amount()
    {
        var dispatcher = new CapturingDispatcher();
        await using var db = _db.NewAppContext(Ecole);
        var handler = new SendDuesReminderSmsCommandHandler(
            db, new FixedTenantProvider(Ecole), dispatcher, new FixedTime(Now),
            NullLogger<SendDuesReminderSmsCommandHandler>.Instance);

        var result = await handler.Handle(new SendDuesReminderSmsCommand(), CancellationToken.None);

        result.QueuedCount.Should().Be(1);
        var sent = dispatcher.Requests.Should().ContainSingle().Subject;
        sent.Recipient.Should().Be("+221770000001");
        sent.Body.Should().Contain("Awa En Retard").And.Contain("retard");
        sent.Body.Should().Contain((Mensualite * 4).ToString("N0"), "le montant annoncé est celui en retard, pas le reliquat de l'année");
        sent.Body.Should().NotContain((Mensualite * TuitionMonths).ToString("N0"));
    }

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingDispatcher : ISmsDispatcher
    {
        public List<SmsDispatchRequest> Requests { get; } = [];

        public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(SmsDispatchOutcome.Queued(Guid.NewGuid()));
        }
    }
}
