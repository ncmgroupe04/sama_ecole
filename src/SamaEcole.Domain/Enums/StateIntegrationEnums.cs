namespace SamaEcole.Domain.Enums;

/// <summary>
/// Diplôme ACADÉMIQUE de l'enseignant — le niveau d'études générales. À ne jamais confondre avec
/// <see cref="ProfessionalQualification"/>, le diplôme PÉDAGOGIQUE : le formulaire STATEDUC les compte
/// dans deux colonnes distinctes, et un titulaire d'un Master sans CAES n'est pas « qualifié » au sens
/// de la statistique ministérielle (Volume 1 §23.3).
///
/// <c>NonRenseigne</c> est la valeur par défaut et signifie « l'école n'a pas encore saisi » — jamais
/// « aucun diplôme », qui se dit <c>Aucun</c>. Cette distinction est ce qui permet au rapport STATEDUC
/// d'imprimer un effectif « non renseigné » plutôt que de gonfler la case « sans diplôme ».
/// </summary>
public enum AcademicQualification
{
    NonRenseigne,
    Aucun,
    BFEM,
    BAC,
    Licence,
    Master,
    Doctorat
}

/// <summary>
/// Diplôme PROFESSIONNEL (pédagogique) de l'enseignant, nomenclature sénégalaise. C'est ce diplôme,
/// et lui seul, qui rend un enseignant « qualifié » dans les agrégats STATEDUC.
///
/// CEAP : Certificat Élémentaire d'Aptitude Pédagogique — élémentaire.
/// CAP  : Certificat d'Aptitude Pédagogique — élémentaire, niveau supérieur au CEAP.
/// CAEM : Certificat d'Aptitude à l'Enseignement Moyen — collège.
/// CAES : Certificat d'Aptitude à l'Enseignement Secondaire — lycée.
/// </summary>
public enum ProfessionalQualification
{
    NonRenseigne,
    Aucun,
    CEAP,
    CAP,
    CAEM,
    CAES
}

/// <summary>
/// Statut administratif de l'enseignant — colonne obligatoire du formulaire STATEDUC, qui distingue
/// les personnels payés par l'État de ceux payés par l'établissement. Sans rapport avec
/// <c>EmployeeContract</c> (module Paie), qui décrit le mode de RÉMUNÉRATION interne : un contractuel
/// de l'État peut être mensualisé, un vacataire de l'école aussi.
/// </summary>
public enum TeacherCivilServiceStatus
{
    NonRenseigne,
    Fonctionnaire,
    Contractuel,
    Vacataire,
    Volontaire,
    Benevole
}

/// <summary>
/// État du document d'état civil au dossier d'examen (Volume 1 §23.4). Remplace fonctionnellement le
/// couple <c>BirthCertificatePresent</c> (bool) + <c>CivilStatusConforming</c> (bool?) de
/// <see cref="Entities.ExamDossier"/>, qui ne savait pas exprimer la situation la plus fréquente au
/// Sénégal : un extrait fourni, non conforme, et DÉJÀ en cours de régularisation au tribunal.
///
/// Les deux anciens champs sont conservés — ils sont écrits par des Handlers existants et lus par
/// l'audit de dossier ; ce statut les complète, il ne les remplace pas silencieusement.
/// <c>NonFourni</c> est le défaut : un dossier qui vient d'être ouvert n'a rien reçu.
/// </summary>
public enum CivilRegistryDocumentStatus
{
    NonFourni,
    Fourni,
    Conforme,
    NonConforme,
    EnRegularisation
}

/// <summary>
/// Format de sortie de l'export « Planète Ready » (Volume 1 §23.2). Deux formats, un seul jeu de
/// colonnes : la matrice est construite une fois, puis sérialisée — un CSV et un JSON du même export
/// portent exactement les mêmes champs, dans le même ordre, sans quoi le rapprochement manuel que
/// l'IEF fait entre les deux deviendrait faux.
/// </summary>
public enum StateExportFormat
{
    Json,
    Csv
}

/// <summary>
/// État de transmission d'un lot vers le SIMEN. Tant que le relais API n'est pas ouvert (voir
/// <c>ISimenBridgeService</c>), seul <c>NonTransmis</c> est atteignable : aucune valeur n'est jamais
/// écrite « en avance » pour simuler une transmission qui n'a pas eu lieu.
/// </summary>
public enum SimenSyncStatus
{
    NonTransmis,
    EnAttente,
    Transmis,
    Rejete
}

/// <summary>Motif de mutation porté par le certificat (Volume 1 §23.5).</summary>
public enum StudentMutationReason
{
    Demenagement,
    ChangementEtablissement,
    RaisonFamiliale,
    RaisonMedicale,
    Autre
}
