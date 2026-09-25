namespace SamaEcole.Domain.Enums;

/// <summary>
/// Cycle de vie d'un billet d'entrée visant un cours précis (Évolution N°5). <c>Issued</c> : émis par la
/// Vie Scolaire, en attente d'acceptation par l'enseignant du cours. <c>Accepted</c> : l'enseignant a
/// accepté l'élève en classe — définitif. <c>Cancelled</c> : annulé par la Vie Scolaire avant acceptation.
///
/// Persisté en TEXTE (jamais un entier qui se briserait si l'ordre changeait). Le statut d'un billet est
/// NULLABLE sur <c>LateArrival</c> : null = billet sans cours visé, c'est-à-dire tout billet antérieur à
/// l'évolution — aucune migration de données.
/// </summary>
public enum EntryTicketStatus
{
    Issued,
    Accepted,
    Cancelled
}
