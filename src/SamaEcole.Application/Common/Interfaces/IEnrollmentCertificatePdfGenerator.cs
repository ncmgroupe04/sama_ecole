using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;

namespace SamaEcole.Application.Common.Interfaces;

public interface IEnrollmentCertificatePdfGenerator
{
    byte[] Generate(EnrollmentCertificateDto certificate, byte[]? logo);
}
