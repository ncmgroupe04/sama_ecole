using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Exams.Commands.CreateExamDossier;

public class CreateExamDossierCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateExamDossierCommand, ExamDossierResult>
{
    public async Task<ExamDossierResult> Handle(CreateExamDossierCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter restreint déjà chaque requête au tenant courant : une ligne d'une
        // autre école y est structurellement introuvable (même principe que CreateStudentCommandHandler).
        var session = await dbContext.ExamSessions
            .FirstOrDefaultAsync(s => s.Id == request.ExamSessionId, cancellationToken)
            ?? throw Invalid(nameof(request.ExamSessionId), "La session d'examen indiquée n'existe pas dans votre établissement.");

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw Invalid(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.");
        }

        var classroom = await dbContext.Classrooms
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw Invalid(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.");

        var expectedCycle = ExpectedCycle(session.ExamType);

        if (classroom.Cycle != expectedCycle)
        {
            throw Invalid(
                nameof(request.ClassroomId),
                $"Cette classe est de cycle {classroom.Cycle}, incompatible avec un examen {session.ExamType} "
                + $"(cycle {expectedCycle} attendu).");
        }

        var dossier = new ExamDossier
        {
            SchoolId = schoolId,
            ExamSessionId = session.Id,
            StudentId = request.StudentId,
            ClassroomId = request.ClassroomId
        };

        dbContext.ExamDossiers.Add(dossier);

        // Un dossier déjà ouvert pour cet élève sur cette session viole l'index unique :
        // SaveChangesAsync le traduit en DuplicateRecordException -> 409 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Id == dossier.Id)
            .Select(d => EF.Property<uint>(d, "xmin"))
            .FirstAsync(cancellationToken);

        return new ExamDossierResult(
            dossier.Id,
            dossier.ExamSessionId,
            dossier.StudentId,
            dossier.ClassroomId,
            dossier.CandidateNumber,
            dossier.ExamCenterName,
            dossier.BirthCertificatePresent,
            dossier.CivilStatusConforming,
            dossier.Status.ToString(),
            rowVersion);
    }

    /// <summary>
    /// CFEE/BFEM/BAC se passent respectivement en fin de Primaire, Collège et Lycée. On s'appuie sur
    /// <see cref="Classroom.Cycle"/> — seul champ STRUCTURÉ de niveau — plutôt que sur
    /// <see cref="Classroom.Level"/>, en texte libre par choix délibéré (voir Classroom.cs) et donc
    /// impropre à une comparaison fiable.
    /// </summary>
    private static CycleType ExpectedCycle(ExamType examType) => examType switch
    {
        ExamType.CFEE => CycleType.Primaire,
        ExamType.BFEM => CycleType.College,
        ExamType.BAC => CycleType.Lycee,
        _ => throw new ArgumentOutOfRangeException(nameof(examType), examType, "Type d'examen inconnu.")
    };

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
