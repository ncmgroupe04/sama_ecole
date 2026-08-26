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
