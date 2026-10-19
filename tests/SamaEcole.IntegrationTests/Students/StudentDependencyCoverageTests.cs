using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Students.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// « Tout est supprimable » tient par construction : une table qui référence un élève et qu'aucun développeur n'a
/// classée (lien rompable ou volontairement ignoré) fait échouer la CI — au lieu de laisser une impasse à l'utilisateur.
/// Le modèle EF se lit sans connexion : aucun conteneur nécessaire.
/// </summary>
public class StudentDependencyCoverageTests
{
    [Fact]
    public void Every_Table_That_References_A_Student_Is_Classified()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=x;Password=x")
            .Options;
        using var db = new ApplicationDbContext(options, new NoTenant(), NullLogger<ApplicationDbContext>.Instance);

        var referencing = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Student))
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .ToList();

        var classified = StudentDependencyCatalog.All.Select(d => d.EntityType)
            .Concat(StudentDependencyCatalog.IgnoredDependents)
            .ToHashSet();

        referencing.Should().NotBeEmpty();
        referencing.Where(t => !classified.Contains(t)).Select(t => t.Name)
            .Should().BeEmpty("chaque table liée à un élève doit être classée dans StudentDependencyCatalog (rompable) ou ignorée explicitement");
    }

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }
}
