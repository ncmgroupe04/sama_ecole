using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Buildings.Deletion;
using SamaEcole.Application.Classrooms.Deletion;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Deletion;
using SamaEcole.Application.Inventory.Deletion;
using SamaEcole.Application.Rooms.Deletion;
using SamaEcole.Application.SchoolYears.Deletion;
using SamaEcole.Application.Subjects.Deletion;
using SamaEcole.Application.Teachers.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Deletion;

/// <summary>
/// « Tout est supprimable » tient par construction : une table qui référence une classe, une matière ou un enseignant
/// et qu'aucun développeur n'a classée dans sa politique de suppression fait échouer la CI — au lieu de laisser une
/// impasse à l'utilisateur. Le modèle EF se lit sans connexion : aucun conteneur nécessaire.
/// </summary>
public class DeletionDependencyCoverageTests
{
    public static IEnumerable<object[]> Cases()
    {
        IReadOnlySet<Type> none = new HashSet<Type>();

        yield return [typeof(Classroom), ClassroomDeletionPolicy.Dependencies, none];
        yield return [typeof(Subject), SubjectDeletionPolicy.Dependencies, none];
        // Les matières enseignées (TeacherSubject) sont intrinsèques à la fiche : classées, mais archivées avec elle.
        yield return [typeof(Teacher), TeacherDeletionPolicy.Dependencies.Concat(TeacherDeletionPolicy.Intrinsic).ToList(), none];
        yield return [typeof(Building), BuildingDeletionPolicy.Dependencies, none];
        yield return [typeof(Room), RoomDeletionPolicy.Dependencies, RoomDeletionPolicy.IgnoredDependents];
        // Inscriptions et paiements facturés : couverts par la règle financière, pas par un lien rompable.
        yield return [typeof(FeeCategory), FeeCategoryDeletionPolicy.Dependencies, FeeCategoryDeletionPolicy.CoveredByFinancialRule];
        yield return [typeof(InventoryCategory), InventoryCategoryDeletionPolicy.Dependencies, none];
        // Le journal de stock est append-only : trace immuable ignorée ; les prêts clos sont intrinsèques.
        yield return [typeof(InventoryItem), InventoryItemDeletionPolicy.Dependencies.Concat(InventoryItemDeletionPolicy.Intrinsic).ToList(), InventoryItemDeletionPolicy.IgnoredDependents];
        // Année scolaire (mode réel) : les paiements sont couverts par la règle financière (via les inscriptions) ; la configuration
        // de l'année (trimestres, affectations, fermetures) est intrinsèque.
        yield return [typeof(SchoolYear), SchoolYearDeletionPolicy.Dependencies.Concat(SchoolYearDeletionPolicy.Intrinsic).ToList(), none];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_Table_That_References_The_Element_Is_Classified(
        Type principal, IReadOnlyList<DeletionDependency> dependencies, IReadOnlySet<Type> coveredElsewhere)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=x;Password=x")
            .Options;
        using var db = new ApplicationDbContext(options, new NoTenant(), NullLogger<ApplicationDbContext>.Instance);

        var referencing = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == principal)
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .ToList();

        var classified = dependencies.Select(d => d.EntityType).Concat(coveredElsewhere).ToHashSet();

        referencing.Should().NotBeEmpty();
        referencing.Where(t => !classified.Contains(t)).Select(t => t.Name)
            .Should().BeEmpty($"chaque table liée à {principal.Name} doit être classée dans sa politique de suppression");
    }

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
