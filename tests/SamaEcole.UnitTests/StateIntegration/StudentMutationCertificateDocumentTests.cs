using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.StateIntegration;

/// <summary>
/// Certificat de mutation (Volume 1 §23.5, ticket JGK-M06) : A4 portrait, UNE page. Le bloc identité a
/// été réagencé en deux colonnes (état civil à gauche ; matricule + classe quittée alignés à droite) —
/// ces tests garantissent qu'aucun cas de contenu ne pousse la pièce sur une seconde page.
/// </summary>
public class StudentMutationCertificateDocumentTests
{
    static StudentMutationCertificateDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static StudentMutationCertificateModel BuildModel(
        string studentName = "Awa Fall Test",
        string classroomName = "Terminale",
        string? ienNumber = null,
        StudentMutationReason reason = StudentMutationReason.Demenagement,
        string? reasonDetails = null,
        string? destinationSchool = null,
        string? destinationCity = null,
        bool wasFinanciallyClear = true) =>
        new(
            SchoolName: "Complexe Scolaire Touba Darou Karim",
            NationalSchoolCode: "SN-DK-0042",
            MinistryAuthorizationNumber: "AUT-2019-118",
            InspectionAcademie: "Dakar",
            InspectionEducationFormation: "Dakar-Médina",
            Address: "Rue 10 x Avenue Bourguiba, Dakar",
            Phone: "+221 33 800 00 00",
            Email: "contact@touba-darou-karim.sn",
            City: "Dakar",
            CertificateNumber: "MUT-2025-0002",
            IssuedOn: new DateOnly(2026, 9, 1),
            VerificationUrl: "https://app.example.sn/verifier-mutation/0123456789abcdef0123456789abcdef",
            StudentFullName: studentName,
            Matricule: "ELEV-2025-0001",
            IenNumber: ienNumber,
            IsIenProvisional: ienNumber is { Length: > 0 },
            BirthDate: new DateOnly(2015, 3, 12),
            BirthPlace: "Dakar",
            Gender: "F",
            ClassroomName: classroomName,
            SchoolYearLabel: "2025-2026",
            Reason: reason,
            ReasonDetails: reasonDetails,
            DestinationSchoolName: destinationSchool,
            DestinationCity: destinationCity,
            WasFinanciallyClear: wasFinanciallyClear);

    [Fact]
    public void A_Standard_Certificate_Fits_On_A_Single_A4_Page()
    {
        var pages = new StudentMutationCertificateDocument(BuildModel(), qrCode: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le certificat de mutation tient sur une seule page A4 (Volume 1 §23.5)");
    }

    [Fact]
    public void A_Certificate_With_Long_Values_And_Every_Optional_Line_Still_Fits_On_One_Page()
    {
        var model = BuildModel(
            studentName: "Marie-Joséphine Aïssatou Ndèye Coumba Diop Sagna Faye",
            classroomName: "Terminale L1-a (série littéraire, groupe a)",
            ienNumber: "PROV-2026-000917",
            reason: StudentMutationReason.RaisonFamiliale,
            reasonDetails: "Rapprochement familial à la suite d'une réaffectation professionnelle du tuteur légal dans une autre région.",
            destinationSchool: "Lycée d'excellence de Ziguinchor — annexe de Bignona",
            destinationCity: "Ziguinchor",
            wasFinanciallyClear: false);

        var pages = new StudentMutationCertificateDocument(model, qrCode: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "même au contenu le plus chargé, le certificat ne déborde jamais sur une 2e page");
    }

    [Fact]
    public void The_Certificate_Renders_To_A_Non_Empty_Pdf()
    {
        var bytes = new StudentMutationCertificateDocument(BuildModel(), qrCode: null).GeneratePdf();

        bytes.Should().NotBeNullOrEmpty();
    }
}
