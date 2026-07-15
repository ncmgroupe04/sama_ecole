using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-F02, AGENTS.md règle #5 — CRITÈRE OBLIGATOIRE du ticket : « deux paiements concurrents sur
/// le même solde ne produisent jamais un état incohérent ».
///
/// Le solde vit sur l'inscription (<c>TotalDue − AmountPaid</c>). Deux caissiers qui encaissent en même
/// temps sur la même inscription ont chacun LU le même jeton xmin ; le premier qui écrit gagne, le second
/// se heurte à un 409 (ConcurrencyConflictException) plutôt que d'écraser le solde — jamais de sur-crédit
/// silencieux. On l'exerce contre un vrai PostgreSQL sous le rôle applicatif (RLS active).
/// </summary>
[Trait("Category", "MultiTenant")]
public class PaymentConcurrencyTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private const decimal TotalDue = 100_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2025-0001",
            FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = TotalDue, AmountPaid = 0m, ReceiptNumber = "REC-2025-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Payment NewPayment(decimal amount, string receiptNumber) => new()
    {
        SchoolId = Ecole,
        EnrollmentId = Inscription,
        Amount = amount,
        Method = PaymentMethod.Cash,
        Status = PaymentStatus.Partial,
        BalanceAfter = TotalDue - amount,
        ReceiptNumber = receiptNumber,
        ReceivedByUserId = Caissier,
        PaidAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Two_Concurrent_Payments_On_The_Same_Balance_The_Second_One_Is_Refused()
    {
        // Deux caissiers ont chacun ouvert la fiche : ils détiennent le même jeton xmin de l'inscription.
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var enrollmentA = await ctxA.Enrollments.FirstAsync(e => e.Id == Inscription);
        var enrollmentB = await ctxB.Enrollments.FirstAsync(e => e.Id == Inscription);

        // A encaisse 30 000 en premier : le xmin de l'inscription bascule en base.
        ctxA.Payments.Add(NewPayment(30_000m, "REC-2025-0002"));
        enrollmentA.AmountPaid += 30_000m;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        // B encaisse avec le jeton qu'il détenait — désormais périmé. Son UPDATE de l'inscription
        // « WHERE xmin = <ancien> » ne touche aucune ligne : SaveChanges refuse, et son INSERT de paiement
        // est rembobiné avec (SaveChanges est atomique) plutôt que de créer un sur-crédit.
        ctxB.Payments.Add(NewPayment(50_000m, "REC-2025-0003"));
        enrollmentB.AmountPaid += 50_000m;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux encaissements concurrents sur le même solde ne doivent jamais s'écraser (règle #5)");

        // État final cohérent : un SEUL paiement, et le cumul encaissé est celui de A, intact.
        await using var check = _db.NewAppContext(Ecole);
        (await check.Payments.CountAsync(p => p.EnrollmentId == Inscription)).Should().Be(1, "le paiement de B a été rembobiné");
        (await check.Enrollments.Where(e => e.Id == Inscription).Select(e => e.AmountPaid).FirstAsync())
            .Should().Be(30_000m, "seul l'encaissement de A a été appliqué au solde");
    }

    [Fact]
    public async Task A_Sequential_Payment_With_A_Fresh_Token_Succeeds()
    {
        // Contre-épreuve : sans conflit, l'encaissement passe. Sinon le test précédent pourrait être vert
        // pour une mauvaise raison (une écriture qui échouerait TOUJOURS).
        await using var ctx = _db.NewAppContext(Ecole);
        var enrollment = await ctx.Enrollments.FirstAsync(e => e.Id == Inscription);

        ctx.Payments.Add(NewPayment(40_000m, "REC-2025-0004"));
        enrollment.AmountPaid += 40_000m;

        var act = async () => await ctx.SaveChangesAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
