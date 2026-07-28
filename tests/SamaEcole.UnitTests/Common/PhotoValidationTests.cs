using FluentAssertions;
using SamaEcole.Application.Common.Validation;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>Feature B — la règle de validation de la photo téléversée, partagée par Student et Teacher.</summary>
public class PhotoValidationTests
{
    [Fact]
    public void Absent_Photo_Should_Pass()
    {
        // La photo est optionnelle : absente/vide n'est pas une erreur, contrairement à un base64 invalide.
        PhotoValidation.BeValidPhotoData(null).Should().BeTrue();
        PhotoValidation.BeValidPhotoData("").Should().BeTrue();
    }

    [Fact]
    public void Valid_Small_Base64_Should_Pass()
    {
        var base64 = Convert.ToBase64String("une petite image de test"u8.ToArray());

        PhotoValidation.BeValidPhotoData(base64).Should().BeTrue();
    }

    [Fact]
    public void Malformed_Base64_Should_Fail()
    {
        PhotoValidation.BeValidPhotoData("ceci n'est pas du base64 valide !!!").Should().BeFalse();
    }

    [Fact]
    public void Oversized_Photo_Should_Fail()
    {
        // Au-delà de MaxPhotoBytes (500 Ko) — largement au-dessus des ~30 Ko attendus après compression
        // cliente (Canvas 300x300 JPEG q80) : un client modifié ne peut pas contourner la borne.
        var oversized = new byte[PhotoValidation.MaxPhotoBytes + 1];
        var base64 = Convert.ToBase64String(oversized);

        PhotoValidation.BeValidPhotoData(base64).Should().BeFalse();
    }

    [Fact]
    public void Photo_At_Exactly_The_Limit_Should_Pass()
    {
        var atLimit = new byte[PhotoValidation.MaxPhotoBytes];
        var base64 = Convert.ToBase64String(atLimit);

        PhotoValidation.BeValidPhotoData(base64).Should().BeTrue();
    }
}
