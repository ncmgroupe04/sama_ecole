namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Refus d'autorisation DONT LE MESSAGE EST DESTINÉ À L'ÉCRAN — typiquement un contrôle de portée qui
/// ne peut se faire qu'au moment de l'exécution : un Enseignant qui demande l'appel d'une classe non
/// assignée, la fiche d'examen d'un élève hors de ses classes, ou un créneau d'emploi du temps qui
/// n'est pas le sien. Traduite en HTTP 403 <c>FORBIDDEN</c> par <c>ExceptionHandlingMiddleware</c>,
/// qui renvoie <see cref="System.Exception.Message"/> tel quel — à la différence d'un
/// <see cref="UnauthorizedAccessException"/> nu, réservé aux gardes internes « ne devrait jamais
/// arriver » (« Tenant is required. »…) et aplati en un « Accès refusé. » générique.
///
/// <para>
/// Le message doit indiquer l'ACTION CORRECTIVE (« demandez au Directeur de faire le rattachement »,
/// « vous n'êtes pas assigné à cette classe pour cette matière ») plutôt qu'un 403 muet —
/// docs/Volume_4_API_Design.md §22. Il ne doit jamais nommer une table, un index ou une contrainte,
/// ni permettre d'énumérer des ressources (docs/Volume_7_Security.md).
/// </para>
///
/// <para>
/// Sous-classe d'<see cref="UnauthorizedAccessException"/> À DESSEIN : les tests d'intégration des
/// autorisations de portée attendent <c>ThrowAsync&lt;UnauthorizedAccessException&gt;</c>, et le
/// mapping HTTP existant (403) reste valable même si un appelant l'attrape sous le type de base.
/// </para>
/// </summary>
public class ForbiddenException(string message) : UnauthorizedAccessException(message);
