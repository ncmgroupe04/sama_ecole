using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// PUT /api/v1/class-subjects/{id} — active ou désactive une matière du programme d'une classe, et règle son
/// groupe d'options (vide : matière suivie par toute la classe). Le coefficient se règle via PUT
/// /api/v1/coefficients (portée classe) : une seule écriture de coefficient dans toute l'application.
/// <see cref="RowVersion"/> : jeton xmin lu avec la ligne — 409 si elle a changé entre-temps (règle #5).
/// </summary>
public record UpdateClassSubjectCommand : IRequest<uint>, IAuditableRequest
{
    public Guid Id { get; init; }
    public required bool IsActive { get; init; }
    public string? OptionGroup { get; init; }
    public required uint RowVersion { get; init; }
}

public class UpdateClassSubjectCommandValidator : AbstractValidator<UpdateClassSubjectCommand>
{
    public UpdateClassSubjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.OptionGroup).MaximumLength(40).NoHtml();
    }
}

public class UpdateClassSubjectCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateClassSubjectCommand, uint>
{
    public async Task<uint> Handle(UpdateClassSubjectCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : la ligne d'une autre école est introuvable → 404.
        var row = await dbContext.ClassSubjects.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Matière de classe {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(row, request.RowVersion);

        row.IsActive = request.IsActive;

        // Changer de groupe ne touche pas aux choix enregistrés : un choix n'a d'effet que tant que la matière
        // est dans un groupe (SubjectFollowRules) — revenir au groupe d'origine les retrouve intacts.
        row.OptionGroup = SubjectFollowRules.NormalizeGroup(request.OptionGroup);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.ClassSubjects.AsNoTracking()
            .Where(c => c.Id == row.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);
    }
}
