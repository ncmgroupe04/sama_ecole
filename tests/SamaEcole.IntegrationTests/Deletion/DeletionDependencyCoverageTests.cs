using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Classrooms.Deletion;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
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
        yield return [typeof(Classroom), ClassroomDeletionPolicy.Dependencies];
        yield return [typeof(Subject), SubjectDeletionPolicy.Dependencies];
        // Les matières enseignées (TeacherSubject) sont intrinsèques à la fiche : classées, mais archivées avec elle.
        yield return [typeof(Teacher), TeacherDeletionPolicy.Dependencies.Concat(TeacherDeletionPolicy.Intrinsic).ToList()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_Table_That_References_The_Element_Is_Classified(Type principal, IReadOnlyList<DeletionDependency> dependencies)
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

        var classified = dependencies.Select(d => d.EntityType).ToHashSet();

        referencing.Should().NotBeEmpty();
        referencing.Where(t => !classified.Contains(t)).Select(t => t.Name)
            .Should().BeEmpty($"chaque table liée à {principal.Name} doit être classée dans sa politique de suppression");
    }

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
