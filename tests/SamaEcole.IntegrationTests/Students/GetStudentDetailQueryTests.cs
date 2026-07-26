using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>Rôle du seul champ que GetStudentDetailQueryHandler lit sur ICurrentUserService.</summary>
file sealed class FakeCurrentUserService(Role? role) : ICurrentUserService
{
    public Guid? UserId => null;
    public Role? Role => role;
    public string? IpAddress => null;
}

/// <summary>
/// Ticket JGK-D02 — la fiche élève complète (<c>GetStudentDetailQueryHandler</c>) exerce le vrai
/// Handler contre un PostgreSQL réel sous le rôle applicatif (RLS active), exactement comme
/// GradeSummaryTests et EnrollmentTests : la garantie ne vaut que si elle tient à ce niveau, pas
/// seulement contre un DbContext en mémoire qui n'a ni Global Query Filter réel ni RLS.
///
/// Deux exigences couvertes ici (AGENTS.md — tout ce qui touche Notes/Finance/multi-tenant exige
/// un test) :
///   1. Isolation : un élève d'une autre école est introuvable (404), jamais servi.
///   2. Gracieux : un élève valide sans historique ne casse rien — listes vides, totaux à zéro.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetStudentDetailQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static readonly Guid AnneeA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid TrimestreA = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid MatiereA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    // École A : un élève COMPLET (inscription + note + paiement) pour la contre-épreuve, et un
    // élève SANS AUCUN historique pour le cas « fiche vide ».
    private static readonly Guid EleveComplet = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid InscriptionComplete = Guid.Parse("11111111-0000-0000-0000-0000000000a2");
    private static readonly Guid EleveSansHistorique = Guid.Parse("11111111-0000-0000-0000-0000000000a3");

    // École B : le seul élève dont l'École A ne doit JAMAIS voir la fiche.
    private static readonly Guid EleveEcoleB = Guid.Parse("22222222-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 45 });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = TrimestreA, SchoolId = EcoleA, SchoolYearId = AnneeA, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 1, 15)
        });
        owner.Subjects.Add(new Subject
        {
            Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4
        });

        owner.Students.AddRange(
            new Student
            {
                Id = EleveComplet, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
            },
            new Student
            {
                Id = EleveSansHistorique, SchoolId = EcoleA, Matricule = "ELEV-2026-0002", FullName = "Cheikh Sy",
                BirthDate = new DateOnly(2015, 7, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA
            },
            new Student
            {
                Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop",
                BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB
            });

        owner.Enrollments.Add(new Enrollment
        {
            Id = InscriptionComplete, SchoolId = EcoleA, StudentId = EleveComplet, SchoolYearId = AnneeA,
            ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 30_000m, ReceiptNumber = "REC-2026-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        owner.Grades.Add(new Grade
        {
            SchoolId = EcoleA, StudentId = EleveComplet, SubjectId = MatiereA, TermId = TrimestreA,
            EvaluationType = EvaluationType.Devoir1, Value = 14
        });

        owner.Payments.Add(new Payment
        {
            SchoolId = EcoleA, EnrollmentId = InscriptionComplete, Amount = 30_000m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Partial, BalanceAfter = 70_000m,
            ReceiptNumber = "REC-2026-0002", ReceivedByUserId = Guid.NewGuid(), PaidAt = DateTimeOffset.UtcNow
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------
    // 1) Isolation multi-tenant
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_Throws_NotFound_When_The_Student_Belongs_To_Another_School()
    {
        // Un utilisateur de l'École A (contexte applicatif borné à EcoleA — Global Query Filter +
        // RLS, exactement comme au runtime) tente d'ouvrir la fiche d'un élève de l'École B.
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Directeur));

        var act = async () => await handler.Handle(new GetStudentDetailQuery(EleveEcoleB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "le Global Query Filter EF Core + la RLS PostgreSQL doivent rendre un élève d'une autre " +
            "école structurellement invisible (AGENTS.md règle #2) — jamais une fiche servie, jamais une fuite");
    }

    [Fact]
    public async Task Handle_Returns_The_Student_When_Called_From_Its_Own_School()
    {
        // Contre-épreuve : sans elle, le test ci-dessus pourrait être vert pour une mauvaise raison
        // (un Handler cassé qui échoue pour TOUT le monde, pas seulement pour le mauvais tenant).
        await using var db = _db.NewAppContext(EcoleB);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Directeur));

        var detail = await handler.Handle(new GetStudentDetailQuery(EleveEcoleB), CancellationToken.None);

        detail.Identity.FullName.Should().Be("Modou Diop");
        detail.Identity.Matricule.Should().Be("ELEV-2026-0001");
    }

    // ------------------------------------------------------------------
    // 2) Fiche vide — gracieuse, pas de crash, pas d'objet fantôme
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_Empty_Sections_And_Zeroed_Totals_For_A_Student_With_No_History()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Directeur));

        var detail = await handler.Handle(new GetStudentDetailQuery(EleveSansHistorique), CancellationToken.None);

        // L'identité, elle, est toujours renseignée : seul l'élève INTROUVABLE est un 404.
        detail.Identity.FullName.Should().Be("Cheikh Sy");
        detail.Identity.Matricule.Should().Be("ELEV-2026-0002");
        detail.Identity.ClassroomName.Should().Be("CM2");

        detail.AcademicHistory.Should().BeEmpty("aucune inscription n'a été saisie pour cet élève");
        detail.Grades.Should().BeEmpty("aucune note n'a été saisie pour cet élève");

        detail.Payments.Should().NotBeNull("le Directeur voit toujours le récapitulatif financier");
        detail.Payments!.Entries.Should().BeEmpty("aucun versement n'a été encaissé pour cet élève");
        detail.Payments.TotalDue.Should().Be(0m);
        detail.Payments.TotalPaid.Should().Be(0m);
        detail.Payments.RemainingBalance.Should().Be(0m);

        // Aucun SchoolSettings n'a été semé pour l'École A : le barème par défaut doit s'appliquer,
        // pas une exception ni un zéro silencieux.
        detail.GradingScale.Should().Be(SchoolSettingsDefaults.GradingScale);
    }

    [Fact]
    public async Task Handle_Populates_Every_Section_For_A_Student_With_A_Full_History()
    {
        // Contre-épreuve du cas vide : sans elle, des sections « toujours vides » (un bug qui
        // ignorerait les jointures) rendraient le test précédent vert pour une mauvaise raison.
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Directeur));

        var detail = await handler.Handle(new GetStudentDetailQuery(EleveComplet), CancellationToken.None);

        detail.AcademicHistory.Should().ContainSingle();
        detail.AcademicHistory[0].SchoolYearLabel.Should().Be("2026-2027");
        detail.AcademicHistory[0].Status.Should().Be(nameof(EnrollmentStatus.Confirmed));

        detail.Grades.Should().ContainSingle();
        detail.Grades[0].Subjects.Should().ContainSingle(s => s.SubjectName == "Mathématiques" && s.Devoir1 == 14);

        detail.Payments.Should().NotBeNull();
        detail.Payments!.Entries.Should().ContainSingle();
        detail.Payments.TotalDue.Should().Be(100_000m);
        detail.Payments.TotalPaid.Should().Be(30_000m);
        detail.Payments.RemainingBalance.Should().Be(70_000m);
    }

    // ------------------------------------------------------------------
    // 3) Confidentialité : jamais de paiements pour l'Enseignant
    // ------------------------------------------------------------------

    [Fact]
    public async Task Handle_Never_Returns_Payments_For_An_Enseignant()
    {
        // docs/Volume_7_Security.md « Finance » et Volume_1_Cahier_des_Charges.md (Enseignant : « Sans
        // accès » à la Finance) : Payments doit être ABSENT de la réponse, pas seulement masqué côté
        // UI — un accès direct à l'URL ne doit rien exposer.
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Enseignant));

        var detail = await handler.Handle(new GetStudentDetailQuery(EleveComplet), CancellationToken.None);

        detail.Payments.Should().BeNull("un Enseignant n'a jamais accès aux données financières d'un élève");

        // Contre-épreuve : le reste de la fiche reste servi normalement, seuls les paiements sont coupés.
        detail.Identity.FullName.Should().Be("Awa Fall");
        detail.AcademicHistory.Should().ContainSingle();
        detail.Grades.Should().ContainSingle();
    }
}
