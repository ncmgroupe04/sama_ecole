using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
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
///
/// L'inscription FIGE LA DETTE et rien d'autre : elle ne crée AUCUN <see cref="Payment"/>,
/// <c>AmountPaid</c> reste à 0. Le secrétariat n'encaisse aucun fonds (AGENTS.md règle #4,
/// docs/design-references/README.md §1) ; tout règlement, y compris le premier, se fait ensuite à la
/// Caisse (RecordPaymentCommand), sur la base du reçu d'inscription.
/// </summary>
public class CreateEnrollmentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache,
    ICurrentUserService currentUser)
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

        // Garde serveur (AGENTS.md règle sur les modules) : un client qui poste un régime non-Externe
        // alors que le Directeur n'a pas activé l'Internat est rejeté en 422 — jamais accepté puis
        // silencieusement ignoré (même philosophie que la garde déjà en place pour SchoolModule.Pedagogy).
        if (request.BoardingStatus != BoardingStatus.Externe)
        {
            var settings = await dbContext.SchoolSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
            var internatEnabled = settings?.IsInternatEnabled ?? SchoolSettingsDefaults.IsInternatEnabled;

            if (!internatEnabled)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.BoardingStatus),
                        "Le module Internat n'est pas activé pour votre établissement.")
                ]);
            }
        }

        var receipt = await dbContext.ExecuteInTransactionAsync(async ct =>
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

            // Capacité revérifiée DANS la transaction (spec §5.1) : réduit la fenêtre de course sans
            // l'éliminer — un COUNT non verrouillé sous READ COMMITTED n'empêche pas deux transactions
            // strictement concurrentes de passer toutes les deux. Un verrou de ligne dédié (SELECT ...
            // FOR UPDATE) a été explicitement écarté pour cette V1 afin de ne pas introduire de nouvelle
            // mécanique de verrouillage ; à revoir si des dépassements de capacité réels remontent en
            // production.
            if (request.RoomId is { } roomId)
            {
                var room = await dbContext.Rooms.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == roomId && r.Type == RoomType.Dortoir, ct)
                    ?? throw new ValidationException([
                        new ValidationFailure(nameof(request.RoomId), "La chambre indiquée n'existe pas dans votre établissement.")
                    ]);

                var occupied = await dbContext.Enrollments.CountAsync(
                    e => e.RoomId == roomId && e.SchoolYearId == activeYear.Id && e.Status != EnrollmentStatus.Cancelled, ct);

                if (occupied >= room.Capacity)
                {
                    throw new ValidationException([
                        new ValidationFailure(nameof(request.RoomId), "Cette chambre a atteint sa capacité maximale.")
                    ]);
                }
            }

            var tuitionMonths = await ResolveTuitionMonthsAsync(ct);
            var lines = await BuildFeeLinesAsync(schoolId, request.ClassroomId, tuitionMonths, ct);

            // Pension (module Internat) : catégories IsBoardingFee, seulement pour Interne/Demi-
            // pensionnaire et seulement si IncludeBoardingFee — les élèves Externe de la même classe
            // ne voient jamais ces lignes (spec §5.1).
            if (request.BoardingStatus != BoardingStatus.Externe && request.IncludeBoardingFee)
            {
                var boardingLines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
                    dbContext, schoolId, request.ClassroomId, tuitionMonths, existingFeeCategoryIds: new HashSet<Guid>(), ct);
                lines.AddRange(boardingLines);
            }

            var totalDue = lines.Sum(l => l.LineTotal);

            // Numéro officiel du reçu (JGK-E02), attribué DANS la transaction comme le matricule : s'il y
            // a le moindre rollback ensuite, le compteur de reçus est rembobiné avec — aucun trou.
            var receiptNumber = await matriculeGenerator.GenerateNextReceiptNumberAsync(schoolId, ct);

            var enrollment = new Enrollment
            {
                SchoolId = schoolId,
                StudentId = student.Id,
                SchoolYearId = activeYear.Id,
                ClassroomId = request.ClassroomId,
                Type = request.Type,
                IsRepeating = request.IsRepeating,
                BoardingStatus = request.BoardingStatus,
                RoomId = request.RoomId,
                Status = EnrollmentStatus.Confirmed,
                TotalDue = totalDue,
                // L'inscription n'encaisse rien : la dette est intégralement à régler à la Caisse.
                AmountPaid = 0m,
                ReceiptNumber = receiptNumber,
                EnrolledAt = timeProvider.GetUtcNow()
            };

            dbContext.Enrollments.Add(enrollment);

            foreach (var line in lines)
            {
                line.SchoolId = schoolId;
                line.EnrollmentId = enrollment.Id;
                dbContext.EnrollmentFeeLines.Add(line);
            }

            // Matières optionnelles (Évolution N°6) : dans la MÊME transaction que l'inscription — jamais un élève
            // inscrit sans ses options, ni des options sans inscription. Option invalide → 422, tout est annulé.
            await StudentOptionWriter.SetAsync(
                dbContext, schoolId, currentUser.UserId?.ToString() ?? "system", student.Id, request.ClassroomId,
                activeYear.Id, request.SubjectOptionIds, fillDefaults: true, ct, nameof(request.SubjectOptionIds));

            await dbContext.SaveChangesAsync(ct);

            var school = await dbContext.Schools.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == schoolId, ct);

            return new EnrollmentReceiptDto(
                enrollment.Id,
                receiptNumber,
                school?.Name ?? string.Empty,
                school?.Address,
                school?.Phone,
                school?.Email,
                school?.Ninea,
                school?.RegistreCommerce,
                ReceiptCity.FromAddress(school?.Address),
                school?.LogoUrl,
                matricule,
                student.FullName,
                classroom.Name,
                classroom.Level,
                activeYear.Label,
                student.GuardianName,
                student.GuardianPhone,
                enrollment.Type.ToString(),
                enrollment.Status.ToString(),
                enrollment.EnrolledAt,
                lines.Select(l => new EnrollmentFeeLineDto(
                    l.Designation, l.IsRecurring, l.UnitAmount, l.Months, l.LineTotal)).ToList(),
                totalDue,
                // Rien n'est encaissé à l'inscription : ventilation vide, total à 0, aucun mode de
                // règlement. Ces champs ne sont peuplés que par la relecture d'un reçu historique
                // (GetEnrollmentReceiptQuery) et restent au DTO pour la stabilité de forme côté Finance.
                [],
                0m,
                null,
                classroom.IsAccelerated);
        }, cancellationToken);

        // Une nouvelle inscription change à la fois le dû/attendu financier et les effectifs du
        // dashboard Directeur — les deux caches doivent tomber, jamais un seul.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return receipt;
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
            FullName = request.FullName!.ToTitleCase(),
            BirthDate = request.BirthDate!.Value,
            // Obligatoire (feature E) : garanti non vide par CreateEnrollmentCommandValidator sur la branche NewEnrollment.
            BirthPlace = request.BirthPlace!,
            Gender = request.Gender!,
            ClassroomId = request.ClassroomId,
            GuardianName = request.GuardianName.ToTitleCase(),
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
    ///
    /// EXCLUT les catégories <see cref="FeeCategory.IsBoardingFee"/> (module Internat) : ces frais ne
    /// sont ajoutés que par <see cref="BoardingFeeLineBuilder"/>, conditionnés au régime d'hébergement
    /// et à <c>IncludeBoardingFee</c> — sinon un élève Externe de la même classe se verrait facturer la
    /// pension d'office dès lors que l'école aurait un tarif de pension sur cette classe (spec §5.1).
    /// </summary>
    private async Task<List<EnrollmentFeeLine>> BuildFeeLinesAsync(
        Guid schoolId, Guid classroomId, int tuitionMonths, CancellationToken ct)
    {
        var fees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId && !category.IsBoardingFee
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
