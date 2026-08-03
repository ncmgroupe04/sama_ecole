using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;

public class GetSchoolCardsPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IQrCodeService qrCodeService,
    ISchoolCardPdfGenerator pdfGenerator) : IRequestHandler<GetSchoolCardsPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetSchoolCardsPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à la session.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"School {schoolId} not found");

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId && c.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classroom {request.ClassroomId} not found");

        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId && y.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"SchoolYear {request.SchoolYearId} not found");

        // Même convention que GetStudentsExportPdf/GetClassGrades/Attendance/ReportCards : la classe
        // courante d'un élève est Student.ClassroomId, pas une jointure sur Enrollments — sinon un
        // élève réaffecté de classe après son inscription initiale (UpdateStudent ne touche que
        // Student.ClassroomId, jamais l'Enrollment de l'année) disparaît à tort de ses propres cartes
        // scolaires alors qu'il apparaît bien dans l'effectif affiché par GetClassroomsQuery.
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .ToListAsync(cancellationToken);

        if (students.Count == 0)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("ClassroomId", "Aucun élève inscrit dans cette classe.")]);

        var cards = students.Select(student =>
        {
            // The QR code contains a verification URL (or matricule).
            string qrContent = $"https://app.samaecole.sn/verify?id={student.Matricule}";
            var qrCodeImage = qrCodeService.GenerateQrCode(qrContent);

            return new SchoolCardDto(
                StudentFullName: student.FullName,
                Matricule: student.Matricule,
                BirthDate: student.BirthDate,
                BirthPlace: student.BirthPlace,
                QrCodeImage: qrCodeImage
            );
        }).ToList();

        var batch = new SchoolCardBatchDto(
            SchoolName: school.Name,
            SchoolLogoUrl: school.LogoUrl,
            ClassroomName: classroom.Name,
            SchoolYearName: schoolYear.Label,
            PhoneNumber: school.Phone,
            Address: school.Address,
            Cards: cards
        );

        return pdfGenerator.GeneratePdf(batch);
    }
}
