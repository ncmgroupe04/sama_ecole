using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// Noms bilingues Élève &amp; Tuteur — configuration EF des deux miroirs arabes (spec §8, « unitaires EF »).
/// Test de MODÈLE, sans base : il lit la métadonnée EF, qui est ce que la migration reproduit en colonnes.
/// Facultatifs (nullable, jamais de valeur par défaut inventée) et 200 caractères, comme
/// <see cref="Student.FullName"/> et la borne du validateur de <c>GuardianName</c>.
/// </summary>
public class StudentArabicNamesModelTests
{
    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType StudentModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options;
        using var context = new ApplicationDbContext(options, new NoTenant(), NullLogger<ApplicationDbContext>.Instance);
        return context.Model.FindEntityType(typeof(Student))!;
    }

    [Theory]
    [InlineData(nameof(Student.FullNameAr))]
    [InlineData(nameof(Student.GuardianNameAr))]
    public void An_Arabic_Name_Is_Optional_And_Capped_At_200_Characters(string property)
    {
        var column = StudentModel().FindProperty(property)!;

        column.IsNullable.Should().BeTrue("un nom arabe est facultatif : un élève sans nom arabe reste enregistrable");
        column.GetMaxLength().Should().Be(200);
    }

    [Fact]
    public void A_New_Student_Has_No_Arabic_Name_By_Default()
    {
        var student = new Student
        {
            FullName = "Awa Fall", BirthPlace = "Dakar", Gender = "F", Matricule = "ELEV-2026-0001"
        };

        student.FullNameAr.Should().BeNull("jamais une valeur inventée ni dérivée du nom français");
        student.GuardianNameAr.Should().BeNull();
    }
}
