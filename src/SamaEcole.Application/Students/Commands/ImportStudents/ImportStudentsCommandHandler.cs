using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.ImportStudents;

public class ImportStudentsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IStudentImportFileParser fileParser,
    IMatriculeGenerator matriculeGenerator)
    : IRequestHandler<ImportStudentsCommand, ImportStudentsResult>
{
    /// <summary>
    /// Au-delà d'une rentrée scolaire complète (le cas cité, ~800 élèves) : borne de sécurité contre un
    /// fichier malformé (des milliers de lignes vides détectées comme données) plutôt qu'une limite
    /// métier réaliste — scinder un fichier plus gros reste toujours possible.
    /// </summary>
    private const int MaxRows = 1000;

    private static readonly string[] BirthDateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    private record ParsedStudentRow(
        string FullName, DateOnly BirthDate, string BirthPlace, string Gender, Guid ClassroomId,
        string? GuardianName, string? GuardianPhone, string? GuardianEmail, string? Address);

    public async Task<ImportStudentsResult> Handle(ImportStudentsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Lecture BRUTE : IStudentImportFileParser lève déjà une ValidationException si le format est
        // illisible, l'extension non supportée, ou le fichier ne contient aucune ligne de données.
        var fileRows = fileParser.Parse(request.FileContent, request.FileName);

        if (fileRows.Count > MaxRows)
        {
            throw new ValidationException([
                new ValidationFailure(
                    "File", $"Le fichier compte {fileRows.Count} lignes, au-delà de la limite de {MaxRows} : scindez-le en plusieurs imports.")
            ]);
        }

        // Roster des classes de l'ÉCOLE (pas de l'année scolaire) : une classe existe indépendamment de
        // l'exercice, exactement comme pour la création unitaire (CreateStudentCommandHandler).
        //
        // Construit à la main (pas ToDictionaryAsync) : l'index unique de Classroom porte sur le nom
        // EXACT (sensible à la casse), donc « CM2 A » et « cm2 a » peuvent légitimement coexister en
        // base — un ToDictionary insensible à la casse lèverait alors une exception sur la clé en
        // double. On garde ici la PREMIÈRE occurrence rencontrée, jamais un plantage sur ce cas limite.
        var classroomsByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var classroom in await dbContext.Classrooms.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken))
        {
            classroomsByName.TryAdd(classroom.Name.Trim(), classroom.Id);
        }

        var results = new List<ImportStudentsRowResult>();
        var parsedRows = new List<ParsedStudentRow>();

        foreach (var row in fileRows)
        {
            var (fieldErrors, parsed) = ValidateRow(row, classroomsByName);

            results.Add(new ImportStudentsRowResult(
                row.RowNumber, fieldErrors.Count == 0,
                row.FullName, row.BirthDate, row.BirthPlace, row.Gender, row.ClassroomName,
                row.GuardianName, row.GuardianPhone, row.GuardianEmail, row.Address,
                fieldErrors));

            if (parsed is not null)
            {
                parsedRows.Add(parsed);
            }
        }

        var invalidCount = results.Count(r => !r.IsValid);

        if (request.DryRun)
        {
            // Aperçu : aucune écriture, jamais un matricule consommé pour une ligne qui ne sera peut-être
            // jamais confirmée (voir le commentaire de classe d'ImportStudentsCommand).
            return new ImportStudentsResult(
                DryRun: true, Committed: false, fileRows.Count, results.Count - invalidCount, invalidCount, Created: 0, results);
        }

        if (invalidCount > 0)
        {
            // RE-validation côté confirmation : rejet intégral, RIEN n'est écrit (AGENTS.md règle #5 —
            // même esprit que ImportGradesCommandHandler). Chaque ligne en erreur devient une entrée
            // "Ligne N" agrégeant tous ses champs fautifs, pour rester dans le format d'erreur normalisé.
            var errors = results
                .Where(r => !r.IsValid)
                .Select(r => new ValidationFailure(
                    $"Ligne {r.RowNumber}", string.Join(" ", r.FieldErrors.Values)));

            throw new ValidationException(errors);
        }

        // Fichier entièrement valide : chaque élève est créé dans UNE seule transaction (règle #5) — le
        // matricule de CHAQUE ligne est généré ICI, séquentiellement, DANS la transaction (règle #3) :
        // si l'import échoue en cours de route, tous les compteurs consommés sont rembobinés avec elle.
        var created = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var parsed in parsedRows)
            {
                var matricule = await matriculeGenerator.GenerateNextStudentMatriculeAsync(schoolId, ct);

                dbContext.Students.Add(new Student
                {
                    SchoolId = schoolId,
                    Matricule = matricule,
                    FullName = parsed.FullName,
                    BirthDate = parsed.BirthDate,
                    BirthPlace = parsed.BirthPlace,
                    Gender = parsed.Gender,
                    ClassroomId = parsed.ClassroomId,
                    GuardianName = parsed.GuardianName,
                    GuardianPhone = parsed.GuardianPhone,
                    GuardianEmail = parsed.GuardianEmail,
                    Address = parsed.Address
                });
            }

            await dbContext.SaveChangesAsync(ct);
            return parsedRows.Count;
        }, cancellationToken);

        return new ImportStudentsResult(
            DryRun: false, Committed: true, fileRows.Count, ValidRows: created, InvalidRows: 0, Created: created, results);
    }

    /// <summary>
    /// Valide UNE ligne, en accumulant TOUTES ses erreurs par champ (pas seulement la première) : l'écran
    /// d'aperçu doit pouvoir colorer chaque cellule fautive indépendamment des autres. Renvoie la ligne
    /// PARSÉE (prête à insérer) uniquement si elle est entièrement valide.
    /// </summary>
    private static (Dictionary<string, string> Errors, ParsedStudentRow? Parsed) ValidateRow(
        StudentImportFileRow row, IReadOnlyDictionary<string, Guid> classroomsByName)
    {
        var errors = new Dictionary<string, string>();

        var fullName = row.FullName.Trim();
        if (fullName.Length == 0)
        {
            errors["fullName"] = "Le nom complet est obligatoire.";
        }
        else if (fullName.Length > 200)
        {
            errors["fullName"] = "Le nom complet ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(fullName))
        {
            errors["fullName"] = SafeTextValidation.ErrorMessage;
        }

        DateOnly birthDate = default;
        var rawBirthDate = row.BirthDate.Trim();
        if (rawBirthDate.Length == 0)
        {
            errors["birthDate"] = "La date de naissance est obligatoire.";
        }
        else if (!DateOnly.TryParseExact(rawBirthDate, BirthDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out birthDate))
        {
            errors["birthDate"] = "Date invalide : utilisez le format jj/mm/aaaa.";
        }
        else if (birthDate >= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors["birthDate"] = "La date de naissance doit être dans le passé.";
        }

        var birthPlace = row.BirthPlace.Trim();
        if (birthPlace.Length == 0)
        {
            // Obligatoire au Sénégal (feature E, Student.BirthPlace) — même règle que la création unitaire.
            errors["birthPlace"] = "Le lieu de naissance est obligatoire.";
        }
        else if (birthPlace.Length > 200)
        {
            errors["birthPlace"] = "Le lieu de naissance ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(birthPlace))
        {
            errors["birthPlace"] = SafeTextValidation.ErrorMessage;
        }

        var rawGender = row.Gender.Trim();
        var gender = rawGender.ToUpperInvariant();
        if (gender is not ("M" or "F"))
        {
            errors["gender"] = "Le genre doit être « M » ou « F ».";
        }

        Guid classroomId = default;
        var className = row.ClassroomName.Trim();
        if (className.Length == 0)
        {
            errors["classroomName"] = "La classe est obligatoire.";
        }
        else if (!classroomsByName.TryGetValue(className, out classroomId))
        {
            errors["classroomName"] = $"Classe « {className} » introuvable : vérifiez l'orthographe ou créez-la d'abord.";
        }

        var guardianName = row.GuardianName.Trim();
        if (guardianName.Length > 200)
        {
            errors["guardianName"] = "Le nom du tuteur ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(guardianName))
        {
            errors["guardianName"] = SafeTextValidation.ErrorMessage;
        }

        var guardianPhone = row.GuardianPhone.Trim();
        if (guardianPhone.Length > 30)
        {
            errors["guardianPhone"] = "Le téléphone du tuteur ne peut pas dépasser 30 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(guardianPhone))
        {
            errors["guardianPhone"] = SafeTextValidation.ErrorMessage;
        }

        var guardianEmail = row.GuardianEmail.Trim();
        if (guardianEmail.Length > 255)
        {
            errors["guardianEmail"] = "L'e-mail du tuteur ne peut pas dépasser 255 caractères.";
        }
        else if (guardianEmail.Length > 0 && !IsValidEmail(guardianEmail))
        {
            errors["guardianEmail"] = "L'e-mail du tuteur n'est pas valide.";
        }
        else if (!SafeTextValidation.IsSafeText(guardianEmail))
        {
            errors["guardianEmail"] = SafeTextValidation.ErrorMessage;
        }

        var address = row.Address.Trim();
        if (address.Length > 300)
        {
            errors["address"] = "L'adresse ne peut pas dépasser 300 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(address))
        {
            errors["address"] = SafeTextValidation.ErrorMessage;
        }

        if (errors.Count > 0)
        {
            return (errors, null);
        }

        return (errors, new ParsedStudentRow(
            fullName, birthDate, birthPlace, gender, classroomId,
            guardianName.Length == 0 ? null : guardianName,
            guardianPhone.Length == 0 ? null : guardianPhone,
            guardianEmail.Length == 0 ? null : guardianEmail,
            address.Length == 0 ? null : address));
    }

    /// <summary>Même contrat que EmailAddress() de FluentValidation (CreateStudentCommandValidator) : un
    /// contrôle de format simple, pas une vérification d'existence du domaine.</summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
