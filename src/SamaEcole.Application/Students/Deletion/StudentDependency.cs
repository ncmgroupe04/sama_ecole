using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Un type de lien d'un élève : comment le compter, comment le rompre (archivage logique), comment le nommer.
/// <paramref name="Breakable"/> faux : le lien bloque la suppression et ne se rompt pas en un clic (ex. matériel prêté).
/// </summary>
public sealed record StudentDependency(
    string Kind,
    string Singular,
    string Plural,
    string BreakAction,
    bool Breakable,
    Type EntityType,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<int>> CountAsync,
    Func<IApplicationDbContext, Guid, string, CancellationToken, Task<int>> BreakAsync);
