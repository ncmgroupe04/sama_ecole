using MediatR;

namespace SamaEcole.Application.Students.Commands.DeleteStudent;

/// <summary>
/// DELETE /api/v1/students/{id} — archive (soft delete) une fiche élève créée par pure erreur de
/// saisie. Toujours un soft delete (AGENTS.md règle #6). Réservé au Directeur et au Secrétariat.
///
/// Le Handler DOIT rejeter la suppression si un historique CRITIQUE est déjà attaché à l'élève : une
/// inscription (même annulée — le reçu porte un numéro officiel gapless, AGENTS.md règle #3) ou une
/// note. Sans inscription ni note, la fiche n'a encore produit aucun effet comptable ou pédagogique :
/// c'est exactement le cas d'une « erreur visuelle » que cette commande cible.
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateStudentCommand (règle #5).
/// </summary>
public record DeleteStudentCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
