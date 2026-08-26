using SamaEcole.Application.Exams;

namespace SamaEcole.Application.Common.Interfaces;

public interface IExamConvocationPdfGenerator
{
    byte[] Generate(ExamConvocationModel model, byte[]? logo, byte[] qrCodeImage);
}
