using FluentAssertions;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Documents;

/// <summary>
/// Module Coran/Franco-Arabe : la police arabe (Noto Naskh Arabic, embarquée en EmbeddedResource
/// dans SamaEcole.Infrastructure) doit s'enregistrer sans erreur, et son enregistrement doit être
/// idempotent — <see cref="PdfFonts.EnsureRegistered"/> est appelé une seule fois en production
/// (DependencyInjection.AddInfrastructure), mais rien n'empêche un test ou un futur second point
/// d'entrée de le rappeler.
/// </summary>
public class PdfFontsTests
{
    [Fact]
    public void EnsureRegistered_Does_Not_Throw()
    {
        var act = PdfFonts.EnsureRegistered;

        act.Should().NotThrow("la police arabe est embarquée dans l'assembly — son absence serait un défaut d'empaquetage, pas une erreur d'exécution normale");
    }

    [Fact]
    public void EnsureRegistered_Is_Idempotent()
    {
        PdfFonts.EnsureRegistered();
        var act = PdfFonts.EnsureRegistered;

        act.Should().NotThrow("un second appel ne doit pas réenregistrer la police ni lever d'exception");
    }
}
