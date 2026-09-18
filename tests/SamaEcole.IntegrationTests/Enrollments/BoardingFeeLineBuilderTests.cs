using FluentAssertions;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Enrollments;

/// <summary>
/// Module Internat (Task 3) — BoardingFeeLineBuilder factorise EXACTEMENT le même calcul que
/// CreateEnrollmentCommandHandler.BuildFeeLinesAsync (montants copiés depuis ClassFee, mensualité ×
/// tuitionMonths), restreint aux catégories IsBoardingFee et EXCLUANT celles déjà présentes sur
/// l'inscription — c'est ce filtre qui garantit qu'un second changement de chambre (Task 7,
/// ChangeBoardingAssignmentCommandHandler) ne double-facture jamais la pension (spec §5.2).
///
/// Exercé contre un PostgreSQL réel (comme EnrollmentTests) plutôt qu'un fake, car le helper prend
/// IApplicationDbContext en paramètre et sa requête (jointure ClassFees/FeeCategories) doit être
/// prouvée sous le Global Query Filter + RLS réels, pas simulée en mémoire.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingFeeLineBuilderTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    /// <summary>Classe SANS aucun ClassFee paramétré pour la pension (aucune classe internat configurée).</summary>
    private static readonly Guid ClasseSansInternat = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");

    private static readonly Guid PensionCategoryId = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid OtherCategoryId = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private const decimal PensionAmount = 20_000m;
    private const decimal OtherAmount = 15_000m;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseSansInternat, SchoolId = EcoleA, Name = "CM1", Level = "Primaire", Capacity = 35 });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = PensionCategoryId, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true },
            new FeeCategory { Id = OtherCategoryId, SchoolId = EcoleA, Name = "Mensualité", IsRecurring = true, IsBoardingFee = false });

        owner.ClassFees.AddRange(
            new ClassFee { SchoolId = EcoleA, FeeCategoryId = PensionCategoryId, ClassroomId = ClasseA, Amount = PensionAmount },
            new ClassFee { SchoolId = EcoleA, FeeCategoryId = OtherCategoryId, ClassroomId = ClasseA, Amount = OtherAmount });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Includes_Only_Categories_Flagged_IsBoardingFee()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, EcoleA, ClasseA, tuitionMonths: 9, existingFeeCategoryIds: new HashSet<Guid>(), CancellationToken.None);

        lines.Should().ContainSingle();
        lines[0].FeeCategoryId.Should().Be(PensionCategoryId);
        lines[0].Designation.Should().Be("Pension");
        lines[0].UnitAmount.Should().Be(PensionAmount);
        lines[0].Months.Should().Be(9);
        lines[0].LineTotal.Should().Be(180_000m);
    }

    [Fact]
    public async Task Excludes_Categories_Already_Present_On_The_Enrollment()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, EcoleA, ClasseA, tuitionMonths: 9,
            existingFeeCategoryIds: new HashSet<Guid> { PensionCategoryId }, CancellationToken.None);

        lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_Empty_When_No_ClassFee_Exists_For_The_Boarding_Category()
    {
        // ClasseSansInternat n'a AUCUN ClassFee paramétré (ni Pension, ni Mensualité) : aucune classe
        // internat n'y a été configurée par l'école.
        await using var db = _db.NewAppContext(EcoleA);

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, EcoleA, ClasseSansInternat, tuitionMonths: 9, existingFeeCategoryIds: new HashSet<Guid>(), CancellationToken.None);

        lines.Should().BeEmpty();
    }
}
