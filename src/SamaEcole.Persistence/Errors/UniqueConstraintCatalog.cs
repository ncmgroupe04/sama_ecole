namespace SamaEcole.Persistence.Errors;

/// <summary>
/// Traduit un refus d'unicité de PostgreSQL (SQLSTATE 23505) en une phrase destinée au PERSONNEL DE
/// L'ÉTABLISSEMENT — directrice, secrétariat, enseignant — et non à un développeur.
///
/// <para>
/// <b>Pourquoi ce fichier existe.</b> Le message renvoyé jusqu'ici était construit à partir des noms
/// techniques que Npgsql expose : « L'entité 'teacher_assignments'
/// (IX_teacher_assignments_TeacherId_ClassroomId_SubjectId_SchoolY~) a été modifiée par un autre
/// utilisateur entre-temps. » Cette phrase avait trois défauts, et chacun suffisait :
/// </para>
/// <list type="number">
///   <item>Elle était FAUSSE dans le cas le plus fréquent. Personne d'autre n'avait rien modifié :
///         l'utilisateur venait simplement de saisir deux fois la même chose.</item>
///   <item>Elle était ILLISIBLE pour son destinataire. Un nom de table, un nom d'index tronqué par
///         PostgreSQL à 63 caractères, et le mot « entité » ne disent rien à une directrice d'école.</item>
///   <item>Elle DIVULGUAIT le schéma de la base à quiconque utilise la plateforme
///         (docs/Volume_7_Security.md — une erreur ne renseigne jamais sur la structure interne).</item>
/// </list>
///
/// <para>
/// <b>Ce que doit dire un message.</b> Trois choses, dans cet ordre : ce qui vient de se passer, la
/// raison pour laquelle la plateforme refuse, et le geste concret qui débloque la situation. Jamais
/// « erreur », jamais « contrainte », jamais un nom de table.
/// </para>
///
/// <para>
/// <b>Clé de recherche.</b> Le nom de TABLE d'abord — il est stable, alors qu'un nom d'index est
/// tronqué à 63 caractères par PostgreSQL et change au moindre renommage de colonne. Le nom de
/// contrainte ne sert qu'à départager les tables qui portent plusieurs index uniques de sens
/// différents (les enseignants : matricule ou compte de connexion).
/// </para>
///
/// <para>
/// <b>Ajouter une entrée.</b> Toute nouvelle table portant un index unique atteignable depuis un
/// écran doit venir ici. À défaut, <see cref="GenericMessage"/> reste correct et lisible — il est
/// simplement moins précis.
/// </para>
/// </summary>
internal static class UniqueConstraintCatalog
{
    /// <summary>
    /// Repli utilisé quand la table n'est pas répertoriée. Volontairement complet plutôt que court :
    /// même sans connaître le métier concerné, l'utilisateur doit comprendre qu'il s'agit d'un
    /// doublon et non d'une panne.
    /// </summary>
    internal const string GenericMessage =
        "Cet enregistrement existe déjà. La plateforme refuse de le créer une seconde fois pour "
        + "éviter les doublons. Vérifiez la liste : une ligne identique s'y trouve déjà.";

    /// <summary>
    /// Message d'un conflit d'écriture RÉEL (deux personnes enregistrent la même fiche en même temps,
    /// ou le formulaire a été ouvert avant une modification faite ailleurs). Il ne parle pas de
    /// doublon : ici, rien n'a été saisi deux fois — c'est la fiche qui a changé sous les doigts de
    /// l'utilisateur, et la plateforme a refusé d'écraser le travail de l'autre personne.
    /// </summary>
    internal const string ConcurrentEditMessage =
        "Cette fiche a été modifiée par une autre personne pendant que vous la remplissiez. Vos "
        + "modifications n'ont pas été enregistrées, afin de ne pas effacer les siennes. Fermez cette "
        + "fenêtre, rouvrez la fiche pour voir la version à jour, puis refaites votre modification.";

    private static readonly Dictionary<string, string> ByTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teacher_assignments"] =
            "Cet enseignant assure déjà cette matière dans cette classe pour l'année scolaire en "
            + "cours. Une matière ne peut lui être confiée qu'une seule fois par classe : "
            + "l'affectation figure déjà dans la liste des affectations. Pour la modifier, retirez-la "
            + "d'abord, ou choisissez une autre classe ou une autre matière.",

