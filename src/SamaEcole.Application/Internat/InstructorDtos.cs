using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat;

/// <summary>
/// Fiche d'un Oustaz pour la Direction. Sert À LA FOIS de résultat de Create/UpdateInstructorCommand et d'élément de
/// liste (ListInstructorsQuery) — pas de type Result séparé, même choix que QuranProgressDto.
/// </summary>
/// <param name="UserId">Compte de connexion lié (rôle Enseignant), ou <c>null</c> si l'Oustaz n'a pas de compte.</param>
/// <param name="UserEmail">Identifiant de connexion du compte lié, pour que la Direction reconnaisse le compte sans le chercher.</param>
/// <param name="StudentCount">Élèves actuellement rattachés à sa Halqa.</param>
/// <param name="RowVersion">Jeton xmin à renvoyer tel quel à la prochaine modification (AGENTS.md règle #5).</param>
public record InstructorDto(
    Guid Id,
    string FullName,
    string? FullNameAr,
    string? Phone,
    EntityStatus Status,
    Guid? UserId,
    string? UserEmail,
    int StudentCount,
    uint RowVersion);

internal static class InstructorProjection
{
    /// <summary>
    /// Projection unique des fiches d'Oustaz (liste ET résultat des commandes) : l'effectif de la Halqa et le
    /// compte lié sont calculés en base, jamais en chargeant les élèves. Le Global Query Filter et la RLS bornent
    /// déjà la lecture à l'école courante.
    ///
    /// Le filtre par identifiant et le tri sont posés AVANT la projection : EF Core ne sait pas composer un Where ni un
    /// OrderBy sur un type construit par constructeur (<c>new InstructorDto(...)</c>). <paramref name="onlyId"/> limite
    /// à une fiche (résultat d'une commande) ; sans lui, toutes les fiches, triées par nom.
    /// </summary>
    public static IQueryable<InstructorDto> Dtos(IApplicationDbContext dbContext, Guid? onlyId = null) =>
        from i in dbContext.Instructors.AsNoTracking()
        where onlyId == null || i.Id == onlyId
        join u in dbContext.Users.AsNoTracking() on i.UserId equals u.Id into linked
        from u in linked.DefaultIfEmpty()
        orderby i.FullName
        select new InstructorDto(
            i.Id,
            i.FullName,
            i.FullNameAr,
            i.Phone,
            i.Status,
            i.UserId,
            u.Email,
            dbContext.Students.Count(s => s.InstructorId == i.Id),
            EF.Property<uint>(i, "xmin"));
}

/// <summary>
/// Garde partagée par CreateInstructorCommandHandler et UpdateInstructorCommandHandler : un <c>UserId</c> rattaché à
/// un Oustaz doit désigner un compte Enseignant DE CETTE école, pas déjà rattaché à un autre Oustaz. Même règle que
/// <c>TeacherUserAccountLink</c> (ticket JGK-D06), avec une différence voulue : un compte absent de l'école — qu'il
/// n'existe nulle part ou qu'il appartienne à une AUTRE école — renvoie le même 404 neutre, pour qu'on ne puisse pas
/// énumérer les comptes des autres établissements en testant des identifiants.
/// </summary>
internal static class InstructorUserAccountLink
{
    public static async Task EnsureLinkableAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        Guid userId,
        Guid? excludeInstructorId,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.SchoolId == schoolId)
            .Select(u => (Role?)u.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            throw new KeyNotFoundException("Compte utilisateur introuvable dans votre établissement.");
        }

        if (role != Role.Enseignant)
        {
            throw new ValidationException([
                new ValidationFailure("UserId", "Seul un compte de rôle Enseignant peut être rattaché à un Oustaz.")
            ]);
        }

        var alreadyLinked = await dbContext.Instructors
            .AnyAsync(i => i.UserId == userId && (excludeInstructorId == null || i.Id != excludeInstructorId), cancellationToken);

        if (alreadyLinked)
        {
            throw new ValidationException([
                new ValidationFailure("UserId", "Ce compte est déjà rattaché à un autre Oustaz.")
            ]);
        }
    }
}
