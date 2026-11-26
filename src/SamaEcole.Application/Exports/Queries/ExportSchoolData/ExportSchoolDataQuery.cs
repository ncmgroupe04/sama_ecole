using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Exports.Queries.ExportSchoolData;

/// <summary>
/// POST /api/v1/exports/school-data — « Exporter mes données » : archive ZIP de toutes les données de l'établissement courant.
/// Réservé au Directeur (contrôleur). C'est une LECTURE (Query, règle #7) mais sensible : toute la base d'une école, données
/// personnelles d'élèves comprises. Trois garde-fous :
///   * confirmation par MOT DE PASSE du Directeur (poste laissé ouvert, jeton dérobé), 422 sur le champ si refusée ;
///   * journal d'audit (<see cref="IAuditableRequest"/>) : qui a exporté, quand, succès ou échec — jamais le mot de passe ;
///   * aucun secret dans l'archive (<see cref="SensitiveColumnRules"/>).
/// </summary>
public record ExportSchoolDataQuery(string Password) : IRequest<SchoolDataExportResult>, IAuditableRequest;

/// <summary>
/// Archive prête à être servie : flux sur un fichier temporaire supprimé à la fermeture (<c>FileOptions.DeleteOnClose</c>),
/// positionné au début. Le consommateur le ferme (ASP.NET Core le fait après l'envoi).
/// </summary>
public sealed record SchoolDataExportResult(Stream Content, string FileName, int TableCount, int RowCount) : IAsyncDisposable, IDisposable
{
    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public class ExportSchoolDataQueryValidator : AbstractValidator<ExportSchoolDataQuery>
{
    public ExportSchoolDataQueryValidator() => RuleFor(x => x.Password).NotEmpty().WithMessage("Saisissez votre mot de passe pour confirmer.");
}

public class ExportSchoolDataQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    ISchoolDataExporter exporter,
    ILogger<ExportSchoolDataQueryHandler> logger)
    : IRequestHandler<ExportSchoolDataQuery, SchoolDataExportResult>
{
    public async Task<SchoolDataExportResult> Handle(ExportSchoolDataQuery request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        await ConfirmPasswordAsync(actorId, request.Password, cancellationToken);

        // Fichier temporaire plutôt que mémoire : une archive d'établissement peut peser plusieurs centaines de Mo, et ZipArchive
        // écrit en synchrone, ce que Kestrel interdit sur le corps de la réponse.
        var path = Path.Combine(Path.GetTempPath(), $"sama-export-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
            var manifest = await exporter.ExportAsync(stream, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);

            logger.LogInformation(
                "Export global de l'établissement « {School} » par l'utilisateur {ActorId} : {Tables} tables, {Rows} lignes.",
                manifest.SchoolName, actorId, manifest.Tables.Count(t => t.RowCount > 0), manifest.TotalRows);

            return new SchoolDataExportResult(
                stream, FileNameFor(manifest), manifest.Tables.Count(t => t.RowCount > 0), manifest.TotalRows);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task ConfirmPasswordAsync(Guid actorId, string password, CancellationToken cancellationToken)
    {
        var passwordHash = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Utilisateur courant introuvable.");

        if (passwordHasher.Verify(passwordHash, password))
        {
            return;
        }

        logger.LogWarning("Confirmation par mot de passe REFUSÉE pour l'export global. Utilisateur {ActorId}.", actorId);

        // 422 sur le champ, pas 401 : la session est valide, c'est la confirmation qui échoue (même convention que l'activation d'année).
        throw new SamaEcole.Application.Common.Exceptions.ValidationException([new ValidationFailure(nameof(ExportSchoolDataQuery.Password), "Mot de passe incorrect.")]);
    }

    /// <summary>« export-nom-de-l-ecole-2026-11-15.zip » : ASCII seul, sans caractère interdit dans un nom de fichier.</summary>
    private static string FileNameFor(SchoolDataExportManifest manifest)
    {
        var slug = new string(manifest.SchoolName.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "-{2,}", "-").Trim('-');
        if (slug.Length == 0) slug = "ecole";
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return $"export-{slug}-{manifest.GeneratedAtUtc:yyyy-MM-dd}.zip";
    }
}