        ["teacher_subjects"] =
            "Cette matière fait déjà partie des matières qualifiées de cet enseignant. Il est inutile "
            + "de l'ajouter une seconde fois.",

        ["classrooms"] =
            "Une classe porte déjà ce nom dans votre établissement. Deux classes ne peuvent pas avoir "
            + "le même nom, sans quoi il deviendrait impossible de savoir dans laquelle inscrire un "
            + "élève. Choisissez un nom différent (par exemple « 6eme A » et « 6eme B »).",

        ["subjects"] =
            "Une matière porte déjà ce nom pour ce niveau. Choisissez un autre nom, ou modifiez la "
            + "matière existante au lieu d'en créer une nouvelle.",

        ["subject_coefficient_overrides"] =
            "Un coefficient est déjà réglé pour cette matière, cette année et cette portée (série ou classe). "
            + "Rechargez la grille : la valeur existante s'y trouve, modifiez-la au lieu d'en créer une seconde.",

        ["LateArrivals"] =
            "Un billet d'entrée est déjà actif pour cet élève, ce cours et ce jour. Il n'en faut pas un second : "
            + "retrouvez le billet dans la liste, ou annulez-le d'abord s'il a été émis par erreur.",

        ["buildings"] =
            "Un bâtiment porte déjà ce nom dans votre établissement. Choisissez un nom différent.",

        ["rooms"] =
            "Une salle porte déjà ce nom dans ce bâtiment. Deux salles du même bâtiment ne peuvent "
            + "pas porter le même nom. Choisissez un autre nom, ou rattachez la salle à un autre "
            + "bâtiment.",

        ["school_years"] =
            "Une année scolaire porte déjà ce libellé. Vérifiez la liste des années scolaires : "
            + "celle que vous essayez de créer y figure déjà.",

        ["terms"] =
            "Un trimestre occupe déjà ce rang dans cette année scolaire. Deux trimestres ne peuvent "
            + "pas avoir le même numéro d'ordre. Modifiez le trimestre existant, ou donnez un rang "
            + "différent à celui-ci.",

        ["mentions"] =
            "Une mention porte déjà ce libellé. Modifiez la mention existante au lieu d'en créer une "
            + "seconde portant le même nom.",

        ["fee_categories"] =
            "Un type de frais porte déjà ce nom. Modifiez le type de frais existant plutôt que d'en "
            + "créer un second identique.",

        ["class_fees"] =
            "Un montant est déjà défini pour ce type de frais dans cette classe. Modifiez le montant "
            + "existant : en créer un second rendrait le total dû de chaque élève ambigu.",

        ["fee_installments"] =
            "Une échéance porte déjà ce numéro dans cet échéancier. Les échéances se suivent sans "
            + "doublon : vérifiez la numérotation.",

        ["fee_installment_plans"] =
            "Cette inscription a déjà un échéancier actif. Une inscription ne peut suivre qu'un seul "
            + "échéancier à la fois : modifiez celui qui existe, ou clôturez-le avant d'en créer un "
            + "autre.",

        ["grades"] =
            "Une note existe déjà pour cet élève, dans cette matière, pour cette évaluation du "
            + "trimestre. Pour la corriger, modifiez la note existante : en saisir une seconde "
            + "laisserait deux valeurs contradictoires sur le bulletin.",

        ["report_card_remarks"] =
            "Une appréciation a déjà été saisie pour cet élève sur ce trimestre. Modifiez "
            + "l'appréciation existante plutôt que d'en ajouter une seconde.",

        ["attendance_sheets"] =
            "L'appel a déjà été enregistré pour cette classe, cette matière et ce créneau. Pour "
            + "corriger une présence, rouvrez l'appel existant : un second appel sur le même créneau "
            + "fausserait le taux de présence.",

        ["student_attendances"] =
            "Cet élève figure déjà sur cet appel. Corrigez son statut sur la ligne existante.",

        ["enrollments"] =
            "Cet élève est déjà inscrit pour cette année scolaire. Consultez sa fiche : l'inscription "
            + "en cours s'y trouve. Une seconde inscription créerait un double dossier et un double "
            + "montant dû.",

        ["students"] =
            "Un élève porte déjà ce matricule. Le matricule est attribué automatiquement à "
            + "l'enregistrement : si ce message revient, prévenez votre administrateur.",

