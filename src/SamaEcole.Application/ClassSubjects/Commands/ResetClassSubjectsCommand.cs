using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Common.Interfaces;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// POST /api/v1/class-subjects/reset — « Réinitialiser aux coefficients officiels du Sénégal » (Évolution N°6) :
/// le programme et les coefficients de la classe redeviennent ceux du modèle national de sa série, pour l'année
/// active (voir <see cref="ClassSubjectTemplateInjector"/>). Les matières ajoutées par le Directeur restent.
/// 422 pour une classe sans série ou dont la série n'a pas de modèle.
/// </summary>
public record ResetClassSubjectsCommand(Guid ClassroomId) : IRequest<ClassTemplateReport>, IAuditableRequest;

public class ResetClassSubjectsCommandValidator : AbstractValidator<ResetClassSubjectsCommand>
{
    public ResetClassSubjectsCommandValidator() => RuleFor(x => x.ClassroomId).NotEmpty();
}

public class ResetClassSubjectsCommandHandler(
    IApplicationDbContext dbContext, ClassSubjectTemplateInjector injector, ISeriesTemplateProvider templates)
    : IRequestHandler<ResetClassSubjectsCommand, ClassTemplateReport>
{
    public async Task<ClassTemplateReport> Handle(ResetClassSubjectsCommand request, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable.");

        if (classroom.Series is null || templates.For(classroom.Series).Count == 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId),
                    classroom.Series is null
                        ? "Cette classe n'a pas de série : il n'existe pas de coefficients officiels à rétablir."
                        : "Aucun modèle national n'existe pour la série de cette classe : réglez ses coefficients matière par matière.")
            ]);
        }

        var report = await injector.ApplyAsync(classroom, enforceOfficial: true, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return report;
    }
}
