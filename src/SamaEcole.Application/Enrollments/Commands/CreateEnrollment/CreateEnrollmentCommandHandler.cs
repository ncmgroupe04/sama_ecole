using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Commands.CreateEnrollment;

/// <summary>
/// Cœur du ticket JGK-E01. Suit le patron de référence du module Students : dépendances minimales, le
/// tenant vient d'<see cref="ITenantProvider"/>, le matricule est généré dans la MÊME transaction que
/// l'insertion (AGENTS.md règle #3) — jamais avant. Toute l'opération (création éventuelle de l'élève,
/// génération du matricule, inscription, lignes de frais) est atomique : le moindre échec rembobine
/// tout, matricule compris — aucun trou de numérotation, aucun compte financier orphelin.
///
/// Le montant dû est CALCULÉ ici à partir du barème de la classe (JGK-F01), figé ligne à ligne, et
/// n'est jamais lu de la requête (règle #4 et #10).
/// </summary>
public class CreateEnrollmentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator,
    TimeProvider timeProvider)
    : IRequestHandler<CreateEnrollmentCommand, EnrollmentReceiptDto>
{
    public async Task<EnrollmentReceiptDto> Handle(CreateEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // L'inscription porte l'année ACTIVE (mission JGK-E01). Sans année active, l'école ne peut rien
        // rattacher : on refuse en 422 plutôt que de deviner une année.
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    "SchoolYear",
                    "Aucune année scolaire active. Activez une année scolaire avant d'inscrire un élève.")
            ]);

        // La classe doit exister DANS CETTE ÉCOLE. Le Global Query Filter restreint déjà au tenant : une
        // classe d'une autre école est introuvable ici, et le contrôle vaut appartenance autant
        // qu'existence (même raisonnement que CreateStudentCommandHandler).
        var classroom = await dbContext.Classrooms
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    nameof(request.ClassroomId),
                    "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var (student, matricule) = request.Type == EnrollmentType.NewEnrollment
                ? await CreateStudentAsync(schoolId, request, ct)
                : await LoadStudentForReEnrollmentAsync(request, ct);

            // Un élève n'a qu'une inscription (non annulée) par année (DDS §5.4). L'index unique en base
            // en est la garantie dure ; ce pré-contrôle offre un message lisible plutôt qu'un 409 brut.
            var alreadyEnrolled = await dbContext.Enrollments.AnyAsync(
                e => e.StudentId == student.Id
                     && e.SchoolYearId == activeYear.Id
                     && e.Status != EnrollmentStatus.Cancelled,
                ct);

            if (alreadyEnrolled)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.StudentId),
                        $"Cet élève est déjà inscrit pour l'année {activeYear.Label}.")
                ]);
            }

            var tuitionMonths = await ResolveTuitionMonthsAsync(ct);
            var lines = await BuildFeeLinesAsync(schoolId, request.ClassroomId, tuitionMonths, ct);
            var totalDue = lines.Sum(l => l.LineTotal);

            var enrollment = new Enrollment
            {
                SchoolId = schoolId,
                StudentId = student.Id,
                SchoolYearId = activeYear.Id,
                ClassroomId = request.ClassroomId,
                Type = request.Type,
                Status = EnrollmentStatus.Confirmed,
                TotalDue = totalDue,
                EnrolledAt = timeProvider.GetUtcNow()
            };

            dbContext.Enrollments.Add(enrollment);

            foreach (var line in lines)
            {
                line.SchoolId = schoolId;
                line.EnrollmentId = enrollment.Id;
                dbContext.EnrollmentFeeLines.Add(line);
            }

            await dbContext.SaveChangesAsync(ct);

            var school = await dbContext.Schools.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == schoolId, ct);

            return new EnrollmentReceiptDto(
                enrollment.Id,
                school?.Name ?? string.Empty,
                school?.Phone,
                matricule,
                student.FullName,
                classroom.Name,
                classroom.Level,
                activeYear.Label,
                enrollment.Type.ToString(),
                enrollment.Status.ToString(),
                enrollment.EnrolledAt,
                lines.Select(l => new EnrollmentFeeLineDto(
                    l.Designation, l.IsRecurring, l.UnitAmount, l.Months, l.LineTotal)).ToList(),
                totalDue);
        }, cancellationToken);
    }

    private async Task<(Student student, string matricule)> CreateStudentAsync(
        Guid schoolId, CreateEnrollmentCommand request, CancellationToken ct)
    {
        // Génération du matricule dans la transaction (règle #3), exactement comme JGK-D01.
        var matricule = await matriculeGenerator.GenerateNextStudentMatriculeAsync(schoolId, ct);

        var student = new Student
        {
            SchoolId = schoolId,
            Matricule = matricule,
            FullName = request.FullName!,
            BirthDate = request.BirthDate!.Value,
            Gender = request.Gender!,
            ClassroomId = request.ClassroomId,
            GuardianName = request.GuardianName,
            GuardianPhone = request.GuardianPhone
        };

        dbContext.Students.Add(student);
        return (student, matricule);
    }

    private async Task<(Student student, string matricule)> LoadStudentForReEnrollmentAsync(
        CreateEnrollmentCommand request, CancellationToken ct)
    {
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct)
            ?? throw new ValidationException([
                new ValidationFailure(
                    nameof(request.StudentId),
                    "L'élève à réinscrire n'existe pas dans votre établissement.")
            ]);

        // La réinscription fait de la nouvelle classe la classe COURANTE de l'élève : sans cette mise à
        // jour, sa fiche continuerait d'afficher la classe de l'an dernier. Aucun nouveau matricule —
        // c'est le même élève (règle métier de la réinscription, Volume 1 §6.2).
        student.ClassroomId = request.ClassroomId;

        return (student, student.Matricule);
    }

    private async Task<int> ResolveTuitionMonthsAsync(CancellationToken ct)
    {
        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var months = settings?.TuitionMonthsPerYear ?? SchoolSettingsDefaults.TuitionMonthsPerYear;

        // Filet : un réglage à 0 (jamais posé par la validation, mais possible via un import direct)
        // annulerait toute scolarité. On retombe alors sur le défaut plutôt que de facturer 0.
        return months < SchoolSettingsDefaults.MinTuitionMonths
            ? SchoolSettingsDefaults.TuitionMonthsPerYear
            : months;
    }

    /// <summary>
    /// Construit l'instantané des frais de la classe. Une mensualité (catégorie récurrente) est
    /// multipliée par le nombre de mensualités de l'année ; un frais ponctuel ne l'est jamais
    /// (règle métier portée par <see cref="FeeCategory.IsRecurring"/>). Les montants sont COPIÉS, pas
    /// référencés : le reçu reste fidèle même si le barème change ensuite (JGK-F01 l'historise).
    /// </summary>
    private async Task<List<EnrollmentFeeLine>> BuildFeeLinesAsync(
        Guid schoolId, Guid classroomId, int tuitionMonths, CancellationToken ct)
    {
        var fees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId
            orderby category.IsRecurring, category.Name
            select new { fee.FeeCategoryId, category.Name, category.IsRecurring, fee.Amount })
            .ToListAsync(ct);

        return fees.Select(f =>
        {
            var months = f.IsRecurring ? tuitionMonths : 1;
            return new EnrollmentFeeLine
            {
                SchoolId = schoolId,
                FeeCategoryId = f.FeeCategoryId,
                Designation = f.Name,
                IsRecurring = f.IsRecurring,
                UnitAmount = f.Amount,
                Months = months,
                LineTotal = f.Amount * months
            };
        }).ToList();
    }
}
