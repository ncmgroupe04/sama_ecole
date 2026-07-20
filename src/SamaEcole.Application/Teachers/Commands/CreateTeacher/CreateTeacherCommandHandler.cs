using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.CreateTeacher;

public class CreateTeacherCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator)
    : IRequestHandler<CreateTeacherCommand, CreateTeacherResult>
{
    public async Task<CreateTeacherResult> Handle(CreateTeacherCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Les matières doivent exister DANS CETTE ÉCOLE — même garde que ClassroomId dans
        // CreateStudentCommandHandler : sans elle, la FK composite rejetterait bien la ligne, mais
        // sous la forme d'une DbUpdateException remontée en 500 plutôt qu'une erreur de saisie
        // exploitable sur le bon champ.
        var existingSubjectCount = await dbContext.Subjects
            .CountAsync(s => request.SubjectIds.Contains(s.Id), cancellationToken);

        if (existingSubjectCount != request.SubjectIds.Count)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.SubjectIds),
                    "Une ou plusieurs matières indiquées n'existent pas dans votre établissement.")
            ]);
        }

        // Lien vers un compte de connexion (ticket JGK-D06) : facultatif, mais s'il est fourni il doit
        // désigner un Enseignant de CETTE école, pas encore rattaché à une autre fiche. On refuse en
        // 422 sur le bon champ plutôt que de laisser une FK/contrainte d'unicité remonter en 500/409.
        if (request.UserId is { } userId)
        {
            // AsNoTracking : ce compte est seulement inspecté (rôle, unicité du rattachement) ici,
            // jamais modifié — la fiche enseignant créée plus bas référence son Id, pas l'entité même.
            var user = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && u.SchoolId == schoolId, cancellationToken);

            if (user is null)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.UserId), "Le compte indiqué n'existe pas dans votre établissement.")
                ]);
            }

            if (user.Role != Role.Enseignant)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.UserId), "Seul un compte de rôle Enseignant peut être rattaché à une fiche enseignant.")
                ]);
            }

            var alreadyLinked = await dbContext.Teachers.AnyAsync(t => t.UserId == userId, cancellationToken);
            if (alreadyLinked)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.UserId), "Ce compte est déjà rattaché à une autre fiche enseignant.")
                ]);
            }
        }

        // Génération du matricule ET insertion dans une seule transaction (AGENTS.md règle #3) :
        // si l'insertion échoue, le compteur de matricules est rembobiné avec elle — aucun trou.
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var matricule = await matriculeGenerator.GenerateNextTeacherMatriculeAsync(schoolId, ct);

            var teacher = new Teacher
            {
                SchoolId = schoolId,
                Matricule = matricule,
                FullName = request.FullName,
                Email = request.Email,
                Phone = request.Phone,
                BirthPlace = request.BirthPlace,
                PhotoUrl = request.PhotoUrl,
                PhotoData = request.PhotoData is null ? null : Convert.FromBase64String(request.PhotoData),
                UserId = request.UserId
            };

            dbContext.Teachers.Add(teacher);

            foreach (var subjectId in request.SubjectIds)
            {
                dbContext.TeacherSubjects.Add(new TeacherSubject
                {
                    SchoolId = schoolId,
                    TeacherId = teacher.Id,
                    SubjectId = subjectId
                });
            }

            await dbContext.SaveChangesAsync(ct);

            return new CreateTeacherResult(teacher.Id, teacher.Matricule);
        }, cancellationToken);
    }
}
