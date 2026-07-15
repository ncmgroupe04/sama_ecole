using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetClassFees;

public class GetClassFeesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassFeesQuery, IReadOnlyList<ClassFeeDto>>
{
    public async Task<IReadOnlyList<ClassFeeDto>> Handle(
        GetClassFeesQuery request, CancellationToken cancellationToken)
    {
        // Jointures vers fee_categories et classrooms pour restituer des noms lisibles. Toutes les
        // tables sont filtrées sur le même tenant (RLS + Global Query Filter), la jointure ne peut
        // donc pas faire fuiter une catégorie ou une classe d'une autre école.
        //
        // xmin est le jeton de concurrence exposé au client (EF.Property : c'est une colonne système,
        // sans propriété CLR sur l'entité).
        return await (
            from fee in dbContext.ClassFees.AsNoTracking()
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            join classroom in dbContext.Classrooms on fee.ClassroomId equals classroom.Id
            orderby category.Name, classroom.Level, classroom.Name
            select new ClassFeeDto(
                fee.Id,
                fee.FeeCategoryId,
                category.Name,
                fee.ClassroomId,
                classroom.Name,
                classroom.Level,
                fee.Amount,
                EF.Property<uint>(fee, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
