using FluentValidation;
using MediatR;

namespace SamaEcole.Application.RollBook.Queries.GetRollBookGrid;

/// <summary>
/// GET /api/v1/roll-book?classroomId=&amp;month=YYYY-MM — la grille mensuelle d'une classe : un cahier de roulement,
/// avec les saisies du cahier fusionnées aux appels par séance (la saisie prime).
/// </summary>
public record GetRollBookGridQuery(Guid ClassroomId, string Month) : IRequest<RollBookGridDto>;

public class GetRollBookGridQueryValidator : AbstractValidator<GetRollBookGridQuery>
{
    public GetRollBookGridQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty().WithMessage("La classe est obligatoire.");
        RuleFor(q => q.Month)
            .Must(m => RollBookPeriods.TryParseMonth(m, out _, out _))
            .WithMessage("Le mois doit être au format AAAA-MM (ex. 2026-11).");
    }
}

public class GetRollBookGridQueryHandler(RollBookEngine engine, RollBookScopeAuthorizer scope)
    : IRequestHandler<GetRollBookGridQuery, RollBookGridDto>
{
    public async Task<RollBookGridDto> Handle(GetRollBookGridQuery request, CancellationToken cancellationToken)
    {
        RollBookPeriods.TryParseMonth(request.Month, out var from, out var to);

        var year = await engine.ResolveYearAsync(from, to, cancellationToken);

        // Portée : un Enseignant ne voit que les classes où il est affecté (403 sinon).
        await scope.EnsureCanAccessClassroomAsync(request.ClassroomId, year.Id, cancellationToken);

        var book = await engine.BuildAsync(request.ClassroomId, year, from, to, cancellationToken);

        return RollBookGridDto.Create(book, year, request.Month, from, to);
    }
}
