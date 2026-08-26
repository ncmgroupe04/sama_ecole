using SamaEcole.Application.Exams;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Fiche(s) de candidature (PDF, une page par candidat) — unitaire ou par lot selon la taille de
/// <paramref name="candidates"/> transmise à <see cref="Generate"/> (Volume 1 §22.5).
/// </summary>
public interface IExamCandidateFormPdfGenerator
{
    byte[] Generate(IReadOnlyList<ExamCandidateFormModel> candidates, byte[]? logo);
}
