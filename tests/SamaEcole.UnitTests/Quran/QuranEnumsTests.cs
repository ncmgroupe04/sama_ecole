using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

/// <summary>
/// Garde le premier membre de chaque enum aligné sur la valeur par défaut annoncée dans la spec
/// (docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md §3.1/3.2/3.3) : c'est ce membre
/// que le CLR pose par défaut ET celui que la migration doit écrire en dur pour les lignes
/// existantes — un décalage entre les deux a déjà cassé la lecture des ParentSummons en
/// production (ACTIVE_CONTEXT.md, incident du 02/09/2026).
/// </summary>
public class QuranEnumsTests
{
    [Fact]
    public void SectionType_Default_Should_Be_French()
    {
        default(SectionType).Should().Be(SectionType.French);
    }

    [Fact]
    public void SchoolType_Default_Should_Be_Standard()
    {
        default(SchoolType).Should().Be(SchoolType.Standard);
    }

    [Fact]
    public void QuranMemorizationStatus_Default_Should_Be_InProcess()
    {
        default(QuranMemorizationStatus).Should().Be(QuranMemorizationStatus.InProcess);
    }
}
