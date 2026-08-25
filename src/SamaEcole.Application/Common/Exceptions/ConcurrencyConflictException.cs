namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Conflit d'écriture : la plateforme a refusé d'enregistrer plutôt que d'écraser ou de dupliquer
/// (AGENTS.md règle #5). Traduite en HTTP 409 par SamaEcole.Web.
///
/// <para>
/// <b>Le message part directement à l'écran.</b> C'est la raison d'être de cette classe : le texte
/// porté ici est celui que lira une directrice ou un enseignant, sans reformulation par
/// l'intermédiaire. Il ne doit donc jamais nommer une table, un index ou une contrainte — voir
/// <c>SamaEcole.Persistence.Errors.UniqueConstraintCatalog</c>, qui rédige ces phrases. Le détail
/// technique voyage à part, dans <see cref="TechnicalDetail"/>, à seule destination des journaux.
/// </para>
///
/// <para>
/// Deux causes très différentes aboutissaient ici sous une phrase unique — « L'entité 'X' (IX_…) a
/// été modifiée par un autre utilisateur entre-temps » — et cette phrase était fausse pour la plus
/// fréquente des deux : saisir deux fois la même chose n'est pas une écriture concurrente. D'où la
/// séparation avec <see cref="DuplicateRecordException"/>, qui reste une sous-classe : le code HTTP
/// (409) et les tests qui attendent un conflit d'écriture ne changent pas.
/// </para>
/// </summary>
public class ConcurrencyConflictException : Exception
{
    /// <summary>
    /// Entité et clé concernées, pour les journaux serveur UNIQUEMENT. Jamais renvoyé au client :
    /// une erreur ne renseigne pas sur la structure interne de la base (docs/Volume_7_Security.md).
    /// </summary>
    public string TechnicalDetail { get; }

    /// <summary>
    /// Verrou optimiste (xmin) : la ligne a changé entre sa lecture et son enregistrement. Personne
    /// n'a saisi de doublon — c'est la fiche qui a bougé sous les doigts de l'utilisateur.
    /// </summary>
    public ConcurrencyConflictException(string entityName, object key)
        : base("Cette fiche a été modifiée par une autre personne pendant que vous la remplissiez. "
             + "Vos modifications n'ont pas été enregistrées, afin de ne pas effacer les siennes. "
             + "Fermez cette fenêtre, rouvrez la fiche pour voir la version à jour, puis refaites "
             + "votre modification.")
        => TechnicalDetail = $"{entityName} ({key})";

    /// <summary>Réservé aux sous-classes qui rédigent elles-mêmes le message destiné à l'écran.</summary>
    protected ConcurrencyConflictException(string message, string technicalDetail)
        : base(message)
        => TechnicalDetail = technicalDetail;
}

/// <summary>
/// Doublon refusé par la base : la ligne existe déjà (PostgreSQL 23505). Rien de concurrent dans le
/// cas courant — l'utilisateur a simplement enregistré deux fois la même chose, ou le pré-contrôle
/// du Handler a été court-circuité par une saisie simultanée.
///
/// Sous-classe de <see cref="ConcurrencyConflictException"/> à dessein : même statut HTTP 409, même
/// promesse de ne jamais dupliquer silencieusement (AGENTS.md règle #5) ; seuls le message et le
/// code d'erreur diffèrent, pour que l'utilisateur lise ce qui s'est réellement passé.
/// </summary>
public sealed class DuplicateRecordException(string message, string technicalDetail)
    : ConcurrencyConflictException(message, technicalDetail);
