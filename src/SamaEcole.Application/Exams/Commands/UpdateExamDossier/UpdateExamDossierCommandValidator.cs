using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.UpdateExamDossier;

public class UpdateExamDossierCommandValidator : AbstractValidator<UpdateExamDossierCommand>
{
    public UpdateExamDossierCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExamCenterName).MaximumLength(150).NoHtml();
        RuleFor(x => x.BirthCertificateNumber).MaximumLength(50).NoHtml();
        RuleFor(x => x.CivilStatusNotes).MaximumLength(500).NoHtml();
    }
}
