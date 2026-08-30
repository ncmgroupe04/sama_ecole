using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Queries.GetStateducReport;

/// <summary>
/// Agrège le rapport annuel STATEDUC (Volume 1 §23.3).
///
/// TROIS PRINCIPES, qui expliquent tout le reste du code :
///
/// 1. ON COMPTE DES INSCRIPTIONS, PAS DES ÉLÈVES EN BASE. Même raison que l'export Planète : la
///    population déclarée est celle qui a été scolarisée sur l'exercice, arrivées et départs compris.
///
/// 2. L'ÂGE EST CALCULÉ À LA DATE D'OBSERVATION, jamais « aujourd'hui » au moment de l'impression.
///    Sans cela, deux tirages du même rapport à six mois d'écart donneraient deux pyramides des âges
///    différentes pour la même année scolaire — et l'école serait incapable d'expliquer l'écart à
///    l'IEF.
///
/// 3. AUCUNE CASE N'EST DEVINÉE. Un enseignant sans diplôme saisi va dans <c>NonRenseigne</c>, une
///    date de naissance aberrante va dans la tranche « âge non déterminé ». Ces lignes sont visibles
///    dans le formulaire final : c'est ce qui permet de distinguer un établissement réellement peu
///    qualifié d'un établissement qui n'a pas fini sa saisie.
/// </summary>
public class GetStateducReportQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<GetStateducReportQuery, StateducReportDto>
{
    /// <summary>
    /// Bornes de plausibilité d'une date de naissance d'élève. Au-delà, la donnée est le produit d'un
    /// import raté (jour/mois inversés, année à deux chiffres) : la ranger dans une vraie tranche
    /// d'âge déplacerait un effectif réel vers une case fausse. Elle part en « âge non déterminé ».
    /// </summary>
    private const int MaxPlausibleStudentAge = 30;

    public async Task<StateducReportDto> Handle(
        GetStateducReportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement dans le jeton d'authentification.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Année scolaire introuvable dans votre établissement.");

        var now = timeProvider.GetUtcNow();
        var observationDate = request.ObservationDate ?? DateOnly.FromDateTime(now.UtcDateTime);

        var enrolled = await LoadEnrolledStudentsAsync(request.SchoolYearId, cancellationToken);

        // Chargés UNE fois et passés aux deux tableaux de personnel. Deux lectures séparées auraient
        // pu, sous écriture concurrente, produire deux effectifs enseignants différents dans le même
        // document — un formulaire dont deux tableaux se contredisent est refusé par l'IEF.
        var teachers = await LoadActiveTeachersAsync(cancellationToken);

        return new StateducReportDto(
            SchoolName: school.Name,
            NationalSchoolCode: school.NationalSchoolCode,
            MinistryAuthorizationNumber: school.MinistryAuthorizationNumber,
            SchoolDistrictCode: school.SchoolDistrictCode,
            InspectionAcademie: school.InspectionAcademie,
            InspectionEducationFormation: school.InspectionEducationFormation,
            GpsCoordinates: school.GpsCoordinates,
            Address: school.Address,
            Phone: school.Phone,
            Email: school.Email,
            SchoolYearLabel: schoolYear.Label,
            ObservationDate: observationDate,
            GeneratedAt: now,
            EnrollmentsByLevel: BuildEnrollmentsByLevel(enrolled),
            AgePyramid: BuildAgePyramid(enrolled, observationDate),
            TeacherQualifications: BuildTeacherQualifications(teachers),
            TeacherStatuses: BuildTeacherStatuses(teachers),
            ClassroomCount: await dbContext.Classrooms.AsNoTracking().CountAsync(cancellationToken),
            PhysicalRoomCount: await dbContext.Rooms.AsNoTracking().CountAsync(cancellationToken),
            BuildingCount: await dbContext.Buildings.AsNoTracking().CountAsync(cancellationToken));
    }

