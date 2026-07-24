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

public record SchoolCardBatchDto(
    string SchoolName,
    string? SchoolLogoUrl,
    string ClassroomName,
    string SchoolYearName,
    string? PhoneNumber,
    string? Address,
    List<SchoolCardDto> Cards
);
