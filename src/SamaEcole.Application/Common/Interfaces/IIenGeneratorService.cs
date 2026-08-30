namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère un Identifiant National de l'Élève (IEN) de SECOURS — Volume 1 §23.1, ticket JGK-M01.
///
/// ⚠ MISE EN GARDE, à lire avant tout appel. L'IEN est délivré par le SIMEN (ministère). Nous ne
/// sommes PAS habilités à en créer. Ce que produit ce service est un identifiant PROVISOIRE, marqué
/// comme tel (<c>Student.IsIenProvisional</c>), destiné à débloquer les traitements internes d'une
/// école qui n'a pas encore reçu les numéros officiels de ses élèves — rien de plus.
///
/// Conséquences, non négociables :
///   • Un IEN provisoire ne doit JAMAIS être présenté comme officiel. L'export Planète marque la
///     ligne ; le rapport STATEDUC les compte à part.
///   • L'arrivée du numéro officiel l'ÉCRASE (<c>AssignStudentIenCommand</c>), sans conserver le
///     provisoire : garder deux identifiants pour un même élève, c'est garantir qu'un traitement
///     finira par utiliser le mauvais.
///   • Le service ne s'exécute pas si l'école n'a pas de <c>NationalSchoolCode</c> : sans lui, le
///     numéro fabriqué n'aurait même pas la forme d'un IEN.
///
/// Concurrence et absence de trou : même contrat que <see cref="IMatriculeGenerator"/> — l'appel se
/// fait DANS la transaction qui écrit l'élève, l'implémentation sérialise les appels concurrents de la
/// même école, et un rollback de la transaction annule l'incrément.
/// </summary>
public interface IIenGeneratorService
{
    /// <summary>
    /// Produit le prochain IEN provisoire de l'établissement. Lève <see cref="InvalidOperationException"/>
    /// si l'école n'a pas de code établissement national — un IEN sans code d'école n'identifie rien.
    /// </summary>
    Task<string> GenerateProvisionalIenAsync(Guid schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Valide la FORME d'un IEN saisi à la main (longueur, alphabet, clé de contrôle). Ne dit RIEN de
    /// son existence réelle au fichier national : seul le SIMEN peut l'affirmer, et tant que le relais
    /// API n'est pas ouvert (<see cref="ISimenBridgeService"/>) personne ne peut le vérifier ici.
    ///
    /// Un contrôle de forme qui passe n'est donc pas une garantie d'authenticité — c'est un garde-fou
    /// contre la faute de frappe, et il doit être présenté comme tel dans l'interface.
    /// </summary>
    bool IsWellFormed(string ienNumber);
}
