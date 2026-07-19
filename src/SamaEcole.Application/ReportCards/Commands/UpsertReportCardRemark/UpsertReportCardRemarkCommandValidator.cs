using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.ReportCards.Commands.UpsertReportCardRemark;

public class UpsertReportCardRemarkCommandValidator : AbstractValidator<UpsertReportCardRemarkCommand>
{
    public UpsertReportCardRemarkCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.TermId).NotEmpty();

        RuleFor(c => c.DisciplinaryMention).IsInEnum().When(c => c.DisciplinaryMention.HasValue)
            .WithMessage("Distinction invalide.");

        // 300 caractères : au-delà, le cadre "Observations" du bulletin A5 déborde sur une seconde
        // page (voir ReportCardRemarkConfiguration).
        RuleFor(c => c.Observations).MaximumLength(300).NoHtml();
    }
}
