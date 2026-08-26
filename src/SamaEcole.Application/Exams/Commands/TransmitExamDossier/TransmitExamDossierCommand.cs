using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.TransmitExamDossier;

/// <summary>
/// POST /api/v1/exams/dossiers/{id}/transmit — marque un dossier transmis à l'IEF/l'IA. Refusé si
/// le dossier est encore <c>Incomplet</c> (Volume 1 §22.3) : c'est l'audit qui fait foi, pas une
/// case cochée manuellement.
/// </summary>
public record TransmitExamDossierCommand(Guid Id) : IRequest<ExamDossierResult>;
