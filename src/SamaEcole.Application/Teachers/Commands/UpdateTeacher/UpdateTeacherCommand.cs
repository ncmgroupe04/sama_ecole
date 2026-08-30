using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Teachers.Commands.UpdateTeacher;

/// <summary>
/// PUT /api/v1/teachers/{id} — corrige les informations non financières et non sécurisées d'une fiche
/// enseignant déjà créée (état civil, contact, matières qualifiées). Réservé au Directeur et au
/// Secrétariat (TeachersController.ManageRoles), comme la création.
///
/// Volontairement ABSENTS de ce contrat : <c>Matricule</c> (généré une seule fois, règle #3) et
/// <c>UserId</c> — le rattachement d'un compte de connexion est une opération SÉCURISÉE (elle change
/// à qui appartient l'accès à la saisie de l'appel), hors du périmètre « correction de fiche
/// administrative » de ce ticket.
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation
/// (TeacherProfileDto.RowVersion) : verrouillage optimiste (AGENTS.md règle #5).
/// </summary>
public record UpdateTeacherCommand(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    DateOnly BirthDate,
    string? BirthPlace,
    string? Address,
    string? PhotoUrl,
    IReadOnlyList<Guid> SubjectIds,
    uint RowVersion,

    // Champs STATEDUC (JGK-M05) — facultatifs, position après RowVersion pour ne pas casser les
    // appelants existants. Défauts « non renseigné » : un PUT qui les omet remet donc l'enseignant
    // en « non renseigné ». L'écran renvoie toujours les valeurs courantes (round-trip), ce n'est
    // donc un souci que pour un client tiers qui construirait le corps à la main.
    string? Gender = null,
    AcademicQualification AcademicQualification = AcademicQualification.NonRenseigne,
    ProfessionalQualification ProfessionalQualification = ProfessionalQualification.NonRenseigne,
    TeacherCivilServiceStatus CivilServiceStatus = TeacherCivilServiceStatus.NonRenseigne,
    string? CivilServiceMatricule = null,
    DateOnly? FirstAppointmentDate = null) : IRequest<UpdateTeacherResult>;

public record UpdateTeacherResult(Guid Id, uint RowVersion);
