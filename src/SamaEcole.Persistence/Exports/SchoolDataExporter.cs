using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exports;
using SamaEcole.Domain.Common;

namespace SamaEcole.Persistence.Exports;

/// <summary>
/// Archive « Exporter mes données » construite depuis le MODÈLE EF : une entité de plus dans le modèle est exportée sans toucher
/// ce fichier — c'est ce qui garantit que l'export reste « toutes les données » dans la durée.
///
/// Isolation : chaque lecture passe par le DbContext de la requête, donc par le Global Query Filter (SchoolId + non supprimé)
/// ET la policy RLS du rôle applicatif. Aucune requête n'ignore les filtres : l'archive ne peut contenir que l'école courante.
/// Les lignes supprimées logiquement ne sont pas exportées. Les secrets sont exclus (<see cref="SensitiveColumnRules"/>).
///
/// Format : un CSV UTF-8 (avec BOM, pour Excel) par table, séparateur « ; », guillemets doublés, nombres au format invariant
/// (point décimal), dates ISO 8601, énumérations en toutes lettres. Seules les tables non vides produisent un fichier.
/// </summary>
public sealed class SchoolDataExporter(
    ApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider) : ISchoolDataExporter
{
    private const char Separator = ';';

    private Guid _schoolId;

    private static readonly MethodInfo ExportEntityMethod =
        typeof(SchoolDataExporter).GetMethod(nameof(ExportEntityAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public async Task<SchoolDataExportManifest> ExportAsync(Stream destination, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        _schoolId = schoolId;

        var schoolName = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "Établissement";

        var generatedAt = timeProvider.GetUtcNow();
        var tables = new List<SchoolDataExportTable>();

        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entityType in ExportableEntityTypes())
            {
                var tableName = entityType.GetTableName()!;
                var columns = ExportedProperties(entityType);

                var task = (Task<int>)ExportEntityMethod.MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [zip, $"donnees/{tableName}.csv", columns, cancellationToken])!;
                var rows = await task;

                tables.Add(new SchoolDataExportTable(tableName, rows == 0 ? null : $"donnees/{tableName}.csv", rows, columns.Count));
            }

            var manifest = new SchoolDataExportManifest(schoolName, generatedAt, tables);
            WriteTextEntry(zip, "manifest.json", BuildManifestJson(manifest), withBom: false);
            WriteTextEntry(zip, "LISEZMOI.txt", BuildReadme(manifest));
            return manifest;
        }
    }

    /// <summary>
    /// Tables d'établissement (ITenantEntity), plus la fiche de l'établissement lui-même et les deux tables de comptes (utilisateurs
    /// et rattachements multi-établissements), qui ne sont PAS filtrées globalement (l'annuaire public lit les écoles, le chemin de
    /// connexion lit les comptes hors tenant) : elles sont donc bornées EXPLICITEMENT à l'école courante dans <see cref="Query{TEntity}"/>.
    /// </summary>
    private static bool IsExportedType(Type clrType) =>
        typeof(ITenantEntity).IsAssignableFrom(clrType)
        || clrType == typeof(Domain.Entities.School)
        || clrType == typeof(Domain.Entities.User)
        || clrType == typeof(Domain.Entities.UserSchool);

    /// <summary>Source d'une table, bornée à l'école courante (Global Query Filter pour les tables tenant, filtre explicite pour l'école et les comptes).</summary>
    private IQueryable<TEntity> Query<TEntity>() where TEntity : class
    {
        if (typeof(TEntity) == typeof(Domain.Entities.School))
        {
            // La table des établissements est lisible par l'annuaire public : aucun filtre global ne la borne. Sans ce filtre explicite,
            // l'archive contiendrait TOUTES les écoles de la plateforme.
            return (IQueryable<TEntity>)(object)dbContext.Schools.Where(s => s.Id == _schoolId && !s.IsDeleted);
        }

        if (typeof(TEntity) == typeof(Domain.Entities.User))
        {
            return (IQueryable<TEntity>)(object)dbContext.Users.Where(u => u.SchoolId == _schoolId && !u.IsDeleted);
        }

        if (typeof(TEntity) == typeof(Domain.Entities.UserSchool))
        {
            return (IQueryable<TEntity>)(object)dbContext.UserSchools.Where(u => u.SchoolId == _schoolId && !u.IsDeleted);
        }

        return dbContext.Set<TEntity>();
    }

    /// <summary>Toutes les tables exportables, par ordre alphabétique.</summary>
    private IEnumerable<IEntityType> ExportableEntityTypes() =>
        dbContext.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.GetTableName() is not null && t.BaseType is null)
            .Where(t => IsExportedType(t.ClrType))
            .Where(t => !SensitiveColumnRules.ExcludedTables.Contains(t.GetTableName()!))
            .OrderBy(t => t.GetTableName(), StringComparer.Ordinal);

    /// <summary>Colonnes exportées : propriétés CLR scalaires, hors secrets et hors jetons de concurrence.</summary>
    private static List<IProperty> ExportedProperties(IEntityType entityType) =>
        entityType.GetProperties()
            .Where(p => p.PropertyInfo is not null && !p.IsShadowProperty())
            .Where(p => !SensitiveColumnRules.ExcludedColumns.Contains(p.Name))
            .Where(p => p.ClrType != typeof(byte[]))
            .Where(p => !p.IsConcurrencyToken)
            .ToList();

    /// <summary>Public pour le test qui interdit toute colonne au nom suspect non revue.</summary>
    public static IEnumerable<(string Table, string Column)> ExportedColumns(DbContext context) =>
        context.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.GetTableName() is not null && t.BaseType is null)
            .Where(t => IsExportedType(t.ClrType))
            .Where(t => !SensitiveColumnRules.ExcludedTables.Contains(t.GetTableName()!))
            .SelectMany(t => ExportedProperties(t).Select(p => (t.GetTableName()!, p.GetColumnName())));

    /// <summary>
    /// Écrit une table dans l'archive. L'entrée n'est créée QUE si la table a des lignes : un ZipArchive en création ne sait pas retirer
    /// une entrée déjà ouverte, et un CSV d'en-tête seul n'apporte rien.
    /// </summary>
    private async Task<int> ExportEntityAsync<TEntity>(
        ZipArchive zip, string entryName, List<IProperty> columns, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (!await Query<TEntity>().AsNoTracking().AnyAsync(cancellationToken))
        {
            return 0;
        }

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(string.Join(Separator, columns.Select(c => Escape(c.GetColumnName()))));

        var rows = 0;
        await foreach (var entity in Query<TEntity>().AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var line = new StringBuilder();
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) line.Append(Separator);
                line.Append(Escape(Format(columns[i].PropertyInfo!.GetValue(entity))));
            }

            await writer.WriteLineAsync(line.ToString());
            rows++;
        }

        return rows;
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable e when value is not string => string.Join("|", e.Cast<object?>().Select(Format)),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// CSV : guillemets doublés si la valeur contient un séparateur, un guillemet ou un saut de ligne. Neutralise en plus l'injection de
    /// formule (une cellule commençant par = + - @ serait exécutée par Excel) en la préfixant d'une apostrophe.
    /// </summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' || (value.Length > 1 && value[0] == '-' && !char.IsDigit(value[1])))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([Separator, '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    /// <summary>BOM pour les fichiers que l'utilisateur ouvre dans Excel/Bloc-notes (CSV, LISEZMOI) ; JAMAIS pour le JSON, que les lecteurs standard refusent.</summary>
    private static void WriteTextEntry(ZipArchive zip, string name, string content, bool withBom = true)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        writer.Write(content);
    }

    private static string BuildManifestJson(SchoolDataExportManifest manifest) =>
        JsonSerializer.Serialize(
            new
            {
                etablissement = manifest.SchoolName,
                genereLe = manifest.GeneratedAtUtc,
                lignesTotal = manifest.TotalRows,
                tables = manifest.Tables.Select(t => new { table = t.Name, fichier = t.File, lignes = t.RowCount, colonnes = t.ColumnCount })
            },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static string BuildReadme(SchoolDataExportManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"EXPORT DES DONNÉES — {manifest.SchoolName}");
        sb.AppendLine($"Généré le {manifest.GeneratedAtUtc.UtcDateTime:dd/MM/yyyy à HH:mm} (UTC).");
        sb.AppendLine();
        sb.AppendLine("Contenu : un fichier CSV par table dans le dossier « donnees », plus manifest.json (liste des tables et nombre de lignes).");
        sb.AppendLine();
        sb.AppendLine("Format des fichiers CSV :");
        sb.AppendLine(" - encodage UTF-8, séparateur point-virgule ( ; ), première ligne = noms des colonnes ;");
        sb.AppendLine(" - nombres avec point décimal, dates au format AAAA-MM-JJ, horodatages ISO 8601 (UTC) ;");
        sb.AppendLine(" - les liens entre fichiers se font par les identifiants (colonnes Id et ...Id) ;");
        sb.AppendLine(" - une valeur commençant par = + - @ est précédée d'une apostrophe : c'est volontaire (protection contre l'exécution de formules).");
        sb.AppendLine("Pour ouvrir un fichier dans Excel : Données > À partir d'un fichier texte/CSV, délimiteur « point-virgule », encodage UTF-8.");
        sb.AppendLine();
        sb.AppendLine("Ce qui n'est PAS dans l'archive :");
        sb.AppendLine(" - les mots de passe et jetons d'accès (jamais exportés) ;");
        sb.AppendLine(" - les éléments supprimés (ils restent dans la corbeille de l'application) ;");
        sb.AppendLine(" - les fichiers binaires (photos, logos, signatures) : seuls leurs adresses figurent dans les tables ;");
        sb.AppendLine(" - les données des autres établissements et de la plateforme.");
        sb.AppendLine();
        sb.AppendLine("Tables exportées (lignes) :");
        foreach (var t in manifest.Tables.Where(t => t.RowCount > 0))
        {
            sb.AppendLine($" - {t.Name} : {t.RowCount}");
        }

        return sb.ToString();
    }
}
