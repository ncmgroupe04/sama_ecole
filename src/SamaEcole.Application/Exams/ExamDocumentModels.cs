namespace SamaEcole.Application.Exams;

/// <summary>
/// Modèles partagés par les documents PDF du module Examens (fiche de candidature, convocation) —
/// même regroupement que les autres modules à plusieurs pièces (voir Inventory.Common).
/// </summary>
public record ExamCandidateFormModel(
    string Reference,
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? SchoolAddress,
    string StudentFullName,
    string StudentMatricule,
    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string ExamType,
    string? Series,
    string? CandidateNumber,
    string? ExamCenterName,
    string? BirthCertificateNumber,
    bool BirthCertificatePresent);

public record ExamConvocationModel(
    string Reference,
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? SchoolCity,
    DateTimeOffset IssuedAt,
    string StudentFullName,
    string StudentMatricule,
    string ClassroomName,
    string ExamType,
    string? Series,
    string CandidateNumber,
    string ExamCenterName);

/// <summary>
/// Un candidat sur le PV de session ou la fiche synthétique (module Franco-Arabe, bloc 4). Les moyennes sont celles
/// de l'ANNÉE, sur le barème du cycle de la classe (<see cref="GradingScale"/>). <see cref="FrenchAverage"/> et
/// <see cref="ArabicAverage"/> ne sont renseignées que si le module Coran/Franco-Arabe est activé et que le candidat
/// a des notes dans la section ; <see cref="IenNumber"/> est repris tel quel de la fiche élève — null s'il n'existe
/// pas, jamais un numéro inventé.
/// </summary>
public record ExamSynthesisCandidate(
    string Reference,
    string StudentFullName,
    string? StudentFullNameAr,
    string Matricule,
    string? IenNumber,
    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string? CandidateNumber,
    string? TableNumber,
    string? ExamCenterName,
    string? ExamCenterCode,
    Domain.Enums.ExamDossierStatus DossierStatus,
    int GradingScale,
    decimal? FrenchAverage,
    decimal? ArabicAverage,
    decimal? CombinedAverage,
    bool? IsAdmitted,
    Domain.Enums.ExamMention? Mention,
    decimal? ExamAverage,
    DateOnly? DeliberatedOn);

/// <summary>Un PV de session (tous les candidats) ou une fiche synthétique (un seul) — même modèle, voir ExamSynthesisBuilder.</summary>
public record ExamSynthesisModel(
    string Reference,
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    Domain.Enums.ExamType ExamType,
    string? Series,
    string SchoolYearLabel,
    bool IsBilingualArabic,
    IReadOnlyList<ExamSynthesisCandidate> Candidates);
