using SamaEcole.Application.Absences.Queries.GetEntryTicket;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un billet d'entrée en classe (billet A5) en PDF, remis par la Surveillance à un élève arrivé
/// en retard pour l'autoriser à rejoindre son cours. Comme les autres documents officiels, la mise en
/// page est une préoccupation d'infrastructure (SamaEcole.Infrastructure, QuestPDF) ; la donnée
/// (<see cref="EntryTicketDto"/>) vient de l'Application. Le document est déterministe : mêmes données
/// figées ⇒ même billet, réimpression comprise.
///
/// <paramref name="logo"/> porte les octets déjà récupérés et validés du logo de l'établissement (via
/// <c>ISchoolLogoProvider</c>), ou <c>null</c> si l'école n'en a pas / s'il est injoignable : dans ce
/// cas l'en-tête retombe sur l'emplacement réservé. La récupération réseau reste hors du générateur,
/// qui demeure une fonction pure et synchrone de ses entrées.
/// </summary>
public interface IEntryTicketPdfGenerator
{
    byte[] Generate(EntryTicketDto ticket, byte[]? logo);
}