        ["teachers"] =
            "Un enseignant porte déjà ce matricule. Le matricule est attribué automatiquement à "
            + "l'enregistrement : si ce message revient, prévenez votre administrateur.",

        ["payments"] =
            "Un reçu porte déjà ce numéro. Le numéro de reçu est attribué automatiquement à "
            + "l'encaissement : n'enregistrez pas une seconde fois le même versement, et prévenez "
            + "votre administrateur si ce message revient.",

        ["employee_contracts"] =
            "Cette personne a déjà un contrat en cours. Clôturez le contrat actuel avant d'en "
            + "enregistrer un nouveau : deux contrats actifs produiraient deux fiches de paie.",

        ["fiches_paie"] =
            "Une fiche de paie existe déjà pour cette personne sur ce mois. Consultez-la plutôt que "
            + "d'en générer une seconde, qui ferait payer le salaire deux fois.",

        ["taxe_declarations"] =
            "Une déclaration existe déjà pour ce mois. Consultez-la dans l'historique : en générer "
            + "une seconde ferait déclarer deux fois les mêmes cotisations.",

        ["cashier_sessions"] =
            "Une session de caisse est déjà ouverte pour ce caissier. Clôturez la session en cours "
            + "avant d'en ouvrir une nouvelle.",

        ["school_settings"] =
            "Les réglages de cet établissement existent déjà. Modifiez-les au lieu d'en créer de "
            + "nouveaux.",

        ["surveillant_entries"] =
            "Un pointage existe déjà pour cet enseignant à cette date. Modifiez le pointage existant.",

        ["users"] =
            "Un compte utilise déjà cette adresse e-mail. Une adresse n'identifie qu'un seul compte "
            + "sur toute la plateforme (pas seulement dans cet établissement), casse comprise. "
            + "Utilisez-en une autre, ou réactivez le compte existant. Pour qu'une même personne gère "
            + "plusieurs établissements, c'est son compte qu'on y rattache, pas un second compte.",

        ["exam_sessions"] =
            "Une session existe déjà pour ce type d'examen et cette série sur cette année scolaire. "
            + "Consultez la liste des sessions : celle que vous essayez de créer s'y trouve déjà.",
    };

    /// <summary>
    /// Départage les tables portant PLUSIEURS index uniques de sens différents, où le seul nom de
    /// table donnerait un message à côté du problème réel. La recherche se fait par fragment, car
    /// PostgreSQL tronque les noms d'index à 63 caractères.
    /// </summary>
    private static readonly (string Fragment, string Message)[] ByConstraintFragment =
    [
        ("teachers_UserId",
            "Ce compte de connexion est déjà rattaché à un autre enseignant. Un compte ne peut "
            + "correspondre qu'à une seule fiche : détachez-le de l'autre fiche avant de le "
            + "rattacher ici."),

        ("employee_contracts_UserId",
            "Ce compte de connexion a déjà un contrat en cours. Clôturez ce contrat avant d'en "
            + "enregistrer un nouveau."),

        ("enrollments_SchoolId_ReceiptNumber",
            "Un reçu d'inscription porte déjà ce numéro. Le numéro est attribué automatiquement à "
            + "l'enregistrement : prévenez votre administrateur si ce message revient."),

        ("exam_dossiers_SchoolId_ExamSessionId_StudentId",
            "Cet élève a déjà un dossier pour cette session d'examen. Consultez la liste des "
            + "dossiers : il y figure déjà."),

        ("exam_dossiers_SchoolId_ExamSessionId_CandidateNumber",
            "Ce numéro de table est déjà attribué à un autre candidat de cette session. Laissez le "
            + "champ vide pour une attribution automatique, ou choisissez-en un autre."),
    ];

    /// <summary>
    /// Rend la phrase à afficher. Ne renvoie jamais null et ne laisse jamais passer un nom technique :
    /// à défaut d'entrée répertoriée, <see cref="GenericMessage"/> reste vrai et lisible.
    /// </summary>
    internal static string Describe(string? constraintName, string? tableName)
    {
        if (!string.IsNullOrWhiteSpace(constraintName))
        {
            foreach (var (fragment, message) in ByConstraintFragment)
            {
                if (constraintName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return message;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(tableName) && ByTable.TryGetValue(tableName, out var byTable)
            ? byTable
            : GenericMessage;
    }
}
