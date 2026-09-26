using MediatR;

namespace SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;

public record GetSchoolCardsPdfQuery(Guid ClassroomId, Guid SchoolYearId) : IRequest<byte[]>;

public record SchoolCardDto(
    string StudentFullName,
    string Matricule,
    DateOnly BirthDate,
    string? BirthPlace,
    byte[] QrCodeImage
);

/// <param name="SchoolLogo">
/// Octets du logo déjà récupérés par le handler via <c>ISchoolLogoProvider</c> (filtrage SSRF,
/// taille et délai bornés), une seule fois pour tout le lot — ou <c>null</c> : cartes sans logo.
/// Le document PDF ne fait jamais lui-même d'appel réseau.
/// </param>
public record SchoolCardBatchDto(
    string SchoolName,
    byte[]? SchoolLogo,
    string ClassroomName,
    string SchoolYearName,
    string? PhoneNumber,
    string? Address,
    List<SchoolCardDto> Cards
);
