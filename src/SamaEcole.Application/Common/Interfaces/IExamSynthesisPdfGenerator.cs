using SamaEcole.Application.Exams;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Documents de synthèse d'examen officiel (module Franco-Arabe, bloc 4) : le PV de la session (un tableau, tous
/// les candidats) et la fiche synthétique d'un candidat (une page). Bilingues français/arabe quand
/// <see cref="ExamSynthesisModel.IsBilingualArabic"/>.
/// </summary>
public interface IExamSynthesisPdfGenerator
{
    byte[] GenerateSessionPv(ExamSynthesisModel model, byte[]? logo);

    byte[] GenerateCandidateSheet(ExamSynthesisModel model, byte[]? logo);
}
