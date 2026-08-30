using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Sérialise la matrice « Planète Ready » en JSON ou en CSV (Volume 1 §23.2).
///
/// UNE SEULE définition des colonnes (<see cref="Columns"/>), partagée par les deux formats : c'est
/// elle qui garantit qu'un CSV et un JSON du même export portent les mêmes champs dans le même ordre.
/// Ajouter une colonne au CSV sans l'ajouter au JSON deviendrait impossible sans le remarquer.
/// </summary>
public class PlaneteExportSerializer : IPlaneteExportSerializer
{
    /// <summary>
    /// Point-virgule, pas virgule. Les fichiers de l'administration sénégalaise sont ouverts dans un
    /// Excel en locale française, où la virgule est le séparateur DÉCIMAL : un CSV à virgules y arrive
    /// tout entier dans la première colonne.
    /// </summary>
    private const char Delimiter = ';';

    /// <summary>
    /// Dates en ISO 8601 dans les DEUX formats. Le format sénégalais « jj/mm/aaaa » serait relu par un
    /// importeur anglophone comme « mm/jj/aaaa » : le 03/07 deviendrait le 7 mars. Une date d'échange
    /// inter-systèmes ne se met pas en forme pour l'œil humain.
    /// </summary>
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Sans cet encodeur, System.Text.Json échapperait « Ndèye » en « Ndèye » : lisible par
        // une machine, illisible par l'agent de l'IEF qui ouvre le fichier pour le contrôler.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// L'ordre et les libellés des colonnes, source de vérité unique. Le libellé est celui attendu par
    /// l'importeur du ministère — il n'est PAS traduit ni « amélioré » : le fichier est rejeté si un
    /// en-tête diffère d'un caractère.
    /// </summary>
    private static readonly (string Header, Func<PlaneteStudentSyncDto, string?> Value)[] Columns =
    [
        ("IEN", s => s.IenNumber),
        ("IEN_PROVISOIRE", s => s.IsIenProvisional ? "OUI" : "NON"),
        ("MATRICULE", s => s.Matricule),
        ("NOM", s => s.LastName),
        ("PRENOMS", s => s.FirstNames),
        ("DATE_NAISSANCE", s => s.BirthDate.ToString(DateFormat, CultureInfo.InvariantCulture)),
        ("LIEU_NAISSANCE", s => s.BirthPlace),
        ("SEXE", s => s.Gender),
        ("NIVEAU", s => s.Level),
        ("CLASSE", s => s.ClassroomName),
        ("CYCLE", s => s.Cycle),
        ("REDOUBLANT", s => s.IsRepeating ? "OUI" : "NON"),
        ("ANNEE_SCOLAIRE", s => s.SchoolYearLabel),
        ("TUTEUR", s => s.GuardianName),
        ("TELEPHONE_TUTEUR", s => s.GuardianPhone),
        ("CODE_ETABLISSEMENT", s => s.NationalSchoolCode)
    ];

    public StateExportFile Serialize(PlaneteExportDto export, StateExportFormat format) => format switch
    {
        StateExportFormat.Json => SerializeJson(export),
        StateExportFormat.Csv => SerializeCsv(export),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Format d'export inconnu.")
    };

    private static StateExportFile SerializeJson(PlaneteExportDto export)
    {
        // Enveloppe explicite : l'en-tête d'établissement et les compteurs de qualité accompagnent les
        // lignes. Sérialiser un simple tableau d'élèves ferait perdre l'origine du lot, et le fichier
        // serait rejeté — voir PlaneteExportDto.
        var payload = new
        {
            codeEtablissement = export.NationalSchoolCode,
            nomEtablissement = export.SchoolName,
            inspectionAcademie = export.InspectionAcademie,
            inspectionEducationFormation = export.InspectionEducationFormation,
            codeCirconscription = export.SchoolDistrictCode,
            anneeScolaire = export.SchoolYearLabel,
            genereLe = export.GeneratedAt.ToString("O", CultureInfo.InvariantCulture),
            effectif = export.StudentCount,

            // Compteurs de QUALITÉ transmis avec le lot, délibérément : le destinataire doit pouvoir
            // décider quoi faire des lignes provisoires ou sans IEN sans avoir à les recompter.
            effectifIenProvisoire = export.ProvisionalIenCount,
            effectifSansIen = export.MissingIenCount,

            eleves = export.Students.Select(s => Columns.ToDictionary(c => c.Header, c => c.Value(s)))
        };

        return new StateExportFile(
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions),
            BuildFileName(export, "json"),
            StateExportFormat.Json);
    }

    private static StateExportFile SerializeCsv(PlaneteExportDto export)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(Delimiter, Columns.Select(c => c.Header)));

        foreach (var student in export.Students)
        {
            builder.AppendLine(string.Join(Delimiter, Columns.Select(c => Escape(c.Value(student)))));
        }

        // BOM UTF-8 explicite : sans lui, Excel en locale française lit le fichier en Windows-1252 et
        // affiche « Ndèye Fatou » en « NdÃ¨ye Fatou ». Le fichier serait techniquement correct et
        // inutilisable — l'agent le corrigerait à la main, ligne par ligne.
        //
        // Encoding.GetBytes N'ÉMET PAS le BOM (seul un StreamWriter le ferait) : on préfixe donc
        // explicitement le préambule aux octets encodés.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(builder.ToString());
        var content = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);

        return new StateExportFile(content, BuildFileName(export, "csv"), StateExportFormat.Csv);
    }

    /// <summary>
    /// Échappement RFC 4180 : une valeur contenant le séparateur, un guillemet ou un saut de ligne est
    /// entourée de guillemets, ses guillemets internes étant doublés.
    ///
    /// Une valeur NULL devient une cellule VIDE, jamais « null » ni « - » : l'absence de donnée doit
    /// rester une absence à l'arrivée. Écrire un tiret ferait importer la chaîne « - » comme lieu de
    /// naissance.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var needsQuoting = value.Contains(Delimiter)
                           || value.Contains('"')
                           || value.Contains('\n')
                           || value.Contains('\r');

        return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>
    /// Nom de fichier auto-descriptif : code établissement, année, horodatage. L'agent de l'IEF reçoit
    /// des dizaines de fichiers par jour — « export.csv » y est indistinguable des autres.
    /// L'année scolaire porte un « / » (« 2026/2027 ») qui n'est pas un caractère de nom de fichier.
    /// </summary>
    private static string BuildFileName(PlaneteExportDto export, string extension)
    {
        var year = export.SchoolYearLabel.Replace('/', '-').Replace(' ', '-');
        var stamp = export.GeneratedAt.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

        return $"Planete-{export.NationalSchoolCode}-{year}-{stamp}.{extension}";
    }
}
