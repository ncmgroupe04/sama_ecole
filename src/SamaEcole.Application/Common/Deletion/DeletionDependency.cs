using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Common.Deletion;

/// <summary>
/// Un type de lien d'un élément à supprimer (élève, classe, matière, enseignant…) : comment le compter, comment le
/// rompre (archivage logique), comment le nommer. <paramref name="Breakable"/> faux : le lien bloque la suppression et
/// ne se rompt pas en un clic (ex. élèves d'une classe, matériel prêté) — <paramref name="BreakAction"/> dit alors
/// à l'utilisateur quoi faire avant.
/// </summary>
public sealed record DeletionDependency(
    string Kind,
    string Singular,
    string Plural,
    string BreakAction,
    bool Breakable,
    Type EntityType,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<int>> CountAsync,
    Func<IApplicationDbContext, Guid, string, CancellationToken, Task<int>> BreakAsync);
