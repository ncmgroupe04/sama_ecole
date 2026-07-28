using System.Globalization;
using System.Text.RegularExpressions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.ImportTeachers;

public partial class ImportTeachersCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ITeacherImportFileParser fileParser,
    IMatriculeGenerator matriculeGenerator)
    : IRequestHandler<ImportTeachersCommand, ImportTeachersResult>
{
    /// <summary>Borne de sécurité contre un fichier malformé, pas une limite métier réaliste — même
    /// contrat que ImportStudentsCommandHandler.MaxRows.</summary>
    private const int MaxRows = 1000;

    private static readonly string[] BirthDateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    [GeneratedRegex(@"^(?<name>.+?)\s*\((?<level>[^()]+)\)$")]
    private static partial Regex SubjectWithLevelPattern();

    private record ParsedTeacherRow(
        string FullName, string Email, string Phone, DateOnly BirthDate, string? BirthPlace,
        IReadOnlyList<Guid> SubjectIds);

    public async Task<ImportTeachersResult> Handle(ImportTeachersCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Lecture BRUTE : ITeacherImportFileParser lève déjà une ValidationException si le format est
        // illisible, l'extension non supportée, ou le fichier ne contient aucune ligne de données.
        var fileRows = fileParser.Parse(request.FileContent, request.FileName);

        if (fileRows.Count > MaxRows)
        {
            throw new ValidationException([
                new ValidationFailure(
                    "File", $"Le fichier compte {fileRows.Count} lignes, au-delà de la limite de {MaxRows} : scindez-le en plusieurs imports.")
            ]);
        }

        // Matières de l'ÉCOLE courante — voir Subject : la clé métier est (Niveau, Nom), donc un même
        // nom peut désigner plusieurs matières distinctes. Deux index : l'un par nom seul (pour détecter
        // l'ambiguïté), l'autre par (nom, niveau) pour la résolution explicite « Nom (Niveau) ».
        var subjects = await dbContext.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Level })
            .ToListAsync(cancellationToken);

        var byNameOnly = new Dictionary<string, List<(Guid Id, string Level)>>(StringComparer.OrdinalIgnoreCase);
        var byNameAndLevel = new Dictionary<(string Name, string Level), Guid>(NameLevelComparer.Instance);
        foreach (var subject in subjects)
        {
            if (!byNameOnly.TryGetValue(subject.Name, out var list))
            {
                list = [];
                byNameOnly[subject.Name] = list;
            }
            list.Add((subject.Id, subject.Level));

            byNameAndLevel.TryAdd((subject.Name, subject.Level), subject.Id);
        }

        // Doublons contre l'existant : un enseignant déjà enregistré avec le même e-mail ou le même
        // téléphone ne doit jamais être recréé par erreur lors d'une rentrée scolaire.
        var existingTeachers = await dbContext.Teachers.AsNoTracking()
            .Select(t => new { t.Email, t.Phone })
            .ToListAsync(cancellationToken);
        var existingEmails = existingTeachers.Select(t => t.Email.Trim().ToLowerInvariant()).ToHashSet();
        var existingPhones = existingTeachers
            .Where(t => t.Phone is not null)
            .Select(t => NormalizePhone(t.Phone!))
            .ToHashSet();

        var seenEmails = new Dictionary<string, int>();
        var seenPhones = new Dictionary<string, int>();

        var results = new List<ImportTeachersRowResult>();
        var parsedRows = new List<ParsedTeacherRow>();

        foreach (var row in fileRows)
        {
            var (fieldErrors, parsed) = ValidateRow(
                row, byNameOnly, byNameAndLevel, existingEmails, existingPhones, seenEmails, seenPhones);

            results.Add(new ImportTeachersRowResult(
                row.RowNumber, fieldErrors.Count == 0,
                row.FullName, row.Email, row.Phone, row.BirthDate, row.BirthPlace, row.Subjects,
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
            // jamais confirmée (voir le commentaire de classe d'ImportTeachersCommand).
            return new ImportTeachersResult(
                DryRun: true, Committed: false, fileRows.Count, results.Count - invalidCount, invalidCount, Created: 0, results);
        }

        if (invalidCount > 0)
        {
            // RE-validation côté confirmation : rejet intégral, RIEN n'est écrit (AGENTS.md règle #5).
            var errors = results
                .Where(r => !r.IsValid)
                .Select(r => new ValidationFailure(
                    $"Ligne {r.RowNumber}", string.Join(" ", r.FieldErrors.Values)));

            throw new ValidationException(errors);
        }

        // Fichier entièrement valide : chaque enseignant est créé dans UNE seule transaction (règle #5)
        // — le matricule de CHAQUE ligne est généré ICI, séquentiellement, DANS la transaction (règle
        // #3) : si l'import échoue en cours de route, tous les compteurs consommés sont rembobinés.
        var created = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var parsed in parsedRows)
            {
                var matricule = await matriculeGenerator.GenerateNextTeacherMatriculeAsync(schoolId, ct);

                var teacher = new Teacher
                {
                    SchoolId = schoolId,
                    Matricule = matricule,
                    FullName = parsed.FullName,
                    Email = parsed.Email,
                    Phone = parsed.Phone,
                    BirthDate = parsed.BirthDate,
                    BirthPlace = parsed.BirthPlace
                };

                dbContext.Teachers.Add(teacher);

                foreach (var subjectId in parsed.SubjectIds)
                {
                    dbContext.TeacherSubjects.Add(new TeacherSubject
                    {
                        SchoolId = schoolId,
                        TeacherId = teacher.Id,
                        SubjectId = subjectId
                    });
                }
            }

            await dbContext.SaveChangesAsync(ct);
            return parsedRows.Count;
        }, cancellationToken);

        return new ImportTeachersResult(
            DryRun: false, Committed: true, fileRows.Count, ValidRows: created, InvalidRows: 0, Created: created, results);
    }

    private static string NormalizePhone(string phone) => phone.Replace(" ", "").Trim();

    /// <summary>
    /// Valide UNE ligne, en accumulant TOUTES ses erreurs par champ (pas seulement la première) : l'écran
    /// d'aperçu doit pouvoir colorer chaque cellule fautive indépendamment des autres. Renvoie la ligne
    /// PARSÉE (prête à insérer) uniquement si elle est entièrement valide.
    /// </summary>
    private static (Dictionary<string, string> Errors, ParsedTeacherRow? Parsed) ValidateRow(
        TeacherImportFileRow row,
        IReadOnlyDictionary<string, List<(Guid Id, string Level)>> byNameOnly,
        IReadOnlyDictionary<(string Name, string Level), Guid> byNameAndLevel,
        IReadOnlySet<string> existingEmails,
        IReadOnlySet<string> existingPhones,
        Dictionary<string, int> seenEmails,
        Dictionary<string, int> seenPhones)
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

        var email = row.Email.Trim();
        if (email.Length == 0)
        {
            errors["email"] = "L'e-mail est obligatoire.";
        }
        else if (email.Length > 255 || !IsValidEmail(email) || !SafeTextValidation.IsSafeText(email))
        {
            errors["email"] = "L'e-mail est invalide.";
        }
        else
        {
            var normalizedEmail = email.ToLowerInvariant();
            if (existingEmails.Contains(normalizedEmail))
            {
                errors["email"] = "Un enseignant avec cet e-mail existe déjà dans votre établissement.";
            }
            else if (seenEmails.TryGetValue(normalizedEmail, out var firstEmailRow))
            {
                errors["email"] = $"Cet e-mail est déjà utilisé à la ligne {firstEmailRow} de ce fichier.";
            }
            else
            {
                seenEmails[normalizedEmail] = row.RowNumber;
            }
        }

        var phone = row.Phone.Trim();
        if (phone.Length == 0)
        {
            errors["phone"] = "Le téléphone est obligatoire.";
        }
        else if (phone.Length > 30 || !SenegalPhoneValidation.IsValidSenegalPhone(phone))
        {
            errors["phone"] = "Le numéro de téléphone doit être un numéro sénégalais valide (ex: 77 123 45 67).";
        }
        else
        {
            var normalizedPhone = NormalizePhone(phone);
            if (existingPhones.Contains(normalizedPhone))
            {
                errors["phone"] = "Un enseignant avec ce téléphone existe déjà dans votre établissement.";
            }
            else if (seenPhones.TryGetValue(normalizedPhone, out var firstPhoneRow))
            {
                errors["phone"] = $"Ce téléphone est déjà utilisé à la ligne {firstPhoneRow} de ce fichier.";
            }
            else
            {
                seenPhones[normalizedPhone] = row.RowNumber;
            }
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
        if (birthPlace.Length > 200)
        {
            errors["birthPlace"] = "Le lieu de naissance ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(birthPlace))
        {
            errors["birthPlace"] = SafeTextValidation.ErrorMessage;
        }

        var (subjectIds, subjectsError) = ResolveSubjects(row.Subjects, byNameOnly, byNameAndLevel);
        if (subjectsError is not null)
        {
            errors["subjects"] = subjectsError;
        }

        if (errors.Count > 0)
        {
            return (errors, null);
        }

        return (errors, new ParsedTeacherRow(
            fullName, email, phone, birthDate, birthPlace.Length == 0 ? null : birthPlace, subjectIds));
    }

    /// <summary>
    /// Découpe la cellule Matières sur la virgule, chaque jeton étant résolu soit par son seul nom (s'il
    /// est sans ambiguïté dans l'établissement), soit par « Nom (Niveau) » quand plusieurs matières
    /// partagent ce nom à des niveaux différents (voir Domain.Entities.Subject : la clé est (Niveau,
    /// Nom), jamais le seul Nom).
    /// </summary>
    private static (List<Guid> SubjectIds, string? Error) ResolveSubjects(
        string rawSubjects,
        IReadOnlyDictionary<string, List<(Guid Id, string Level)>> byNameOnly,
        IReadOnlyDictionary<(string Name, string Level), Guid> byNameAndLevel)
    {
        var tokens = rawSubjects.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return ([], "Au moins une matière est obligatoire.");
        }

        var subjectIds = new List<Guid>();

        foreach (var token in tokens)
        {
            var match = SubjectWithLevelPattern().Match(token);

            if (match.Success)
            {
                var name = match.Groups["name"].Value.Trim();
                var level = match.Groups["level"].Value.Trim();

                if (!byNameAndLevel.TryGetValue((name, level), out var subjectId))
                {
                    return ([], $"Matière « {token} » introuvable : vérifiez le nom et le niveau.");
                }

                if (!subjectIds.Contains(subjectId)) subjectIds.Add(subjectId);
                continue;
            }

            if (!byNameOnly.TryGetValue(token, out var candidates))
            {
                return ([], $"Matière « {token} » introuvable : vérifiez l'orthographe ou créez-la d'abord.");
            }

            if (candidates.Count > 1)
            {
                var levels = string.Join(", ", candidates.Select(c => c.Level));
                return ([], $"Plusieurs matières portent le nom « {token} » ({levels}) : précisez le niveau, ex. « {token} ({candidates[0].Level}) ».");
            }

            var onlyId = candidates[0].Id;
            if (!subjectIds.Contains(onlyId)) subjectIds.Add(onlyId);
        }

        return (subjectIds, null);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            return address.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Comparateur (Nom, Niveau) insensible à la casse — même index que <see cref="byNameOnly"/>
    /// mais tenant compte du niveau pour la résolution explicite « Nom (Niveau) ».</summary>
    private sealed class NameLevelComparer : IEqualityComparer<(string Name, string Level)>
    {
        public static readonly NameLevelComparer Instance = new();

        public bool Equals((string Name, string Level) x, (string Name, string Level) y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Level, y.Level, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, string Level) obj) =>
            HashCode.Combine(
                obj.Name.ToUpperInvariant(),
                obj.Level.ToUpperInvariant());
    }
}
