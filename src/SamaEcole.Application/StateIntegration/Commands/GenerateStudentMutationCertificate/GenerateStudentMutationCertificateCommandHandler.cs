using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Commands.GenerateStudentMutationCertificate;

/// <summary>
/// Délivre un certificat de mutation (Volume 1 §23.5).
///
/// TOUT se passe dans UNE transaction : numéro officiel, code de vérification, ligne en base. Le PDF,
/// lui, est composé APRÈS — un échec de rendu ne doit pas consommer un numéro de la série officielle,
/// et une ligne écrite sans PDF reste rattrapable (le document se réimprime), alors qu'un numéro
/// consommé sans ligne laisse un trou inexplicable dans un registre officiel.
/// </summary>
public class GenerateStudentMutationCertificateCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator,
    IQrCodeService qrCodeService,
    IStudentMutationCertificatePdfGenerator pdfGenerator,
    ISchoolLogoProvider imageProvider,
    StateIntegrationSettings settings,
    TimeProvider timeProvider)
    : IRequestHandler<GenerateStudentMutationCertificateCommand, MutationCertificateResult>
{
    public async Task<MutationCertificateResult> Handle(
        GenerateStudentMutationCertificateCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException("Élève introuvable dans votre établissement.");

        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Année scolaire introuvable dans votre établissement.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstAsync(s => s.Id == schoolId, cancellationToken);

        var settingsRow = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

        // Classe DE L'INSCRIPTION de l'année visée, et non Student.ClassroomId : le certificat atteste
        // de la classe quittée au titre de cet exercice. Un élève déjà réaffecté pour l'année suivante
        // porterait sinon une classe qu'il n'a jamais fréquentée sur l'année certifiée.
        var classroomName = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == student.Id
                        && e.SchoolYearId == request.SchoolYearId
                        && e.Status != EnrollmentStatus.Cancelled)
            .Join(dbContext.Classrooms.AsNoTracking(), e => e.ClassroomId, c => c.Id, (e, c) => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (classroomName is null)
        {
            throw new KeyNotFoundException(
                "Aucune inscription active de cet élève sur l'année scolaire demandée : "
                + "impossible d'attester d'une classe quittée.");
        }

        var wasFinanciallyClear = await IsFinanciallyClearAsync(
            student.Id, request.SchoolYearId, cancellationToken);

        var issuedOn = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Numéro officiel + code de vérification + ligne, dans la MÊME transaction (règle #3).
        var certificate = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var number = await matriculeGenerator.GenerateNextMutationCertificateNumberAsync(schoolId, ct);

            var entity = new StudentMutationCertificate
            {
                SchoolId = schoolId,
                StudentId = student.Id,
                SchoolYearId = request.SchoolYearId,
                CertificateNumber = number,
                VerificationCode = NewVerificationCode(),
                DestinationSchoolName = request.DestinationSchoolName?.Trim(),
                DestinationCity = request.DestinationCity?.Trim(),
                Reason = request.Reason,
                ReasonDetails = request.ReasonDetails?.Trim(),
                ClassroomNameSnapshot = classroomName,
                IssuedOn = issuedOn,
                WasFinanciallyClear = wasFinanciallyClear
            };

            dbContext.StudentMutationCertificates.Add(entity);
            await dbContext.SaveChangesAsync(ct);

            return entity;
        }, cancellationToken);

        var model = new StudentMutationCertificateModel(
            SchoolName: school.Name,
            NationalSchoolCode: school.NationalSchoolCode,
            MinistryAuthorizationNumber: school.MinistryAuthorizationNumber,
            InspectionAcademie: school.InspectionAcademie,
            InspectionEducationFormation: school.InspectionEducationFormation,
            Address: school.Address,
            Phone: school.Phone,
            Email: school.Email,
            City: school.City,
            CertificateNumber: certificate.CertificateNumber,
            IssuedOn: certificate.IssuedOn,
            VerificationUrl: BuildVerificationUrl(certificate.VerificationCode),
            StudentFullName: student.FullName,
            Matricule: student.Matricule,
            IenNumber: student.IenNumber,
            IsIenProvisional: student.IsIenProvisional,
            BirthDate: student.BirthDate,
            BirthPlace: student.BirthPlace,
            Gender: student.Gender,
            ClassroomName: certificate.ClassroomNameSnapshot,
            SchoolYearLabel: schoolYear.Label,
            Reason: certificate.Reason,
            ReasonDetails: certificate.ReasonDetails,
            DestinationSchoolName: certificate.DestinationSchoolName,
            DestinationCity: certificate.DestinationCity,
            WasFinanciallyClear: certificate.WasFinanciallyClear,
            DirectorSignature: await imageProvider.TryFetchAsync(settingsRow?.DirectorSignatureUrl, cancellationToken),
            OfficialStamp: await imageProvider.TryFetchAsync(settingsRow?.OfficialStampUrl, cancellationToken));

        var qrCode = qrCodeService.GenerateQrCode(model.VerificationUrl);
        var content = pdfGenerator.Generate(model, qrCode);

        return new MutationCertificateResult(
            certificate.Id,
            certificate.CertificateNumber,
            $"Certificat-Mutation-{certificate.CertificateNumber}.pdf",
            content,
            wasFinanciallyClear);
    }

    /// <summary>
    /// Vrai si l'élève ne doit plus rien sur l'exercice. INSTANTANÉ figé sur la pièce : le certificat
    /// atteste de la situation au jour de sa délivrance, et la recalculer plus tard changerait le sens
    /// d'un document déjà signé.
    ///
    /// N'EMPÊCHE PAS la délivrance quand c'est faux — voir StudentMutationCertificate.WasFinanciallyClear.
    /// </summary>
    private async Task<bool> IsFinanciallyClearAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var balances = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId
                        && e.SchoolYearId == schoolYearId
                        && e.Status != EnrollmentStatus.Cancelled)
            .Select(e => e.TotalDue - e.AmountPaid)
            .ToListAsync(cancellationToken);

        return balances.All(remaining => remaining <= 0);
    }

    /// <summary>
    /// Code de vérification : 32 caractères hexadécimaux d'aléa CRYPTOGRAPHIQUE. Ni un GUID séquentiel,
    /// ni un dérivé du numéro de certificat — les deux seraient devinables, et un tiers pourrait alors
    /// sonder le point de vérification pour découvrir quels élèves ont quitté l'établissement.
    /// </summary>
    private static string NewVerificationCode() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private string BuildVerificationUrl(string verificationCode) =>
        $"{settings.PublicBaseUrl.TrimEnd('/')}/verifier/mutation/{verificationCode}";
}
