using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;

public class CreateStudentUniformsCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateStudentUniformsCommand, IReadOnlyList<StudentUniformDto>>
{
    public async Task<IReadOnlyList<StudentUniformDto>> Handle(
        CreateStudentUniformsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Inscription, élève, modèles et tailles sont lus par les DbSet filtrés : ceux d'une autre école sont
        // structurellement introuvables (une FK ne vérifierait pas l'école, elle s'exécute hors RLS).
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                "Cette inscription est annulée : aucune tenue ne peut lui être attribuée.", "ENROLLMENT_CANCELLED");
        }

        var student = await dbContext.Students.AsNoTracking()
            .FirstAsync(s => s.Id == enrollment.StudentId, cancellationToken);

        var itemIds = request.Lines.Select(l => l.UniformItemId).Distinct().ToList();
        var items = await dbContext.UniformItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        if (items.Count != itemIds.Count)
        {
            throw Invalid("UniformItemId", "Un des modèles de tenue indiqués n'existe pas dans votre établissement.");
        }

        var variantIds = request.Lines.Where(l => l.UniformVariantId != null).Select(l => l.UniformVariantId!.Value).Distinct().ToList();
        var variants = await dbContext.UniformVariants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        foreach (var line in request.Lines.Where(l => l.UniformVariantId != null))
        {
            if (!variants.TryGetValue(line.UniformVariantId!.Value, out var variant) || variant.UniformItemId != line.UniformItemId)
            {
                throw Invalid("UniformVariantId", "Une des tailles indiquées n'appartient pas au modèle de tenue choisi.");
            }
        }

        // Une ligne ACTIVE par élève et par modèle. Refus explicite plutôt que de laisser parler l'index unique :
        // l'utilisateur lit quel modèle pose problème, et rien n'est enregistré (tout ou rien).
        var alreadyActive = await dbContext.StudentUniforms.AsNoTracking()
            .Where(u => u.EnrollmentId == enrollment.Id && itemIds.Contains(u.UniformItemId)
                        && (u.Status == StudentUniformStatus.Requested || u.Status == StudentUniformStatus.Delivered))
            .Select(u => u.UniformItemId)
            .ToListAsync(cancellationToken);

        if (alreadyActive.Count > 0)
        {
            var names = string.Join(", ", alreadyActive.Select(id => $"« {items[id].Name} »"));
            throw new BusinessRuleException(
                $"Cet élève a déjà une ligne active pour {names}. Modifiez la ligne existante au lieu d'en créer une seconde.",
                "UNIFORM_ALREADY_REQUESTED");
        }

        var created = new List<StudentUniform>();

        foreach (var line in request.Lines)
        {
            var item = items[line.UniformItemId];
            var variant = line.UniformVariantId is { } vid ? variants[vid] : null;

            created.Add(new StudentUniform
            {
                SchoolId = schoolId,
                StudentId = enrollment.StudentId,
                EnrollmentId = enrollment.Id,
                SchoolYearId = enrollment.SchoolYearId,
                UniformItemId = item.Id,
                UniformVariantId = variant?.Id,
                Quantity = line.Quantity,
                Status = StudentUniformStatus.Requested,
                ItemLabel = item.Name,
                SizeLabel = variant?.SizeLabel,
                StudentLabel = $"{student.FullName} ({student.Matricule})"
            });
        }

        dbContext.StudentUniforms.AddRange(created);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await StudentUniformReader.GetByIdsAsync(dbContext, created.Select(u => u.Id).ToList(), cancellationToken);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
