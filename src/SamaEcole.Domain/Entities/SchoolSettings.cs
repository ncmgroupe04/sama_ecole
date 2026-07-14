using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Paramètres d'un établissement (ticket JGK-B02, openapi.yaml §SchoolSettings).
///
/// Table tenant à part entière (SchoolId + RLS + Global Query Filter), et non des colonnes de
/// `schools` : `schools` échappe volontairement à la RLS puisqu'elle DÉFINIT le tenant. Y loger les
/// paramètres les priverait de toute protection en base, alors qu'ils pilotent des règles métier —
/// à commencer par le format des matricules.
/// </summary>
public class SchoolSettings : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Barème de notation : 10 ou 20 (docs/Volume_1_Cahier_des_Charges.md).</summary>
    public int GradingScale { get; set; } = SchoolSettingsDefaults.GradingScale;

    /// <summary>Ex. « ELEV-{YEAR}-{SEQ:4} » — voir MatriculeFormat.</summary>
    public string StudentMatriculeFormat { get; set; } = SchoolSettingsDefaults.StudentMatriculeFormat;

    public string TeacherMatriculeFormat { get; set; } = SchoolSettingsDefaults.TeacherMatriculeFormat;

    /// <summary>Déconnexion automatique par inactivité (ticket JGK-A06, gelé — la valeur est déjà stockée).</summary>
    public int AutoLogoutMinutes { get; set; } = SchoolSettingsDefaults.AutoLogoutMinutes;

    public string DateFormat { get; set; } = SchoolSettingsDefaults.DateFormat;
}

/// <summary>
/// Valeurs par défaut d'un établissement neuf (critère du ticket JGK-B02 : « les valeurs par défaut
/// sont appliquées à la création »). Elles sont AUSSI la source de vérité de la fonction
/// provision_school_director : si l'une change ici, changer la migration correspondante.
/// </summary>
public static class SchoolSettingsDefaults
{
    public const int GradingScale = 20;
    public const string StudentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}";
    public const string TeacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}";
    public const int AutoLogoutMinutes = 10;
    public const string DateFormat = "dd/MM/yyyy";

    public static readonly int[] AllowedGradingScales = [10, 20];
    public static readonly string[] AllowedDateFormats = ["dd/MM/yyyy", "dd MMMM yyyy"];
}