    /// <summary>
    /// La population déclarée : une ligne par inscription non annulée de l'exercice, jointe à l'élève
    /// et à la classe de l'inscription (figée — voir GetPlaneteExportQueryHandler).
    /// </summary>
    private async Task<List<EnrolledStudent>> LoadEnrolledStudentsAsync(
        Guid schoolYearId, CancellationToken cancellationToken) =>
        await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.SchoolYearId == schoolYearId && e.Status != EnrollmentStatus.Cancelled)
            .Join(dbContext.Students.AsNoTracking(),
                e => e.StudentId, s => s.Id, (e, s) => new { e, s })
            .Join(dbContext.Classrooms.AsNoTracking(),
                pair => pair.e.ClassroomId, c => c.Id, (pair, c) => new EnrolledStudent(
                    c.Level,
                    c.Cycle,
                    pair.e.ClassroomId,
                    pair.s.Gender,
                    pair.s.BirthDate,
                    pair.e.IsRepeating,
                    pair.s.IenNumber))
            .ToListAsync(cancellationToken);

    private static List<StateducLevelEnrollmentRow> BuildEnrollmentsByLevel(
        IReadOnlyList<EnrolledStudent> enrolled) =>
        enrolled
            .GroupBy(s => new { s.Level, s.Cycle })
            .OrderBy(g => g.Key.Cycle).ThenBy(g => g.Key.Level)
            .Select(g => new StateducLevelEnrollmentRow(
                Level: g.Key.Level,
                Cycle: g.Key.Cycle.ToString(),
                Boys: g.Count(s => IsMale(s.Gender)),
                Girls: g.Count(s => IsFemale(s.Gender)),
                Repeaters: g.Count(s => s.IsRepeating),
                WithoutIen: g.Count(s => string.IsNullOrWhiteSpace(s.IenNumber)),
                // Divisions RÉELLEMENT peuplées sur ce niveau, comptées depuis les inscriptions et
                // non depuis la table des classes : une 6e C ouverte dans les paramètres mais sans un
                // seul inscrit ne doit pas gonfler le nombre de divisions déclarées — c'est sur ce
                // chiffre que se calcule l'effectif moyen par classe transmis au ministère.
                ClassroomCount: g.Select(s => s.ClassroomId).Distinct().Count()))
            .ToList();

    /// <summary>
    /// Pyramide des âges. Les tranches présentes sont celles RÉELLEMENT peuplées, triées par âge
    /// croissant ; la tranche « non déterminé » ferme la liste et n'apparaît que si elle contient
    /// quelqu'un — un formulaire n'a pas à porter une ligne d'anomalie quand il n'y a pas d'anomalie.
    /// </summary>
    private static List<StateducAgeBracketRow> BuildAgePyramid(
        IReadOnlyList<EnrolledStudent> enrolled, DateOnly observationDate) =>
        enrolled
            .GroupBy(s => AgeAt(s.BirthDate, observationDate))
            .OrderBy(g => g.Key ?? int.MaxValue)
            .Select(g => new StateducAgeBracketRow(
                Age: g.Key,
                Boys: g.Count(s => IsMale(s.Gender)),
                Girls: g.Count(s => IsFemale(s.Gender))))
            .ToList();

    private static List<StateducTeacherQualificationRow> BuildTeacherQualifications(
        IReadOnlyList<TeacherRecord> teachers) =>
        teachers
            .GroupBy(t => new { t.AcademicQualification, t.ProfessionalQualification })
            .OrderBy(g => g.Key.ProfessionalQualification).ThenBy(g => g.Key.AcademicQualification)
            .Select(g => new StateducTeacherQualificationRow(
                g.Key.AcademicQualification,
                g.Key.ProfessionalQualification,
                Men: g.Count(t => IsMale(t.Gender)),
                Women: g.Count(t => IsFemale(t.Gender)),
                // Fiches dont le genre n'a jamais été saisi (Teacher.Gender nullable). Comptées à
                // part, jamais imputées à l'une des deux colonnes réglementaires : voir la remarque
                // de StateducTeacherQualificationRow.
                GenderNotReported: g.Count(t => !IsMale(t.Gender) && !IsFemale(t.Gender))))
            .ToList();

    private static List<StateducTeacherStatusRow> BuildTeacherStatuses(
        IReadOnlyList<TeacherRecord> teachers) =>
        teachers
            .GroupBy(t => t.CivilServiceStatus)
            .OrderBy(g => g.Key)
            .Select(g => new StateducTeacherStatusRow(
                g.Key,
                Men: g.Count(t => IsMale(t.Gender)),
                Women: g.Count(t => IsFemale(t.Gender)),
                GenderNotReported: g.Count(t => !IsMale(t.Gender) && !IsFemale(t.Gender))))
            .ToList();

    /// <summary>
    /// Enseignants ACTIFS de l'établissement. Le formulaire décrit le personnel EN POSTE, pas
    /// l'historique des passages : un enseignant archivé compté au STATEDUC améliorerait
    /// artificiellement le taux d'encadrement déclaré.
    ///
    /// Une seule lecture partagée par les deux tableaux de personnel — deux requêtes séparées auraient
    /// pu, sous écriture concurrente, produire deux effectifs différents dans le même document.
    /// </summary>
    private async Task<List<TeacherRecord>> LoadActiveTeachersAsync(CancellationToken cancellationToken) =>
        await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Status == EntityStatus.Active)
            .Select(t => new TeacherRecord(
                t.AcademicQualification,
                t.ProfessionalQualification,
                t.CivilServiceStatus,
                t.Gender))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Âge révolu à la date d'observation. Null si la date de naissance est absurde (postérieure à
    /// l'observation, ou plus de <see cref="MaxPlausibleStudentAge"/> ans) : voir le principe 3.
    /// </summary>
    private static int? AgeAt(DateOnly birthDate, DateOnly observationDate)
    {
        if (birthDate > observationDate)
        {
            return null;
        }

        var age = observationDate.Year - birthDate.Year;

        // L'anniversaire n'est pas encore passé cette année-là : on retire l'année entamée.
        if (birthDate > observationDate.AddYears(-age))
        {
            age--;
        }

        return age is >= 0 and <= MaxPlausibleStudentAge ? age : null;
    }

    // Le genre est stocké « M » / « F » (Student.Gender, Teacher.Gender). Comparaison insensible à la
    // casse et tolérante à l'espace : les imports Excel produisent « m », « F  », parfois « f ».
    //
    // Une valeur null ou non reconnue n'est comptée NI en garçons NI en filles. Côté élèves, l'écart
    // entre le total du tableau et l'effectif réel devient alors visible ; côté enseignants, elle
    // tombe dans la colonne GenderNotReported. Dans les deux cas l'anomalie se voit — là où la ranger
    // d'office dans l'une des deux colonnes la rendrait indétectable.
    private static bool IsMale(string? gender) =>
        gender?.Trim().Equals("M", StringComparison.OrdinalIgnoreCase) ?? false;

    private static bool IsFemale(string? gender) =>
        gender?.Trim().Equals("F", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Projection intermédiaire : une inscription résolue, prête à être agrégée en mémoire.</summary>
    private record EnrolledStudent(
        string Level,
        CycleType Cycle,
        Guid ClassroomId,
        string Gender,
        DateOnly BirthDate,
        bool IsRepeating,
        string? IenNumber);

    /// <summary>Projection intermédiaire d'un enseignant actif, pour les deux tableaux de personnel.</summary>
    private record TeacherRecord(
        AcademicQualification AcademicQualification,
        ProfessionalQualification ProfessionalQualification,
        TeacherCivilServiceStatus CivilServiceStatus,
        string? Gender);
}
