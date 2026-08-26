/**
 * Centre d'aide intégré (/aide) — guide utilisateur complet, consultable sans quitter
 * l'application ni recharger la page.
 *
 * Le CONTENU vit ici, dans une structure de données, et non en dur dans le gabarit Razor : c'est
 * ce qui permet à la barre de recherche de filtrer sur l'intégralité du texte (titre, définition,
 * procédure, recommandations…) sans dupliquer une seconde fois chaque paragraphe dans un attribut
 * de recherche. Ajouter un module d'aide = ajouter un objet à HELP_SECTIONS, rien d'autre.
 *
 * Chaque article suit le MÊME squelette pédagogique en six rubriques — définition, objectif,
 * problème résolu, procédure, impacts, recommandations. Cette régularité n'est pas cosmétique :
 * un utilisateur qui a lu un article sait où chercher dans tous les autres.
 *
 * Aucun appel réseau : la documentation est statique, donc consultable même quand l'API est
 * injoignable — c'est précisément le moment où l'on a besoin d'aide.
 */
(function () {
    'use strict';

    const HELP_SECTIONS = [
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'configuration',
            number: 1,
            title: 'Configuration initiale & Années scolaires',
            icon: 'calendar',
            summary: "Le socle de l'établissement : l'exercice académique de travail et les locaux qui l'accueillent.",
            concept:
                "Tout établissement travaille sur un EXERCICE : une année scolaire déclarée, bornée par deux dates et " +
                "découpée en trimestres. Ce module installe ce cadre, ainsi que les murs qui l'abritent. Rien de ce qui suit " +
                "n'a de sens sans lui : une inscription, un barème de frais, une note ou un bulletin ne sont jamais des " +
                "données flottantes — elles appartiennent à un exercice, et à un seul. C'est pourquoi ce module se paramètre " +
                "EN PREMIER, avant toute campagne de rentrée, et pourquoi Unikol n'autorise qu'une seule année active à la " +
                "fois : l'ambiguïté sur l'exercice de travail est la première source d'erreurs durables dans un logiciel de " +
                "gestion scolaire.",
            articles: [
                {
                    id: 'annee-scolaire',
                    title: "Ouverture et clôture d'une année académique",
                    location: 'Paramètres › Années scolaires',
                    href: '/parametres?tab=annees-scolaires',
                    roles: ['Directeur'],
                    definition:
                        "L'année scolaire est l'exercice académique déclaré par l'établissement : un libellé (« 2026-2027 »), " +
                        "une date d'ouverture, une date de clôture et les trimestres qui la découpent. Une seule année peut être " +
                        "ACTIVE à un instant donné, et cette unicité est garantie par la base de données elle-même, non par une " +
                        "simple précaution d'affichage.",
                    objectif:
                        "Rattacher sans ambiguïté chaque donnée produite — inscription, barème de frais, encaissement, note, " +
                        "bulletin — à l'exercice auquel elle appartient véritablement. L'année active est le contexte implicite " +
                        "de tout le travail quotidien : elle épargne à chaque agent d'avoir à préciser l'exercice à chaque saisie.",
                    probleme:
                        "Sans exercice de référence, deux rentrées finissent invariablement par se confondre. On retrouve alors " +
                        "des réinscriptions imputées à l'année précédente, des tableaux d'effectifs qui additionnent deux " +
                        "promotions et des recouvrements réclamés au titre d'un exercice déjà soldé. La correction, elle, se " +
                        "fait dossier par dossier, longtemps après les faits.",
                    procedure: [
                        "Ouvrez Paramètres, puis l'onglet « Années scolaires ».",
                        "Cliquez sur « Nouvelle année scolaire » et renseignez le libellé (« 2026-2027 »), la date de début et la date de fin de l'exercice.",
                        "Déclarez les trimestres de l'année : leur découpage conditionne l'ensemble des saisies de notes et l'édition des bulletins.",
                        "Vérifiez attentivement les dates saisies, puis enregistrez. L'année est créée à l'état « à venir » et n'a encore aucun effet sur l'application.",
                        "Le jour de la rentrée, cliquez sur « Activer » en regard de l'année concernée. Par mesure de sécurité, la bascule exige la confirmation du mot de passe du Directeur.",
                        "L'année précédente passe automatiquement en lecture seule : ses données demeurent intégralement consultables, mais ne sont plus modifiables.",
                        "Si le calendrier se décale en cours d'exercice, une année en cours ou à venir reste corrigeable : prolonger la période recale les trimestres sans jamais altérer les notes déjà saisies."
                    ],
                    impacts: [
                        "Inscriptions : toute nouvelle inscription est rattachée d'office à l'année active. Un élève ne peut détenir qu'une seule inscription active par année.",
                        "Frais scolaires : les barèmes sont paramétrés par année. Une nouvelle année suppose de reconduire ou de réviser la grille tarifaire.",
                        "Notes et bulletins : les trimestres déclarés ici alimentent directement les écrans de saisie et l'en-tête des bulletins.",
                        "Comptabilité : les rapports financiers et les statistiques de la direction s'établissent sur le périmètre de l'exercice actif.",
                        "Barre supérieure : l'année active est rappelée en permanence dans l'en-tête de l'application, afin que nul ne travaille par inadvertance sur le mauvais exercice."
                    ],
                    recommandations: [
                        "N'activez la nouvelle année qu'une fois les frais scolaires reconduits : une inscription enregistrée avant le barème se retrouve sans montant dû.",
                        "N'activez jamais une année en cours de journée comptable : clôturez d'abord la caisse de l'exercice précédent.",
                        "Contrôlez le découpage des trimestres avant la première saisie de notes ; le corriger après coup impose de vérifier chaque bulletin déjà édité.",
                        "Une année révolue est volontairement verrouillée. Une donnée qui s'y révèle erronée fait l'objet d'une régularisation historisée sur l'exercice courant, jamais d'une réécriture du passé."
                    ]
                },
                {
                    id: 'infrastructures',
                    title: 'Bâtiments, salles et capacités d’accueil',
                    location: 'Gestion Scolaire › Bâtiments & Salles',
                    href: '/infrastructures',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Cartographie physique de l'établissement, organisée en deux niveaux : le bâtiment (« Bloc A », " +
                        "« Annexe Nord ») et les salles qu'il abrite, chacune dotée d'un type — salle de classe, laboratoire, " +
                        "bureau, autre — et d'une capacité exprimée en places assises. Cette structure est délibérément " +
                        "INDÉPENDANTE des classes pédagogiques : un local est un local, une classe est un groupe d'élèves.",
                    objectif:
                        "Disposer d'un inventaire fiable des locaux afin d'affecter les classes en connaissance de cause, de " +
                        "mesurer le taux d'occupation réel et d'anticiper la saturation avant qu'elle ne se manifeste au premier " +
                        "jour de classe. Pour la direction, c'est l'instrument qui permet d'arbitrer l'ouverture d'une classe " +
                        "supplémentaire sur une donnée mesurée plutôt que sur une impression, et de répondre sans délai à toute " +
                        "demande de justification de la capacité d'accueil.",
                    probleme:
                        "L'inventaire des locaux réside le plus souvent dans la mémoire du surveillant général. Il en résulte " +
                        "des classes de cinquante élèves dans une salle de trente-cinq places, deux groupes convoqués " +
                        "simultanément dans le même local, et l'impossibilité de justifier une capacité d'accueil devant " +
                        "l'inspection académique.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Bâtiments & Salles.",
                        "Créez d'abord le bâtiment : nom et description succincte (situation, étage, usage dominant).",
                        "Depuis la fiche du bâtiment, ajoutez les salles une à une : nom, type et capacité en nombre de places.",
                        "Renseignez une capacité SINCÈRE — le nombre de places réellement disponibles, et non le nombre théorique.",
                        "Les indicateurs en tête d'écran totalisent en temps réel les bâtiments, les salles et la capacité globale de l'établissement.",
                        "Une salle devenue inutilisable est archivée, jamais supprimée : l'historique des affectations passées demeure intact."
                    ],
                    impacts: [
                        "Classes : l'affectation d'une classe à une salle confronte l'effectif inscrit à la capacité déclarée.",
                        "Emploi du temps : les créneaux s'appuient sur les salles existantes pour prévenir les collisions d'occupation.",
                        "Tableau de bord : la capacité totale nourrit les ratios d'occupation présentés à la direction.",
                        "Dossiers administratifs : la capacité d'accueil déclarée est une pièce régulièrement exigée lors des visites de conformité."
                    ],
                    recommandations: [
                        "Adoptez une nomenclature homogène et durable (« Bloc A — Salle 3 » plutôt que « salle du fond »).",
                        "Actualisez la capacité après tout réaménagement mobilier : un chiffre obsolète alimente des ratios trompeurs.",
                        "Distinguez rigoureusement les bureaux administratifs des salles de classe, sous peine de gonfler artificiellement la capacité pédagogique.",
                        "Archivez plutôt que de supprimer : l'application ne pratique aucune suppression définitive des données de gestion, et c'est une garantie."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'pedagogie',
            number: 2,
            title: 'Structure pédagogique & Matières modulables',
            icon: 'book',
            summary: "Les classes, les cycles, et le moteur d'évaluation qui épouse aussi bien l'APC du primaire que la notation du secondaire.",
            concept:
                "Une école est d'abord une organisation pédagogique : des groupes d'élèves — les classes —, rattachés à des " +
                "niveaux, eux-mêmes réunis en cycles, et pour chaque niveau une grille d'évaluation. Ce module construit " +
                "cette ossature. Sa particularité tient au moteur d'évaluation, conçu pour épouser DEUX traditions sans en " +
                "privilégier aucune : la liste plate de matières coefficientées du secondaire, et la grille hiérarchique à " +
                "deux étages — domaines et activités, aux barèmes hétérogènes — que l'Approche par les Compétences impose au " +
                "primaire sénégalais. C'est ce paramétrage, et lui seul, qui détermine la forme exacte des bulletins " +
                "imprimés.",
            articles: [
                {
                    id: 'classes-cycles',
                    title: 'Gestion des classes et des cycles',
                    location: 'Gestion Scolaire › Classes',
                    href: '/classes',
                    roles: ['Directeur', 'Secrétariat', 'Enseignant'],
                    definition:
                        "La classe est le groupe pédagogique auquel un élève est inscrit pour une année donnée : un nom " +
                        "(« CM2 A », « 3e B », « Terminale S2 »), un niveau et un effectif maximal. Le NIVEAU détermine le " +
                        "cycle — CI/CP, CE1/CE2, CM1/CM2, Collège, Lycée — et commande le barème de notation appliqué : " +
                        "sur dix au primaire, sur vingt au collège et au lycée.",
                    objectif:
                        "Constituer l'ossature autour de laquelle s'organisent les inscriptions, les affectations d'enseignants, " +
                        "les grilles d'évaluation, les barèmes de frais et l'édition des bulletins. Pour le secrétariat, c'est la " +
                        "nomenclature de référence à laquelle tout se rattache : un groupe correctement déclaré ici épargne des " +
                        "dizaines de corrections ultérieures sur les inscriptions, les frais et les bulletins. Pour la direction, " +
                        "c'est la base de tout comptage d'effectif.",
                    probleme:
                        "Une nomenclature de classes flottante — « CM2A » ici, « CM2 A » là, « cm2-a » ailleurs — disperse " +
                        "les effectifs entre des groupes fantômes, fausse les statistiques et interdit tout classement " +
                        "cohérent. À l'échelle d'une rentrée, la reprise manuelle représente plusieurs journées de travail.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Classes.",
                        "Cliquez sur « Nouvelle classe » et renseignez le nom exact du groupe.",
                        "Sélectionnez le niveau : ce choix détermine le cycle, et par conséquent le barème de notation qui s'appliquera aux bulletins.",
                        "Indiquez l'effectif maximal souhaité, puis, le cas échéant, la salle d'affectation.",
                        "Désignez le professeur principal ou le titulaire de la classe lorsque l'établissement pratique cet usage.",
                        "Enregistrez. La classe devient immédiatement sélectionnable à l'inscription, à la saisie des notes et au paramétrage des frais."
                    ],
                    impacts: [
                        "Inscriptions : la classe conditionne le barème de frais appliqué et donc le montant dû par la famille.",
                        "Matières : les grilles d'évaluation sont attachées au NIVEAU ; deux classes de même niveau partagent la même grille.",
                        "Notes : le cycle de la classe fixe le barème par défaut de chaque note saisie.",
                        "Bulletins : le nom de la classe, son effectif et le rang de l'élève y figurent tels qu'ils sont enregistrés ici.",
                        "Infrastructures : l'affectation d'une salle confronte l'effectif au nombre de places disponibles."
                    ],
                    recommandations: [
                        "Arrêtez une convention de nommage AVANT la première création, et tenez-vous-y sans exception.",
                        "Créez l'intégralité des classes avant d'ouvrir la campagne d'inscriptions : réaffecter un élève après coup impose de reconsidérer son barème de frais.",
                        "Le niveau n'est pas une étiquette décorative : il gouverne le barème de notation. Une erreur à cet endroit se propage jusqu'aux bulletins.",
                        "Une classe qui n'ouvre pas est archivée en fin d'exercice, jamais supprimée."
                    ]
                },
                {
                    id: 'apc-matieres',
                    title: "Moteur d'évaluation APC & matières hiérarchiques",
                    location: 'Gestion Scolaire › Matières',
                    href: '/matieres',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Le moteur d'évaluation modulaire d'Unikol autorise deux formes de grilles. La grille SIMPLE du " +
                        "secondaire aligne des matières de premier niveau, chacune dotée d'un coefficient. La grille " +
                        "HIÉRARCHIQUE du primaire, conforme à l'Approche par les Compétences, organise l'évaluation sur deux " +
                        "étages : un DOMAINE parent (« Lang. & Com. », « Mathématiques ») porte des ACTIVITÉS filles " +
                        "(« P. Alphabétique », « Vocabulaire », « Fluidité ») ou la paire « Ressources » / « Compétences ». " +
                        "Chaque ligne d'évaluation peut recevoir son propre barème maximal — sur 10, 16, 24, 40, 60 — et son " +
                        "propre rang d'affichage.",
                    objectif:
                        "Reproduire À L'IDENTIQUE la grille officielle de l'établissement, quelle qu'en soit la structure, " +
                        "sans contraindre l'école à plier sa pédagogie aux limites du logiciel. La profondeur est " +
                        "volontairement bornée à deux niveaux, exactement comme les grilles imprimées.",
                    probleme:
                        "La plupart des logiciels de gestion scolaire n'admettent qu'une liste plate de matières notées sur " +
                        "vingt. Les écoles primaires sénégalaises, qui évaluent par domaines et par activités avec des " +
                        "barèmes hétérogènes, se voient alors contraintes de tenir leurs bulletins sous tableur — d'où des " +
                        "totaux recalculés à la main, des erreurs de report et des bulletins dont la présentation varie " +
                        "d'une classe à l'autre.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Matières et sélectionnez le niveau concerné : les grilles sont propres à chaque niveau.",
                        "GRILLE SIMPLE (collège, lycée) : créez chaque matière avec son nom et son coefficient, et laissez le barème vide pour hériter de celui du cycle, soit /20.",
                        "GRILLE APC (primaire) : créez d'abord les DOMAINES parents — « Lang. & Com. », « Maths », « Éveil » — sans leur attribuer de barème, car un domaine n'est jamais noté.",
                        "Ajoutez ensuite, sous chaque domaine, les ACTIVITÉS filles : nom, coefficient et barème propre — sur 60, sur 40, sur 24 ou sur 16 — tel qu'il figure, ligne par ligne, sur la grille officielle.",
                        "Ordonnez les lignes au moyen des flèches de réorganisation : « Ressources » précède toujours « Compétences », et aucun tri automatique ne saurait le deviner.",
                        "Personnalisez, sur le premier domaine, les en-têtes des deux premières colonnes du bulletin : « Domaines » et « Activités » au CI-CP, « Activités » et « Contrôles » au CE1-CE2.",
                        "Prévisualisez un bulletin vierge de la classe afin de confronter la grille écran à la grille papier avant toute saisie de notes."
                    ],
                    impacts: [
                        "Saisie des notes : l'écran de saisie reproduit fidèlement la hiérarchie ; les domaines parents y apparaissent en intitulé et n'acceptent aucune note.",
                        "Calcul des moyennes : chaque note est ramenée au barème de référence avant pondération, de sorte qu'un 45/60 et un 18/24 pèsent identiquement — soit 15/20.",
                        "Bulletins PDF : la fusion verticale de la première colonne, les en-têtes et l'ordre des lignes découlent directement de ce paramétrage.",
                        "Coefficients : ils déterminent le total des points, le total des coefficients et la moyenne générale imprimée sur le bulletin."
                    ],
                    recommandations: [
                        "Ayez la grille officielle imprimée sous les yeux durant tout le paramétrage : l'objectif est la reproduction exacte, non l'interprétation.",
                        "N'attribuez jamais de barème à un domaine parent : sa valeur est celle de ses activités, et la saisie d'une note sur un parent est refusée.",
                        "Un coefficient erroné fausse silencieusement l'ensemble des bulletins du niveau. Faites-le vérifier par un second regard avant la première saisie.",
                        "Achevez la structure de la grille AVANT d'ouvrir la saisie aux enseignants : la remanier une fois les notes saisies impose de contrôler chaque moyenne.",
                        "Une même matière porte légitimement des coefficients différents selon le niveau — « Mathématiques » vaut 4 au primaire et 6 en série scientifique. Ce n'est pas un doublon, c'est le cas normal."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'personnel',
            number: 3,
            title: 'Gestion du personnel & Enseignants',
            icon: 'users',
            summary: "Les fiches du corps enseignant, leurs matières, leurs classes et leur rattachement contractuel.",
            concept:
                "Le corps enseignant est décrit par deux objets distincts qu'il ne faut jamais confondre : la FICHE, qui " +
                "décrit une personne — identité, matricule interne, conditions contractuelles —, et le COMPTE UTILISATEUR, " +
                "qui ouvre un accès à l'application. À la fiche se rattachent les AFFECTATIONS, c'est-à-dire les couples " +
                "matière + classe pour lesquels le professeur est habilité. Cette notion d'affectation est le pivot du module " +
                ": elle borne ce que chacun voit et saisit, alimente l'emploi du temps et le pointage des heures, et fonde la " +
                "responsabilité de chaque note portée au dossier d'un élève.",
            articles: [
                {
                    id: 'enseignants',
                    title: 'Profils des enseignants et affectations',
                    location: 'Gestion Scolaire › Enseignants',
                    href: '/enseignants',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "La fiche enseignant rassemble l'identité du professeur, ses coordonnées, son matricule interne — " +
                        "généré par l'application au moment de l'enregistrement — ainsi que ses AFFECTATIONS : le couple " +
                        "matière + classe pour lequel il est habilité à saisir des notes et à faire l'appel.",
                    objectif:
                        "Circonscrire précisément le périmètre d'intervention de chaque enseignant et alimenter, à partir d'une " +
                        "source unique, l'emploi du temps, le pointage des heures, la paie et les bulletins. Pour la direction, " +
                        "la fiche enseignant est le point de rattachement unique du corps professoral : elle rend visible, en un " +
                        "seul écran, qui enseigne quoi et à qui, et fonde la responsabilité pédagogique de chaque saisie portée " +
                        "au dossier d'un élève.",
                    probleme:
                        "Sans registre d'affectation, n'importe quel compte peut saisir des notes dans n'importe quelle " +
                        "matière, les heures effectuées se réconcilient de mémoire en fin de mois, et une erreur de saisie " +
                        "reste sans auteur identifiable. La responsabilité pédagogique se dilue.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Enseignants, puis cliquez sur « Nouvel enseignant ».",
                        "Renseignez l'état civil, le téléphone, l'adresse électronique et la spécialité. Le matricule est attribué automatiquement à l'enregistrement, jamais à l'ouverture du formulaire.",
                        "Précisez la nature du rattachement — permanent ou vacataire — ainsi que les éléments contractuels utiles à la paie.",
                        "Depuis la fiche détaillée, ajoutez les affectations : pour chaque matière enseignée, désignez la ou les classes concernées.",
                        "Créez, si nécessaire, le compte utilisateur associé depuis Paramètres › Utilisateurs, en lui attribuant le rôle « Enseignant ».",
                        "Un départ se traduit par l'archivage de la fiche : l'historique des notes saisies et des heures pointées demeure intégralement conservé."
                    ],
                    impacts: [
                        "Saisie des notes : un enseignant ne voit et ne renseigne que les couples matière/classe qui lui sont affectés.",
                        "Appel en classe : les classes proposées à l'appel découlent de ces mêmes affectations.",
                        "Emploi du temps et pointage : les créneaux et les heures effectuées s'adossent aux affectations déclarées.",
                        "Fiches de paie : les éléments contractuels de la fiche alimentent le calcul de la rémunération et les déclarations fiscales et sociales.",
                        "Bulletins : le nom de l'enseignant peut figurer en regard de sa matière selon le modèle retenu."
                    ],
                    recommandations: [
                        "Enregistrez les affectations avant l'ouverture de la première période de notation, faute de quoi les enseignants se heurteront à un écran de saisie vide.",
                        "La fiche enseignant et le compte utilisateur sont deux objets distincts : le premier décrit une personne, le second ouvre un accès. Les deux sont nécessaires.",
                        "Maintenez le numéro de téléphone à jour : il constitue le canal de rappel le plus rapide en cas d'absence imprévue.",
                        "Vérifiez la spécialité déclarée avant toute affectation : elle vous prémunit contre l'attribution d'une matière à un professeur qui ne la traite pas."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'eleves',
            number: 4,
            title: "Mouvements d'élèves — Inscriptions & Réinscriptions",
            icon: 'document',
            summary: "De l'accueil d'un nouvel élève à la reconduction annuelle, avec la production automatique de l'échéancier financier.",
            concept:
                "L'inscription est le point de convergence de tout le produit : l'acte unique qui relie un ÉLÈVE — son " +
                "identité, son matricule —, une ANNÉE scolaire, une CLASSE et un BARÈME de frais. On distingue la première " +
                "inscription, qui crée l'identité scolaire, de la réinscription, qui prolonge un dossier existant sans jamais " +
                "le dupliquer. Dans les deux cas, la validation fige le montant dû ligne à ligne et ouvre le dossier " +
                "financier de la famille : c'est ici, et nulle part ailleurs, que la scolarité d'un enfant devient une " +
                "créance de l'établissement.",
            articles: [
                {
                    id: 'premiere-inscription',
                    title: "Première inscription et saisie de l'état civil",
                    location: 'Gestion Scolaire › Élèves',
                    href: '/eleves',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Acte fondateur du dossier scolaire : l'élève est créé dans le registre de l'établissement avec son " +
                        "état civil complet — nom, prénoms, date et lieu de naissance, sexe, nationalité —, les coordonnées " +
                        "de son tuteur légal et sa photographie. Un MATRICULE unique lui est attribué au moment précis de " +
                        "l'enregistrement, et non à l'ouverture du formulaire.",
                    objectif:
                        "Constituer une identité scolaire pérenne, opposable, qui suivra l'élève de son admission à sa sortie et " +
                        "servira de clé unique à ses notes, à ses bulletins et à sa situation financière. Pour le secrétariat, " +
                        "c'est la garantie qu'un même enfant ne sera jamais compté deux fois ni recherché en vain ; pour la " +
                        "direction, c'est la source unique dont procèdent l'effectif déclaré, les documents officiels et le " +
                        "dossier financier de la famille.",
                    probleme:
                        "Le registre papier autorise les homonymies non arbitrées, les dates de naissance divergentes d'un " +
                        "document à l'autre et les doublons créés par deux agents travaillant simultanément. Ces défauts se " +
                        "révèlent au pire moment : à l'édition des bulletins ou lors de l'inscription aux examens officiels.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Élèves, puis cliquez sur « Nouvel élève ».",
                        "Recherchez d'abord le nom dans le registre existant : cette précaution élémentaire évite la quasi-totalité des doublons.",
                        "Saisissez l'état civil en le recopiant sur l'extrait de naissance, orthographe et accents compris — c'est cette graphie qui figurera sur tous les documents officiels.",
                        "Renseignez le tuteur légal : nom, lien de parenté, téléphone et adresse. Ce numéro est celui qui recevra les notifications par SMS ou WhatsApp.",
                        "Ajoutez la photographie de l'élève : elle est automatiquement compressée avant transmission et alimente la carte scolaire.",
                        "Enregistrez. Le matricule est généré dans la transaction même, ce qui garantit l'absence de trou et de collision dans la numérotation.",
                        "Procédez ensuite à l'inscription proprement dite : sélectionnez la classe, contrôlez le barème de frais proposé, puis validez.",
                        "Éditez et remettez à la famille le reçu d'inscription ainsi que l'attestation, tous deux au format PDF."
                    ],
                    impacts: [
                        "Comptabilité : la validation de l'inscription fige le montant total dû, ligne par ligne, et ouvre le dossier financier de l'élève.",
                        "Échéancier : les échéances de règlement sont établies à partir du barème de la classe.",
                        "Classe : l'effectif de la classe s'accroît immédiatement et se confronte à la capacité de la salle affectée.",
                        "Notes : l'élève apparaît dès la validation dans les listes de saisie des notes et dans les feuilles d'appel.",
                        "Documents : reçu, attestation d'inscription et carte scolaire sont produits à partir de cet état civil."
                    ],
                    recommandations: [
                        "N'ouvrez jamais deux formulaires de création simultanément sur deux postes pour le même élève.",
                        "Le matricule n'est jamais réservé à l'avance : un formulaire abandonné ne consomme aucun numéro. Ne cherchez donc pas à « garder » un matricule.",
                        "Une erreur d'état civil se corrige par la fiche élève, et la correction est historisée. Ne créez jamais un second élève pour rectifier le premier.",
                        "Le reçu doit impérativement être remis à la famille, revêtu de la mention réglementaire invitant les parents à le conserver avec soin.",
                        "Contrôlez le numéro de téléphone du tuteur au moment de la saisie : un numéro erroné rend inopérante toute la chaîne de relance."
                    ]
                },
                {
                    id: 'reinscription',
                    title: 'Réinscription et changement de classe',
                    location: 'Gestion Scolaire › Inscriptions',
                    href: '/inscriptions',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "La réinscription rattache un élève DÉJÀ enregistré à une nouvelle année scolaire et à une nouvelle " +
                        "classe. Elle ne crée ni élève ni matricule : elle prolonge un dossier existant. Le changement de " +
                        "classe, quant à lui, redirige une inscription en cours vers un autre groupe du même exercice.",
                    objectif:
                        "Reconduire la scolarité d'une cohorte entière en quelques minutes, tout en conservant l'intégralité de " +
                        "l'historique — notes, bulletins, règlements, discipline — attaché au matricule d'origine. Pour le " +
                        "secrétariat, la campagne de rentrée cesse d'être une ressaisie massive pour devenir un simple contrôle ; " +
                        "pour la direction, la continuité du dossier rend enfin possible le suivi d'une cohorte sur plusieurs " +
                        "années consécutives.",
                    probleme:
                        "Ressaisir chaque rentrée l'état civil de plusieurs centaines d'élèves consomme des semaines de " +
                        "travail et rompt le fil de l'historique : l'élève se retrouve doté de deux dossiers, ses bulletins " +
                        "antérieurs deviennent introuvables et sa situation financière se scinde en deux.",
                    procedure: [
                        "Assurez-vous que la nouvelle année scolaire est ACTIVE et que ses barèmes de frais sont paramétrés.",
                        "Ouvrez Gestion Scolaire › Inscriptions, puis choisissez « Réinscrire ».",
                        "Recherchez l'élève par son matricule ou par son nom : sa fiche remonte avec l'intégralité de son historique.",
                        "Sélectionnez la classe d'accueil de la nouvelle année ; l'application propose le barème correspondant.",
                        "Vérifiez le montant dû et l'échéancier, puis validez la réinscription.",
                        "CHANGEMENT DE CLASSE EN COURS D'ANNÉE : ouvrez l'inscription en cours et sélectionnez la nouvelle classe. Si le barème diffère, la régularisation est historisée et demeure traçable.",
                        "Éditez le reçu de réinscription et remettez-le à la famille."
                    ],
                    impacts: [
                        "Historique : notes, bulletins et règlements des exercices antérieurs restent attachés au même élève et demeurent consultables.",
                        "Solde antérieur : un reliquat impayé de l'année précédente reste rattaché à l'inscription de cet exercice et n'est jamais reporté silencieusement.",
                        "Effectifs : les effectifs de la classe d'origine et de la classe d'accueil sont mis à jour simultanément.",
                        "Notes : un changement de classe en cours de trimestre appelle un contrôle des notes déjà saisies, les grilles pouvant différer d'un niveau à l'autre.",
                        "Comptabilité : toute variation du montant dû est historisée avec son auteur, sa date et son motif."
                    ],
                    recommandations: [
                        "Traitez les réinscriptions par classe entière plutôt qu'au fil de l'eau : les contrôles s'en trouvent grandement facilités.",
                        "Soldez ou constatez formellement les impayés de l'exercice précédent avant de réinscrire, faute de quoi la dette se dilue dans le nouvel échéancier.",
                        "Un changement de classe entre deux niveaux différents modifie la grille d'évaluation : vérifiez systématiquement les notes déjà saisies.",
                        "Le service Finance ne modifie jamais de lui-même un montant issu d'une inscription : toute correction relève du Secrétariat ou de la Direction, et reste historisée."
                    ]
                },
                {
                    id: 'echeanciers',
                    title: 'Affectation automatique des échéanciers financiers',
                    location: 'Gestion Scolaire › Inscriptions',
                    href: '/inscriptions',
                    roles: ['Directeur', 'Secrétariat', 'Finance'],
                    definition:
                        "L'échéancier est le calendrier de règlement adossé à une inscription : montant total dû, décomposé " +
                        "en lignes de frais figées à la validation, puis réparti en échéances datées. À défaut d'accord " +
                        "particulier, Unikol synthétise des échéances mensuelles uniformes à partir du barème de la classe. " +
                        "Un échéancier PERSONNALISÉ peut lui être substitué pour tenir compte d'une situation familiale " +
                        "particulière.",
                    objectif:
                        "Rendre exigible, à date certaine, ce que chaque famille doit à l'établissement, et fonder sur cette base " +
                        "un recouvrement méthodique plutôt qu'une réclamation improvisée. Pour la direction, l'échéancier " +
                        "transforme une créance diffuse en un calendrier d'encaissements prévisible ; pour le service financier, " +
                        "il fournit au guichet la réponse exacte à la seule question qui compte vraiment : que doit cette " +
                        "famille, et depuis quand ?",
                    probleme:
                        "Sans échéancier formalisé, nul ne sait qui doit quoi ni depuis quand. Le recouvrement se réduit à " +
                        "la mémoire du caissier, les familles de bonne foi sont relancées à tort tandis que les retards " +
                        "réels passent inaperçus, et la trésorerie devient imprévisible.",
                    procedure: [
                        "Le montant dû est CALCULÉ par l'application à partir du barème de la classe : il n'est jamais saisi à la main lors de l'inscription.",
                        "À la validation, le détail ligne à ligne est FIGÉ sur l'inscription : une révision ultérieure du barème ne modifiera pas rétroactivement les reçus déjà remis.",
                        "Consultez l'échéancier depuis la fiche de l'inscription : chaque échéance y figure avec sa date d'exigibilité et son montant.",
                        "ÉCHÉANCIER PERSONNALISÉ : créez un plan sur mesure pour une famille donnée, en fixant vous-même les dates et les montants convenus.",
                        "Une renégociation ne modifie jamais le plan en vigueur : elle le remplace, l'ancien étant annulé mais conservé, de sorte que l'accord antérieur reste consultable.",
                        "Chaque encaissement s'impute automatiquement sur le solde, et l'échéancier reflète en temps réel ce qui reste dû."
                    ],
                    impacts: [
                        "Caisse : le caissier voit, à l'écran, le solde exact et l'échéance courante de l'élève qu'il encaisse.",
                        "Recouvrement : le suivi des retards s'appuie sur les dates d'exigibilité de l'échéancier.",
                        "Avis de sommes dues : l'avis remis à la famille est édité directement depuis l'échéancier.",
                        "Trésorerie : les échéances à venir alimentent les prévisions d'encaissement de la direction.",
                        "Rapports financiers : le rapprochement entre attendu et encaissé procède de cette même source."
                    ],
                    recommandations: [
                        "Ne modifiez jamais un montant dû pour « faire tomber juste » : accordez plutôt une remise, laquelle est tracée et justifiée.",
                        "Un accord d'échelonnement doit être saisi le jour même où il est consenti : un arrangement verbal non enregistré n'existe pas au regard du système.",
                        "Contrôlez les barèmes en début d'exercice : ils sont figés sur chaque inscription au moment de sa validation.",
                        "Les échéanciers personnalisés demeurent l'exception : leur multiplication rend le recouvrement illisible."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'evaluations',
            number: 5,
            title: 'Évaluations, saisie des notes & bulletins PDF',
            icon: 'chart-multiple',
            summary: "De la note portée par l'enseignant au bulletin officiel imprimé, en passant par les moyennes, les rangs et les appréciations.",
            concept:
                "Ce module transforme les appréciations des enseignants en un document officiel. La chaîne est volontairement " +
                "à SENS UNIQUE : on saisit des notes, le serveur en dérive les moyennes, les totaux, le rang et la mention, " +
                "et le bulletin ne fait que restituer ce calcul. Aucune moyenne ne se saisit à la main, aucun bulletin ne " +
                "s'annote après coup. La difficulté tient à l'hétérogénéité des barèmes — une grille APC mêle des lignes sur " +
                "60, 40, 24 et 16 : chaque note est donc ramenée à une échelle commune avant d'être pondérée par son " +
                "coefficient, afin qu'un 45/60 et un 18/24 pèsent exactement le même poids.",
            articles: [
                {
                    id: 'saisie-notes',
                    title: 'Saisie sécurisée des notes par classe et par matière',
                    location: 'Gestion Scolaire › Notes et bulletins',
                    href: '/notes',
                    roles: ['Directeur', 'Secrétariat', 'Enseignant'],
                    definition:
                        "Écran de notation qui présente, pour un triplet classe + matière + trimestre, la liste nominative " +
                        "des élèves inscrits et permet d'y porter les notes de devoir et de composition. La saisie est " +
                        "protégée par un verrou optimiste : si deux personnes modifient la même note simultanément, la " +
                        "seconde est avertie du conflit plutôt que d'écraser silencieusement la première.",
                    objectif:
                        "Recueillir les notes une seule fois, à la source, dans un cadre borné par le barème et par les " +
                        "affectations de l'enseignant, et alimenter directement les calculs et les bulletins sans aucune reprise " +
                        "intermédiaire. Pour la direction, cela supprime la période d'incertitude qui sépare la remise des copies " +
                        "de l'édition des bulletins : à tout instant, l'état d'avancement de la notation est visible classe par " +
                        "classe et matière par matière.",
                    probleme:
                        "La chaîne classique — cahier de notes, puis tableur du surveillant, puis bulletin — comporte deux " +
                        "recopies manuelles, donc deux occasions d'erreur par élève et par matière. À l'échelle d'un " +
                        "établissement, les erreurs de report se comptent par dizaines chaque trimestre, et se découvrent " +
                        "généralement lorsque les parents ont déjà le bulletin en main.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Notes et bulletins.",
                        "Sélectionnez successivement la classe, la matière et le trimestre. Un enseignant ne se voit proposer que ses propres affectations.",
                        "La liste nominative s'affiche, accompagnée du barème applicable — celui de la ligne d'évaluation lorsqu'il est défini, celui du cycle à défaut.",
                        "Saisissez les notes. Toute valeur excédant le barème est refusée à la saisie, et non découverte au moment du bulletin.",
                        "Renseignez, selon la grille en vigueur, la note de devoir et la note de composition.",
                        "IMPORT EN MASSE : téléchargez le modèle Excel de la classe, complétez-le hors ligne, puis réimportez-le. Les valeurs y sont contrôlées une à une avant intégration.",
                        "Enregistrez. Chaque saisie est horodatée et attribuée à son auteur.",
                        "Une note erronée se corrige sur ce même écran ; la modification est consignée dans le journal d'audit."
                    ],
                    impacts: [
                        "Moyennes : chaque note est ramenée au barème de référence puis pondérée par le coefficient de la matière.",
                        "Rangs : le classement de la classe se recalcule automatiquement à chaque enregistrement.",
                        "Bulletins : le bulletin ne fait que restituer ces notes ; il n'existe aucune seconde saisie.",
                        "Mentions : la moyenne générale détermine la mention selon les seuils définis par l'établissement.",
                        "Audit : toute création ou modification de note est tracée, avec son auteur et son horodatage."
                    ],
                    recommandations: [
                        "Saisissez les notes matière par matière et menez chaque matière à son terme : une grille partiellement renseignée produit une moyenne trompeuse.",
                        "Vérifiez le barème affiché avant de commencer : une grille APC mêle légitimement des maxima de 60, 40, 24 et 16.",
                        "En cas de conflit signalé, rechargez l'écran et reprenez votre saisie : un collègue a modifié la même note entre-temps. Ne forcez jamais.",
                        "L'import Excel est le mode le plus sûr pour une classe nombreuse, mais contrôlez le rapport d'import avant de valider.",
                        "N'éditez les bulletins qu'une fois TOUTES les matières saisies et contrôlées."
                    ]
                },
                {
                    id: 'bulletins-pdf',
                    title: 'Génération des bulletins officiels au format PDF',
                    location: 'Gestion Scolaire › Notes et bulletins',
                    href: '/notes',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Production du bulletin de notes officiel, composé par le serveur au format PDF et reproduisant " +
                        "fidèlement le modèle de référence de l'établissement : en-tête, tableau des matières respectant la " +
                        "hiérarchie de la grille, moyennes, rang, mention, appréciations et blocs de signature. Le bulletin " +
                        "s'édite individuellement ou pour une classe entière, en un seul document.",
                    objectif:
                        "Délivrer un document officiel homogène, exact et immédiatement remettable, sans mise en page manuelle ni " +
                        "recopie, et dans une présentation strictement identique d'une classe à l'autre. Pour le secrétariat, " +
                        "l'édition d'une promotion entière devient l'affaire de quelques minutes au lieu de plusieurs journées ; " +
                        "pour la direction, l'établissement présente aux familles un document uniforme, qui ne trahit ni la " +
                        "classe ni l'agent qui l'a produit.",
                    probleme:
                        "Le bulletin composé sous traitement de texte varie d'une classe à l'autre, se désaligne dès qu'un " +
                        "nom est plus long que prévu, et suppose de recopier à la main des moyennes déjà calculées ailleurs. " +
                        "L'édition d'une promotion entière mobilise le secrétariat plusieurs jours par trimestre.",
                    procedure: [
                        "Assurez-vous au préalable que toutes les notes du trimestre sont saisies et contrôlées.",
                        "Complétez, le cas échéant, les appréciations et les décisions du conseil des professeurs pour chaque élève.",
                        "Ouvrez Gestion Scolaire › Notes et bulletins, puis sélectionnez la classe et le trimestre.",
                        "BULLETIN INDIVIDUEL : depuis la ligne de l'élève, demandez l'aperçu. Le document s'affiche à l'écran avant toute impression.",
                        "BULLETINS DE CLASSE : demandez l'édition groupée. Un unique document PDF réunit l'ensemble des bulletins de la classe, prêt pour l'impression en série.",
                        "PROCÈS-VERBAL DE DÉLIBÉRATION : éditez, pour le conseil de classe, le tableau récapitulatif des moyennes et des rangs.",
                        "Contrôlez l'aperçu, puis imprimez. Le document reflète l'état des données à l'instant de sa génération."
                    ],
                    impacts: [
                        "Matières : la hiérarchie des domaines et des activités, l'ordre des lignes et les en-têtes de colonnes proviennent intégralement du paramétrage des matières.",
                        "Notes : le bulletin n'est qu'une restitution ; il ne recalcule ni ne corrige rien.",
                        "Mentions : les seuils paramétrés par l'établissement déterminent la mention imprimée.",
                        "Appréciations : distinctions et observations du conseil figurent telles qu'elles ont été saisies.",
                        "Établissement : le nom, le logo et les mentions officielles proviennent des Paramètres de l'école."
                    ],
                    recommandations: [
                        "Éditez un bulletin témoin et faites-le relire avant de lancer l'impression d'une promotion entière.",
                        "Un bulletin édité prématurément et déjà remis ne se rattrape plus : contrôlez l'exhaustivité des notes en amont.",
                        "Vérifiez le logo et la dénomination de l'établissement dans les Paramètres avant la première édition de l'année.",
                        "Conservez le PDF de chaque trimestre : il constitue la preuve de ce qui a été effectivement remis à la famille.",
                        "N'annotez jamais un bulletin à la main : la correction se fait dans l'application, puis le document est réédité."
                    ]
                },
                {
                    id: 'moyennes-rangs',
                    title: 'Moyennes, rangs, mentions et appréciations',
                    location: 'Gestion Scolaire › Notes et bulletins',
                    href: '/notes',
                    roles: ['Directeur', 'Secrétariat', 'Enseignant'],
                    definition:
                        "Ensemble des grandeurs dérivées des notes : moyenne par matière, total des points, total des " +
                        "coefficients, moyenne générale, rang dans la classe, mention et appréciation. Toutes sont " +
                        "recalculées par le serveur, jamais saisies à la main. Les seuils de mention — « Excellent », " +
                        "« Très Bien », « Bien », « Assez Bien », « Passable » — sont librement définis par la direction, " +
                        "chacun par la moyenne minimale qui y donne droit, exprimée sur vingt.",
                    objectif:
                        "Garantir l'exactitude arithmétique et l'équité du classement, tout en laissant à l'établissement la " +
                        "maîtrise de ses propres seuils d'appréciation. Pour la direction, c'est la fin des contestations " +
                        "arithmétiques en conseil de classe : le calcul est reproductible et opposable, et la réclamation d'un " +
                        "parent se vérifie à l'écran en quelques secondes plutôt qu'en refaisant l'opération à la main devant " +
                        "lui.",
                    probleme:
                        "Les moyennes calculées à la main ou sous tableur souffrent d'erreurs de coefficient, de barèmes " +
                        "hétérogènes mal ramenés à une échelle commune et de rangs disputés en conseil de classe. Chaque " +
                        "réclamation d'un parent impose alors de refaire le calcul devant lui.",
                    procedure: [
                        "Définissez les seuils de mention dans Paramètres › Configuration : libellé et moyenne minimale, exprimée sur vingt.",
                        "Saisissez les notes : les moyennes, les totaux et le rang se recalculent à chaque enregistrement.",
                        "Consultez la synthèse de la classe pour vérifier la cohérence d'ensemble avant le conseil.",
                        "APPRÉCIATIONS : pour chaque élève, cochez la distinction retenue par le conseil — du blâme aux félicitations — et rédigez les observations.",
                        "Renseignez la décision du conseil lorsque la période concernée l'exige.",
                        "Tant qu'aucune appréciation n'a été saisie, le bulletin imprime les cases vierges plutôt qu'une valeur inventée."
                    ],
                    impacts: [
                        "Barèmes hétérogènes : chaque note est ramenée au barème de référence avant pondération, de sorte qu'un 45/60 et un 18/24 pèsent identiquement.",
                        "Coefficients : ils déterminent le total des points et la moyenne générale ; une erreur s'y propage jusqu'au rang.",
                        "Rang : il se recalcule automatiquement dès qu'une note de la classe est modifiée.",
                        "Bulletin : mention, rang et appréciations y sont imprimés tels qu'ils sont calculés ou saisis.",
                        "Procès-verbal de délibération : il consolide moyennes et rangs pour le conseil de classe."
                    ],
                    recommandations: [
                        "Arrêtez les seuils de mention en début d'exercice : les modifier après l'édition des bulletins crée une disparité entre trimestres.",
                        "Une moyenne qui paraît aberrante trahit presque toujours un coefficient ou un barème mal renseigné, non une erreur de calcul.",
                        "Ne saisissez jamais une moyenne à la main : elle est nécessairement dérivée des notes.",
                        "Rédigez des appréciations circonstanciées et bienveillantes : elles constituent souvent le seul message écrit que la famille conserve."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'comptabilite',
            number: 6,
            title: 'Comptabilité, frais scolaires & caisse',
            icon: 'wallet',
            summary: "Du barème tarifaire à l'encaissement au guichet, jusqu'au suivi méthodique des impayés.",
            concept:
                "Trois notions se succèdent et ne doivent jamais être confondues. Le BARÈME est ce que l'établissement décide " +
                "de facturer, par classe et par catégorie de frais. L'ÉCHÉANCIER est ce qu'une famille donnée doit, et à " +
                "quelles dates. L'ENCAISSEMENT est ce qu'elle a effectivement versé. Le principe qui gouverne tout le module " +
                "en découle : le service financier encaisse, il ne fixe ni ne corrige jamais un montant dû — toute révision " +
                "relève du secrétariat ou de la direction, et demeure historisée. C'est cette séparation des rôles qui rend " +
                "une caisse contrôlable.",
            articles: [
                {
                    id: 'bareme-frais',
                    title: "Barème des frais d'inscription et des mensualités",
                    location: 'Comptabilité › Frais Scolaires',
                    href: '/frais',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Grille tarifaire de l'établissement pour un exercice donné : frais d'inscription, mensualités et " +
                        "frais annexes — tenue, transport, cantine, examens —, définis par catégorie et déclinés classe par " +
                        "classe. C'est cette grille, et elle seule, qui détermine le montant dû à chaque inscription.",
                    objectif:
                        "Fixer une fois pour toutes le tarif applicable, de sorte que le montant réclamé à une famille ne dépende " +
                        "jamais de l'agent qui l'accueille au guichet. Pour la direction, c'est l'instrument qui rend la " +
                        "politique tarifaire de l'établissement explicite, comparable d'une classe à l'autre, et défendable aussi " +
                        "bien devant les familles que devant le conseil d'administration.",
                    probleme:
                        "Lorsque le tarif est de tradition orale, chaque agent applique sa propre version, les remises " +
                        "s'accordent sans trace, et l'établissement se trouve dans l'incapacité de justifier des écarts " +
                        "constatés entre familles d'une même classe.",
                    procedure: [
                        "Ouvrez Comptabilité › Frais Scolaires.",
                        "Vérifiez que l'année scolaire active est bien celle pour laquelle vous entendez paramétrer les tarifs.",
                        "Créez les catégories de frais que pratique l'établissement : frais d'inscription, mensualité, tenue scolaire, transport, cantine, fournitures et frais d'examen.",
                        "Pour chaque classe, renseignez le montant applicable à chaque catégorie.",
                        "Précisez le caractère du frais : obligatoire ou facultatif, ponctuel ou récurrent.",
                        "Enregistrez. Le barème devient immédiatement opérant pour toute nouvelle inscription.",
                        "RÉVISION EN COURS D'ANNÉE : la modification d'un montant est historisée avec son auteur, sa date et son motif, et n'altère jamais les inscriptions déjà validées."
                    ],
                    impacts: [
                        "Inscriptions : le montant total dû découle directement de ce barème et se fige, ligne par ligne, à la validation.",
                        "Échéanciers : les échéances mensuelles se déduisent des montants récurrents du barème.",
                        "Reçus : le détail imprimé sur le reçu reprend les lignes figées à l'inscription, et non le barème en vigueur au jour de l'impression.",
                        "Rapports financiers : le montant attendu de l'exercice résulte de l'agrégation des barèmes appliqués.",
                        "Historique : chaque révision tarifaire demeure consultable dans l'historique des modifications."
                    ],
                    recommandations: [
                        "Paramétrez les barèmes AVANT d'activer la nouvelle année et d'ouvrir les inscriptions.",
                        "Faites valider la grille par la direction avant sa mise en service : elle engage l'établissement vis-à-vis des familles.",
                        "Une révision tarifaire en cours d'exercice ne s'applique qu'aux inscriptions postérieures. C'est délibéré : un reçu déjà remis ne saurait changer de montant.",
                        "Distinguez soigneusement les frais obligatoires des frais facultatifs : cette distinction fonde le calcul des impayés.",
                        "Le service Finance encaisse, il ne fixe pas les tarifs. Le paramétrage du barème relève de la direction."
                    ]
                },
                {
                    id: 'encaissement',
                    title: 'Encaissement au guichet et reçus de paiement',
                    location: 'Comptabilité › Caisse (Encaissements)',
                    href: '/caisse',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Acte de caisse par lequel un versement de la famille est imputé sur le solde d'une inscription. " +
                        "Chaque encaissement donne lieu à un reçu numéroté, édité au format PDF, portant la mention " +
                        "réglementaire invitant les parents à conserver soigneusement leur reçu après paiement.",
                    objectif:
                        "Constater sans délai tout versement, en délivrer la preuve à la famille, et tenir en permanence un solde " +
                        "exact par élève. Pour le service financier, chaque versement devient immédiatement opposable, appuyé sur " +
                        "un reçu numéroté ; pour la direction, la recette du jour cesse d'être une estimation pour devenir un " +
                        "chiffre arrêté, rattaché à un agent et à une journée précise.",
                    probleme:
                        "Le carnet à souches se perd, se recopie mal et ne se totalise qu'en fin de journée. Les " +
                        "contestations de paiement sont alors indémontrables dans un sens comme dans l'autre, et le " +
                        "rapprochement entre la caisse physique et le registre relève de la reconstitution.",
                    procedure: [
                        "Ouvrez Comptabilité › Caisse (Encaissements) et ouvrez votre session de caisse en déclarant le fonds initial.",
                        "Recherchez l'élève par son matricule ou par son nom : sa situation financière s'affiche — total dû, déjà réglé, solde restant.",
                        "Saisissez le montant versé et le mode de règlement retenu : espèces, virement bancaire, chèque ou paiement mobile.",
                        "Vérifiez l'imputation proposée sur les lignes de frais, puis validez l'encaissement.",
                        "Le solde de l'inscription est mis à jour dans la transaction même : deux encaissements concurrents sur le même élève ne peuvent produire de sur-crédit.",
                        "Éditez le reçu PDF et remettez-le à la famille, en attirant son attention sur la nécessité de le conserver.",
                        "CORRECTION : un encaissement erroné est ANNULÉ — statut « annulé », conservé dans l'historique —, jamais effacé. La saisie correcte est ensuite reprise."
                    ],
                    impacts: [
                        "Solde de l'élève : le cumul des versements s'impute immédiatement sur le montant dû de l'inscription.",
                        "Échéancier : les échéances honorées se soldent au fur et à mesure des encaissements.",
                        "Recouvrement : l'élève sort automatiquement de la liste des débiteurs dès que son solde est apuré.",
                        "Clôture de caisse : chaque encaissement alimente le journal de la session de caisse ouverte.",
                        "Trésorerie et rapports : les recettes du jour remontent dans les tableaux de bord de la direction."
                    ],
                    recommandations: [
                        "N'encaissez jamais sans avoir ouvert votre session de caisse : le versement ne serait rattaché à aucune journée comptable.",
                        "Remettez systématiquement le reçu, même pour un versement partiel. C'est l'unique preuve dont dispose la famille.",
                        "Le service Finance ne modifie jamais un montant dû issu d'une inscription : il ne fait qu'y imputer des versements. Toute correction du montant dû relève du Secrétariat ou de la Direction.",
                        "En cas de micro-coupure réseau pendant l'envoi, l'application retente automatiquement l'enregistrement en arrière-plan (bandeau « en attente d'envoi ») tant que l'onglet reste ouvert, sans jamais créer de doublon. Si la coupure persiste au-delà de ces tentatives, rien n'est enregistré et vous en êtes averti : reprenez alors la validation vous-même.",
                        "Un encaissement erroné s'annule et se ressaisit. Ne tentez jamais de le rectifier par un second versement compensatoire."
                    ]
                },
                {
                    id: 'recouvrement',
                    title: 'Suivi des recouvrements, impayés et relances',
                    location: 'Comptabilité › Caisse et Rapports financiers',
                    href: '/caisse',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Dispositif de suivi des soldes débiteurs : identification des élèves dont une échéance est " +
                        "dépassée, classement par ancienneté de la créance, édition d'un avis de sommes dues et envoi de " +
                        "relances par SMS aux tuteurs. Les lots de relance sont préparés chaque nuit par l'application, " +
                        "mais demeurent à l'état de brouillon : aucun envoi de masse ne part sans un geste humain.",
                    objectif:
                        "Substituer à une réclamation improvisée un recouvrement méthodique, équitable et traçable, qui préserve " +
                        "la trésorerie de l'établissement sans exposer les familles à des relances injustifiées. Pour la " +
                        "direction, c'est la maîtrise du principal risque de trésorerie d'un établissement privé ; pour le " +
                        "service financier, c'est une liste de travail ordonnée par ancienneté de créance, à la place d'une " +
                        "chasse au retardataire menée de mémoire.",
                    probleme:
                        "Sans suivi structuré, les impayés ne se découvrent qu'au moment des bulletins ou des examens, " +
                        "c'est-à-dire trop tard pour être recouvrés sereinement. Les relances se font au jugé, certaines " +
                        "familles à jour sont importunées tandis que des retards anciens demeurent ignorés.",
                    procedure: [
                        "Consultez la liste des débiteurs : elle recense les inscriptions dont une échéance est échue et non réglée.",
                        "Examinez l'ancienneté de la créance : un retard de huit jours et un retard de trois mois n'appellent pas le même traitement.",
                        "AVIS DE SOMMES DUES : éditez le document PDF détaillant ce qui reste dû et remettez-le au tuteur.",
                        "RELANCE PAR SMS : ouvrez le lot de relance préparé pour la classe, vérifiez nominativement les destinataires, retirez les cas litigieux, puis déclenchez l'envoi.",
                        "L'envoi n'intervient qu'après cette validation explicite : aucune campagne ne part automatiquement.",
                        "ARRANGEMENT : si la famille sollicite un délai, formalisez un échéancier personnalisé plutôt qu'une tolérance verbale.",
                        "Suivez le taux de recouvrement dans Comptabilité › Rapports financiers, et exportez le rapport au format Excel pour le conseil d'administration."
                    ],
                    impacts: [
                        "Échéanciers : c'est la date d'exigibilité de chaque échéance qui déclenche l'entrée en liste des débiteurs.",
                        "Encaissement : tout versement retire aussitôt l'élève de la liste dès lors que son solde est apuré.",
                        "SMS : la relance suppose un numéro de tuteur valide et la formule d'abonnement autorisant les notifications par SMS.",
                        "Trésorerie : l'encours des impayés pèse directement sur les prévisions de trésorerie.",
                        "Direction : le taux de recouvrement figure parmi les indicateurs du tableau de bord."
                    ],
                    recommandations: [
                        "Relisez nominativement chaque lot avant envoi : une relance adressée à une famille à jour porte durablement atteinte à la relation.",
                        "Relancez tôt et posément plutôt que tard et brutalement : un retard de quinze jours se règle souvent d'un simple message.",
                        "N'exposez jamais publiquement la situation financière d'un élève, et ne l'écartez d'une activité pédagogique qu'après décision formelle de la direction.",
                        "Consignez tout arrangement dans un échéancier personnalisé : un accord verbal non enregistré n'engage personne et ne protège pas la famille.",
                        "Vérifiez la qualité des numéros de téléphone : un fichier de contacts défaillant réduit à néant l'efficacité du dispositif."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'pilotage',
            number: 7,
            title: 'Rapports, statistiques & audit',
            icon: 'shield',
            summary: "Les instruments de pilotage de la direction et la traçabilité des opérations quotidiennes.",
            concept:
                "Ce module ne produit aucune donnée : il RESTITUE celles que les six précédents ont saisies. D'où sa règle de " +
                "lecture, qui vaut avertissement : un indicateur ne vaut jamais mieux que les saisies quotidiennes qui " +
                "l'alimentent, et un chiffre surprenant trahit plus souvent une erreur d'écriture qu'un événement réel. On y " +
                "trouve trois familles d'instruments : les tableaux de bord, qui donnent la situation à l'instant présent ; " +
                "les rapports exportables, destinés au comptable et au conseil d'administration ; et le journal d'audit, qui " +
                "retrace l'auteur, la date et la valeur antérieure de toute opération sensible.",
            articles: [
                {
                    id: 'tableau-de-bord',
                    title: 'Tableaux de bord de la direction',
                    location: 'Tableau de bord et Rapports financiers',
                    href: '/tableau-de-bord',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Synthèse chiffrée de l'établissement à l'instant présent : effectifs par classe et par niveau, " +
                        "recettes encaissées, encours d'impayés, taux de recouvrement, assiduité et occupation des locaux. " +
                        "Les rapports financiers approfondissent cette vue et s'exportent au format Excel.",
                    objectif:
                        "Donner à la direction une lecture immédiate et fiable de la situation de son établissement, sans avoir à " +
                        "solliciter le secrétariat ni à consolider des tableaux à la main. Pour la direction, c'est l'instrument " +
                        "d'arbitrage : ouvrir une classe, recruter un enseignant, engager une dépense deviennent des décisions " +
                        "fondées sur une mesure datée plutôt que sur une impression, et justifiables devant un tiers.",
                    probleme:
                        "Les indicateurs reconstitués à la demande arrivent tardivement, ne concordent pas entre eux et " +
                        "reposent sur des périmètres implicites. Les décisions — ouverture d'une classe, recrutement, " +
                        "investissement — se prennent alors sur une impression plutôt que sur une mesure.",
                    procedure: [
                        "Ouvrez le Tableau de bord : les indicateurs portent sur l'année scolaire ACTIVE.",
                        "Consultez les effectifs par classe et par niveau, ainsi que leur évolution.",
                        "Examinez les indicateurs financiers : montants attendus, encaissés et restant dus.",
                        "Ouvrez Gestion Scolaire › Rapports pour le détail de l'assiduité par classe et par élève.",
                        "Ouvrez Comptabilité › Rapports financiers pour la consolidation des recettes.",
                        "Exportez le rapport financier au format Excel afin de le transmettre au comptable ou au conseil d'administration.",
                        "Ouvrez Paramètres › Journal d'audit pour retracer une opération sensible : son auteur, sa date et la valeur antérieure."
                    ],
                    impacts: [
                        "Périmètre : tous les indicateurs se rapportent à l'année scolaire active. Changer d'année change la lecture.",
                        "Qualité des données : un indicateur ne vaut que ce que valent les saisies quotidiennes qui l'alimentent.",
                        "Audit : toute opération sensible — note, paiement, révision de frais — est tracée avec son auteur et son horodatage.",
                        "Export Excel : le fichier produit est destiné au comptable de l'établissement et respecte la présentation attendue."
                    ],
                    recommandations: [
                        "Consultez le tableau de bord à jour fixe — chaque lundi, par exemple — plutôt qu'au gré des inquiétudes.",
                        "Un écart soudain sur un indicateur trahit plus souvent une erreur de saisie qu'un événement réel : vérifiez avant de décider.",
                        "Le journal d'audit ne sert pas à sanctionner mais à comprendre : il rétablit la chronologie exacte d'une opération contestée.",
                        "Exportez et archivez le rapport financier à chaque fin de trimestre : c'est la photographie de l'exercice à cette date."
                    ]
                },
                {
                    id: 'cloture-caisse',
                    title: 'Clôture de caisse et journée du secrétariat',
                    location: 'Comptabilité › Caisse (Encaissements)',
                    href: '/caisse',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Rituel comptable de fin de journée : la session de caisse ouverte le matin avec un fonds initial " +
                        "est clôturée le soir en déclarant le solde constaté. L'application édite alors le rapport de " +
                        "clôture, qui rapproche le total théorique des encaissements du numéraire réellement compté.",
                    objectif:
                        "Arrêter chaque journée sur un chiffre incontestable, détecter immédiatement tout écart de caisse, et " +
                        "fixer la responsabilité de chaque caissier sur sa propre session. Pour la direction, c'est la protection " +
                        "la plus élémentaire contre les fuites de caisse ; pour le caissier lui-même, c'est la garantie qu'un " +
                        "manquant survenu ailleurs ou un autre jour ne pourra jamais lui être imputé.",
                    probleme:
                        "Une caisse jamais arrêtée formellement rend tout écart indétectable : lorsqu'un manquant apparaît " +
                        "en fin de mois, il devient impossible d'en déterminer le jour, l'opération ou l'agent. Le soupçon " +
                        "se répand alors sur l'ensemble du service.",
                    procedure: [
                        "À l'ouverture du guichet, ouvrez votre session de caisse en déclarant le fonds de caisse initial.",
                        "Effectuez la journée d'encaissement : chaque versement est rattaché à cette session nominative.",
                        "En fin de journée, comptez physiquement le numéraire en caisse.",
                        "Ouvrez la clôture, déclarez le solde constaté, et confrontez-le au total théorique calculé par l'application.",
                        "Justifiez tout écart avant de valider : un écart accepté sans explication est un écart perdu.",
                        "Validez la clôture, puis éditez le rapport de clôture journalière au format PDF.",
                        "Faites contresigner le rapport par la direction selon l'usage de l'établissement, et classez-le."
                    ],
                    impacts: [
                        "Encaissements : aucun versement ne peut être enregistré hors d'une session de caisse ouverte.",
                        "Trésorerie : les recettes de la journée clôturée alimentent la position de trésorerie de l'établissement.",
                        "Rapports financiers : la consolidation des recettes repose sur les sessions clôturées.",
                        "Audit : l'ouverture et la clôture sont tracées avec leur auteur et leur horodatage.",
                        "Responsabilité : chaque session est nominative, ce qui circonscrit tout écart à un agent et à une journée."
                    ],
                    recommandations: [
                        "Clôturez chaque jour, sans exception : une session laissée ouverte plusieurs jours ruine l'intérêt du dispositif.",
                        "Comptez le numéraire AVANT de consulter le total théorique, afin de ne pas s'aligner inconsciemment sur le chiffre attendu.",
                        "N'encaissez jamais sous la session d'un collègue : la responsabilité en serait faussée.",
                        "Conservez les rapports de clôture : ils constituent la pièce justificative de la comptabilité de caisse.",
                        "Clôturez impérativement la caisse avant toute bascule d'année scolaire."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'examens',
            number: 8,
            title: 'Examens officiels — CFEE, BFEM, BAC',
            icon: 'document-text',
            summary: "Constitution et suivi des dossiers de candidature, de l'ouverture à la transmission à l'IEF/l'IA et à la saisie des résultats.",
            concept:
                "Une candidature aux examens officiels (CFEE en fin de CM2, BFEM en fin de troisième, BAC en terminale) " +
                "traverse un cycle de vie précis : le dossier s'ouvre INCOMPLET, ne devient COMPLET que lorsque l'extrait de " +
                "naissance est déclaré présent et l'état civil déclaré conforme, ne peut être TRANSMIS à l'Inspection de " +
                "l'Éducation et de la Formation (IEF) ou à l'Inspection d'Académie (IA) qu'une fois Complet, et ne reçoit un " +
                "résultat qu'une fois Transmis. Chaque étape verrouille la précédente : on ne transmet jamais un dossier dont " +
                "il manque une pièce, et on ne délibère jamais un dossier qui n'a pas été transmis. Le numéro de table, comme " +
                "le matricule d'un élève, n'est attribué qu'au moment précis où le centre d'examen est arrêté — jamais avant, " +
                "pour qu'aucune suite de numéros ne comporte de trou.",
            articles: [
                {
                    id: 'dossiers-candidature',
                    title: 'Ouverture des sessions et constitution des dossiers de candidature',
                    location: 'Gestion Scolaire › Examens officiels',
                    href: '/examens',
                    roles: ['Directeur', 'Secrétariat', 'Enseignant'],
                    definition:
                        "La SESSION est la campagne d'examen de l'établissement pour une année scolaire et un type d'examen " +
                        "donnés (CFEE, BFEM ou BAC, ce dernier décliné par série). Le DOSSIER, lui, est ouvert par élève, sur " +
                        "une session, avec la classe figée à l'ouverture. Un dossier porte l'état civil et le contrôle de " +
                        "conformité — extrait de naissance présent, état civil conforme — qui déterminent son statut : " +
                        "Incomplet tant qu'une pièce manque, Complet dès que tout est réuni.",
                    objectif:
                        "Constituer, avant la date limite fixée par l'administration, un dossier par candidat, contrôlé pièce " +
                        "par pièce, de sorte qu'aucune transmission ne soit jamais refusée par l'IEF ou l'IA pour un motif " +
                        "d'état civil. Pour le secrétariat, l'onglet Audit répond en un instant à la seule question qui " +
                        "compte avant l'échéance : quels dossiers, et pour quelle pièce précisément, restent à compléter.",
                    probleme:
                        "Une liste de candidats tenue sous tableur ne signale aucune pièce manquante avant le dépôt physique " +
                        "du dossier au guichet de l'IEF, où le refus se découvre trop tard pour être corrigé dans les délais. " +
                        "L'élève concerné se retrouve alors exclu de la session en cours.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Examens officiels, onglet Sessions, et créez la session : année scolaire, type d'examen (CFEE, BFEM, BAC) et, pour le BAC, la série.",
                        "Basculez vers l'onglet Dossiers et cliquez sur « Nouveau dossier » : recherchez l'élève par son matricule ou son nom, la classe se préremplit depuis sa classe actuelle.",
                        "Depuis la fiche du dossier, déclarez la présence de l'extrait de naissance et la conformité de l'état civil au regard des pièces reçues.",
                        "Le dossier passe automatiquement à Complet dès que ces deux contrôles sont positifs — aucune case à cocher séparément ne fait foi.",
                        "Avant la date limite, ouvrez l'onglet Audit, sélectionnez la session : la liste des dossiers encore Incomplets s'affiche, avec le détail exact de ce qui manque à chacun.",
                        "Une fois un dossier Complet, transmettez-le à l'IEF/l'IA depuis l'onglet Dossiers ; un dossier encore Incomplet voit ce bouton refuser l'opération et renvoie vers l'Audit."
                    ],
                    impacts: [
                        "Élèves : la classe d'un dossier est figée à son ouverture ; un transfert de classe ultérieur de l'élève ne la modifie jamais rétroactivement.",
                        "Fiches de candidature (PDF) : l'impression, individuelle ou par lot, exclut systématiquement tout dossier encore Incomplet.",
                        "Export ministériel : le fichier remis à l'IEF/l'IA ne porte que les dossiers effectivement transmis.",
                        "Enseignants : un professeur consulte, en lecture seule, les dossiers des seules classes où il a une affectation active sur l'année en cours — jamais ceux d'une autre classe, un dossier portant des données d'état civil sensibles."
                    ],
                    recommandations: [
                        "Ouvrez la session et les dossiers dès la publication du calendrier officiel : le contrôle des pièces prend du temps, la dernière semaine n'y suffit pas.",
                        "Consultez l'Audit chaque semaine à l'approche de l'échéance plutôt qu'une seule fois in extremis.",
                        "Ne déclarez jamais un état civil « conforme » sans avoir confronté le dossier scolaire à la pièce originale présentée par la famille.",
                        "La classe d'un dossier ne se corrige pas après coup : vérifiez-la au moment même de l'ouverture."
                    ]
                },
                {
                    id: 'attribution-convocations-resultats',
                    title: 'Attribution centre/table, convocations et résultats de délibération',
                    location: 'Gestion Scolaire › Examens officiels',
                    href: '/examens',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Une fois le dossier Complet, trois opérations distinctes suivent : l'attribution du centre d'examen " +
                        "et du numéro de table (généré automatiquement, comme un matricule, au moment même de l'attribution) ; " +
                        "l'édition et l'envoi de la convocation ; et, après la session, la saisie du résultat de délibération " +
                        "— admission, mention (sauf au CFEE, qui n'en attribue pas) et moyenne.",
                    objectif:
                        "Produire, sans ressaisie ni recopie, les documents qui engagent l'établissement vis-à-vis de l'IEF/l'IA " +
                        "et des familles — fiche de candidature, convocation, relevé ministériel — et consigner un résultat " +
                        "officiel exploitable pour les statistiques de réussite de l'établissement, comparées d'une année sur " +
                        "l'autre.",
                    probleme:
                        "Un numéro de table attribué à la main, avant que le centre ne soit définitivement arrêté, produit " +
                        "immanquablement des doublons ou des trous dans la numérotation lorsque l'affectation change en " +
                        "dernière minute — une anomalie que l'IEF/l'IA ne pardonne pas sur un relevé officiel.",
                    procedure: [
                        "Depuis l'onglet Dossiers, sur un dossier Complet ou déjà Transmis, ouvrez « Attribuer centre/table » : indiquez le centre — repris par défaut du centre de la session — et laissez le numéro de candidat VIDE pour une génération automatique.",
                        "Éditez la fiche de candidature individuelle en PDF, ou lancez une impression par lot filtrée par session ou par classe.",
                        "Une fois centre et numéro de table attribués sur l'ensemble d'une session, éditez ou envoyez les convocations : l'envoi par SMS/WhatsApp est réservé à la formule Premium, l'impression PDF individuelle reste libre pour toutes les formules.",
                        "Un centre/numéro de table manquant sur ne serait-ce qu'un dossier du lot bloque l'envoi groupé des convocations : l'application l'indique explicitement plutôt que d'envoyer un lot incomplet.",
                        "À l'issue de la délibération, saisissez pour chaque dossier transmis le résultat : admis ou non, mention (BFEM/BAC uniquement) et moyenne obtenue.",
                        "Exportez le relevé ministériel (Excel) de la session pour la transmission finale à l'IEF/l'IA.",
                        "Consultez l'onglet Statistiques pour le taux de réussite par série/classe et sa comparaison avec l'année scolaire précédente."
                    ],
                    impacts: [
                        "Numérotation : le numéro de table n'existe qu'à compter de l'attribution du centre — jamais avant, et jamais modifiable par une correction manuelle du dossier lui-même.",
                        "Convocations : le canal SMS/WhatsApp est le même que celui des autres notifications sortantes de l'établissement (relances, avis) — aucun crédit ni canal distinct.",
                        "Statistiques : seuls les dossiers Transmis ou Validés entrent dans le calcul du taux de réussite ; un dossier encore en préparation ne peut jamais fausser un taux affiché en cours d'année.",
                        "Audit : toute génération d'export ministériel, impression par lot ou envoi de convocations est journalisée."
                    ],
                    recommandations: [
                        "N'attribuez le centre et le numéro de table qu'une fois la répartition définitivement arrêtée par l'IEF/l'IA : une réattribution ultérieure reste possible mais alourdit inutilement le suivi.",
                        "Contrôlez un exemplaire de fiche de candidature imprimée avant de lancer l'impression par lot d'une session entière.",
                        "Vérifiez le solde de crédits SMS avant un envoi groupé de convocations à une session nombreuse.",
                        "Saisissez les résultats dès la délibération plutôt qu'en différé : c'est la même discipline que pour les notes de classe, et elle évite toute confusion entre deux sessions successives d'un même type d'examen."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'rh-paie',
            number: 9,
            title: 'Ressources Humaines & Paie',
            icon: 'payment',
            summary: "Contrats du personnel, pointage des heures des vacataires, fiches de paie et déclarations fiscales mensuelles.",
            concept:
                "Le personnel de l'établissement — enseignants et agents administratifs — relève de deux régimes de " +
                "rémunération distincts. Le PERMANENT perçoit un salaire de base fixe, mensuel, indépendant du nombre " +
                "d'heures effectuées. Le VACATAIRE est rémunéré au TAUX HORAIRE, ce qui suppose un pointage préalable des " +
                "heures effectivement assurées : sans heures pointées, aucune fiche de paie cohérente ne peut être générée " +
                "pour lui. La fiche de paie, une fois éditée, alimente à son tour la déclaration fiscale mensuelle — la " +
                "consolidation des charges sociales dues à l'État (IPRES, CSS, VRS, BRS) sur l'ensemble du personnel.",
            articles: [
                {
                    id: 'contrats-personnel',
                    title: 'Contrats du personnel — Permanent et Vacataire',
                    location: 'Comptabilité › Paie',
                    href: '/paie',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Le contrat rattache un employé — enseignant ou compte utilisateur non-enseignant — à un régime de " +
                        "rémunération : Permanent (salaire de base fixe) ou Vacataire (taux horaire). Une prime de transport " +
                        "facultative peut s'ajouter à l'un comme à l'autre. Le contrat est ACTIF jusqu'à sa clôture, qui " +
                        "n'efface jamais rien : un contrat clôturé demeure consultable, mais ne peut plus produire de " +
                        "nouvelle fiche de paie.",
                    objectif:
                        "Fixer, pour chaque membre du personnel, le régime et le montant qui serviront de base à chaque fiche " +
                        "de paie, sans avoir à les ressaisir chaque mois. Pour la direction, c'est l'unique référence " +
                        "salariale de l'établissement, opposable en cas de litige et alignée sur ce qui figure réellement " +
                        "sur chaque bulletin remis.",
                    probleme:
                        "Un salaire négocié verbalement et jamais consigné se traduit, en fin de mois, par une fiche de " +
                        "paie approximative ou par un désaccord entre l'employé et l'établissement sur le montant convenu.",
                    procedure: [
                        "Ouvrez Comptabilité › Paie, onglet Contrats, puis « Nouveau contrat ».",
                        "Choisissez le type d'employé : un enseignant déjà fiché, ou un utilisateur non-enseignant (personnel administratif).",
                        "Sélectionnez le régime — Permanent ou Vacataire — puis renseignez le salaire de base ou le taux horaire selon le cas, et la prime de transport le cas échéant.",
                        "Enregistrez : le contrat devient immédiatement sélectionnable pour la génération d'une fiche de paie.",
                        "RÉVISION : depuis la ligne du contrat, modifiez le salaire, le taux horaire ou la prime, en indiquant obligatoirement le motif — une augmentation annuelle, par exemple.",
                        "CLÔTURE : en cas de départ, clôturez le contrat en précisant la date et le motif. Le contrat n'est jamais supprimé, seulement fermé à toute nouvelle fiche.",
                        "ATTESTATION DE TRAVAIL : téléchargez à tout moment le PDF de l'attestation, depuis la ligne du contrat concerné."
                    ],
                    impacts: [
                        "Fiches de paie : seul un contrat ACTIF (non clôturé) peut servir de base à une nouvelle fiche.",
                        "Heures des vacataires : le régime Vacataire conditionne l'accès au pointage des heures.",
                        "Déclarations fiscales : les charges sociales consolidées procèdent des fiches de paie, elles-mêmes adossées aux contrats.",
                        "Historique : toute révision de salaire ou de taux horaire est conservée avec son auteur, sa date et son motif."
                    ],
                    recommandations: [
                        "Enregistrez le contrat dès l'embauche, avant toute première fiche de paie : une fiche ne peut jamais précéder son contrat.",
                        "Motivez systématiquement une révision salariale : le motif est ce qui rend l'historique compréhensible des mois plus tard.",
                        "Clôturez sans délai le contrat d'un employé qui quitte l'établissement, pour prévenir toute fiche de paie émise par erreur.",
                        "Ne confondez jamais Permanent et Vacataire à la création : le régime choisi détermine les champs attendus lors de la génération de la fiche."
                    ]
                },
                {
                    id: 'heures-vacataires-fiches-paie',
                    title: 'Pointage des heures et génération des fiches de paie',
                    location: 'Comptabilité › Paie › Pointage Profs',
                    href: '/pointage-profs',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Pour un contrat Vacataire, chaque heure effectuée se pointe individuellement — date, nombre " +
                        "d'heures, note facultative — AVANT de pouvoir être reprise dans une fiche de paie. La FICHE DE " +
                        "PAIE, elle, se génère par contrat, par mois et par année ; elle calcule le salaire brut et le net à " +
                        "payer, puis se restitue en bulletin PDF, à l'identique quel que soit le régime du contrat.",
                    objectif:
                        "Garantir que le montant versé à un vacataire correspond exactement aux heures qu'il a réellement " +
                        "assurées, consignées au fil de l'eau plutôt que reconstituées de mémoire en fin de mois. Pour la " +
                        "Finance, c'est l'assurance de ne jamais rémunérer une heure non prouvée, et pour le vacataire, la " +
                        "garantie qu'aucune heure effectuée ne soit oubliée.",
                    probleme:
                        "Sans pointage horodaté, le nombre d'heures d'un vacataire se négocie de mémoire en fin de mois, " +
                        "au désavantage de l'un ou de l'autre selon les cas, et sans aucune pièce pour trancher un " +
                        "désaccord.",
                    procedure: [
                        "Ouvrez Comptabilité › Paie › Pointage Profs, ou la fiche « heures » accessible directement depuis un contrat Vacataire dans l'onglet Contrats.",
                        "Sélectionnez le contrat concerné, puis ajoutez chaque heure : date, nombre d'heures (les demi-heures sont admises) et une note facultative.",
                        "Consultez, filtré par mois et par année, le récapitulatif des heures déjà pointées pour ce contrat.",
                        "Téléchargez la fiche d'heures au format PDF si une pièce signée est requise.",
                        "Ouvrez ensuite l'onglet Fiches de paie et cliquez sur « Générer une fiche » : choisissez le contrat, le mois et l'année.",
                        "Pour un contrat Vacataire, reportez le total d'heures pointées sur le mois dans le champ « Heures travaillées » ; pour un Permanent, ce champ reste sans objet.",
                        "Validez : la fiche calcule le brut et le net, puis devient immédiatement imprimable en bulletin PDF."
                    ],
                    impacts: [
                        "Salaire du vacataire : le brut de sa fiche se calcule directement du produit des heures saisies par le taux horaire de son contrat.",
                        "Déclarations fiscales : chaque fiche générée entre dans l'agrégat des charges sociales du mois correspondant.",
                        "Historique : les heures pointées restent consultables mois par mois, indépendamment des fiches déjà générées.",
                        "Bulletin PDF : le document remis à l'employé restitue exactement les montants calculés, sans reprise manuelle."
                    ],
                    recommandations: [
                        "Pointez les heures au fil de l'eau, jour après jour, plutôt qu'en une seule saisie de fin de mois sujette à l'oubli.",
                        "Contrôlez le total des heures pointées AVANT de générer la fiche de paie : une fiche déjà éditée et remise ne se corrige pas en silence.",
                        "Faites viser la fiche d'heures par le vacataire lorsque l'usage de l'établissement le prévoit.",
                        "Une fiche de paie erronée ne se réédite pas à la légère : vérifiez le mois, l'année et le contrat sélectionnés avant de valider."
                    ]
                },
                {
                    id: 'declarations-fiscales',
                    title: 'Déclarations fiscales mensuelles — IPRES, CSS, VRS, BRS',
                    location: 'Comptabilité › Paie',
                    href: '/paie',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "La déclaration fiscale consolide, pour un mois donné, les charges sociales et fiscales dues à " +
                        "l'État sur l'ensemble des fiches de paie déjà générées : cotisations de retraite (IPRES), de " +
                        "sécurité sociale (CSS), et les prélèvements VRS et BRS. Elle en totalise le montant global dû à " +
                        "l'État pour ce mois.",
                    objectif:
                        "Éviter à l'établissement de reconstituer, fiche par fiche, ce qu'il doit verser à l'État chaque mois, " +
                        "et lui fournir un document unique, daté et vérifiable, à remettre en appui de sa déclaration " +
                        "administrative. Pour la direction, c'est la garantie qu'aucune charge sociale n'est omise ni " +
                        "comptée deux fois.",
                    probleme:
                        "Recalculer chaque mois, à la main, les charges sociales dues sur l'ensemble du personnel expose à " +
                        "l'oubli d'un contrat, à une erreur d'addition, et in fine à un retard ou à une insuffisance de " +
                        "déclaration devant l'administration fiscale.",
                    procedure: [
                        "Assurez-vous d'abord que TOUTES les fiches de paie du mois concerné sont générées : la déclaration n'agrège que ce qui existe déjà.",
                        "Ouvrez Comptabilité › Paie, onglet Déclarations fiscales.",
                        "Cliquez sur « Générer une déclaration », renseignez le mois et l'année, puis validez.",
                        "Consultez le détail par poste — IPRES, CSS, VRS, BRS — et le total dû à l'État qui en résulte.",
                        "Conservez ou transmettez ce récapitulatif pour la déclaration administrative effective auprès des organismes concernés."
                    ],
                    impacts: [
                        "Fiches de paie : une fiche générée APRÈS la déclaration du mois n'y figure pas — régénérez la déclaration si une fiche a été ajoutée en retard.",
                        "Contrats : un contrat clôturé en cours de mois continue de peser sur la déclaration via les fiches déjà émises pour lui.",
                        "Rapports financiers : les charges sociales consolidées éclairent la charge salariale totale de l'établissement."
                    ],
                    recommandations: [
                        "Générez la déclaration en tout dernier, une fois certain qu'aucune fiche de paie du mois ne reste à éditer.",
                        "Conservez une déclaration par mois, même après transmission à l'administration : c'est la pièce justificative de ce qui a été réellement versé.",
                        "En cas de fiche de paie ajoutée après coup, régénérez la déclaration plutôt que de corriger le total à la main."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'resilience-reseau',
            number: 10,
            title: 'Résilience réseau — travailler sur une connexion instable',
            icon: 'globe',
            summary: "Ce que l'application fait, et ne fait pas, quand la connexion se coupe pendant une saisie — badge de connectivité, brouillons et reprise automatique.",
            concept:
                "Unikol est une application 100 % en ligne : aucune donnée n'est jamais enregistrée localement à titre " +
                "définitif, il n'existe ni mode hors ligne ni file d'attente de synchronisation entre plusieurs postes. Ce " +
                "choix délibéré (Volume 0 §0.13) élimine toute une classe de conflits — deux postes qui auraient chacun " +
                "enregistré la même opération pendant une coupure. Ce que l'application offre à la place, c'est une " +
                "RÉSILIENCE COURTE : elle détecte la coupure, en informe l'utilisateur sans l'alarmer inutilement, " +
                "conserve sa saisie EN MÉMOIRE LOCALE le temps qu'il la termine, et, sur deux écrans précis, retente " +
                "elle-même l'enregistrement en arrière-plan tant que l'onglet reste ouvert.",
            articles: [
                {
                    id: 'etat-connexion-brouillons',
                    title: 'Bandeau de connexion, badge de connectivité et brouillons de formulaire',
                    location: 'Barre supérieure — présente sur toutes les pages',
                    href: '/tableau-de-bord',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "Trois indicateurs se distinguent, et non un simple point vert/rouge : CONNECTÉ (le serveur a " +
                        "répondu), VÉRIFICATION EN COURS (état transitoire, quelques centaines de millisecondes), et HORS " +
                        "LIGNE (coupure confirmée). Un BROUILLON, lui, est la saisie d'un formulaire en cours — pas encore " +
                        "envoyée — conservée sur CE poste, dans le navigateur, jamais sur le serveur.",
                    objectif:
                        "Dire honnêtement à l'utilisateur ce qui est enregistré et ce qui ne l'est pas encore, sans jamais " +
                        "lui laisser croire qu'une saisie est sauvegardée alors qu'elle ne vit que dans son navigateur. Pour " +
                        "un poste sur une connexion mobile instable, c'est la différence entre une coupure gérée sereinement " +
                        "et une ressaisie complète par méfiance.",
                    probleme:
                        "Une coupure réseau silencieuse, sans indicateur ni message, laisse l'utilisateur découvrir bien " +
                        "plus tard — parfois après avoir fermé l'onglet — qu'une saisie entière n'a jamais atteint le " +
                        "serveur.",
                    procedure: [
                        "Observez le badge de la barre supérieure : « Connecté », « Vérification… » ou « Hors ligne — n saisie(s) conservée(s) ».",
                        "En cas de coupure confirmée, un bandeau plein cadre apparaît ; il disparaît dès que la connexion est vérifiée comme rétablie, avec une confirmation brève.",
                        "Cliquez sur le badge pour forcer une nouvelle vérification plutôt que d'attendre le retour automatique de la connexion.",
                        "Pendant une coupure, certains formulaires (caisse, entre autres) conservent votre saisie en mémoire locale : à la reconnexion, un bandeau propose de la restaurer.",
                        "Un brouillon disparaît de lui-même après 24 heures, ou dès que la saisie correspondante est effectivement validée.",
                        "Ne comptez jamais sur un brouillon au-delà de la session en cours : fermer l'onglet avant validation reste une perte de saisie assumée."
                    ],
                    impacts: [
                        "Aucune donnée métier : un brouillon ne modifie jamais rien côté serveur ; il ne fait que restaurer les CHAMPS d'un formulaire.",
                        "Multi-établissement : les brouillons d'un compte rattaché à plusieurs écoles sont cloisonnés par établissement, un brouillon de l'école A ne réapparaît jamais dans l'école B.",
                        "Compteur du badge : il reflète exactement le nombre de brouillons non expirés conservés sur ce poste, pas une file d'écritures en attente d'envoi."
                    ],
                    recommandations: [
                        "Ne considérez jamais un brouillon comme un enregistrement : tant que le bandeau de confirmation n'est pas apparu, rien n'est acquis côté serveur.",
                        "Sur une connexion notoirement instable, terminez une saisie en une seule session plutôt que de compter sur le brouillon d'un jour à l'autre.",
                        "Le badge « Vérification… » n'est pas une alerte : c'est un état normal de quelques centaines de millisecondes à chaque sonde."
                    ]
                },
                {
                    id: 'reprise-automatique-caisse-appel',
                    title: 'Reprise automatique après coupure — Caisse et Appel en classe',
                    location: 'Comptabilité › Caisse et Gestion Scolaire › Appel en classe',
                    href: '/caisse',
                    roles: ['Directeur', 'Finance', 'Secrétariat', 'Enseignant', 'Surveillant'],
                    definition:
                        "Sur deux écrans précis — l'encaissement en caisse et l'appel en classe —, une coupure réseau " +
                        "survenant PENDANT l'envoi ne se traduit plus par un échec immédiat : l'application retente elle-même " +
                        "l'enregistrement, avec un délai croissant, tant que l'onglet reste ouvert, et affiche « en attente " +
                        "d'envoi » le temps de la reprise. La sécurité tient à un principe strict : l'application ne " +
                        "RETENTE JAMAIS une réponse déjà reçue du serveur, succès ou erreur métier — seule l'ABSENCE de " +
                        "réponse (coupure, DNS, délai dépassé) déclenche une nouvelle tentative.",
                    objectif:
                        "Épargner à la caissière ou à l'enseignant une ressaisie manuelle pour la coupure la plus fréquente " +
                        "en contexte sénégalais — le micro-basculement d'antenne de quelques secondes —, sans jamais courir " +
                        "le risque d'un double encaissement ou d'un doublon de feuille d'appel.",
                    probleme:
                        "Sans mécanisme de reprise, une coupure de trois secondes pendant la validation d'un encaissement " +
                        "obligeait à tout ressaisir, avec le risque réel qu'un second essai, si le premier avait en fait " +
                        "abouti côté serveur, produise un double paiement ou une double feuille de présence.",
                    procedure: [
                        "Validez l'encaissement ou l'appel normalement : rien ne change tant que le réseau répond.",
                        "Si une coupure survient PENDANT l'envoi, le bouton affiche « En attente d'envoi… » et un bandeau ambré confirme qu'une nouvelle tentative est en cours.",
                        "Ne cliquez pas une seconde fois et ne rechargez pas la page : l'application rejoue la même opération, avec la même clé, jusqu'à cinq tentatives sur environ trente secondes.",
                        "Dès que le réseau répond, l'opération se conclut normalement — pour la caisse, le reçu s'affiche ; pour l'appel, la fiche est marquée enregistrée.",
                        "Si toutes les tentatives échouent (coupure prolongée), l'application vous en informe explicitement : rien n'a été enregistré, et la reprise redevient manuelle.",
                        "Pour l'appel en classe spécifiquement, si la feuille apparaît déjà enregistrée après une coupure, c'est que la première tentative a bien abouti — sa réponse s'était simplement perdue en route ; ce n'est jamais traité comme une erreur à l'écran."
                    ],
                    impacts: [
                        "Caisse : un retry rejoue le résultat déjà produit par la tentative précédente (même reçu, même numéro) au lieu de créer un second paiement.",
                        "Appel en classe : la contrainte d'unicité de la feuille (classe, matière, date, créneau) rend un doublon structurellement impossible, retry ou non.",
                        "Aucune persistance au-delà de l'onglet : si l'onglet se ferme avant confirmation, la tentative en cours est perdue — c'est une limite assumée, pas un incident.",
                        "Une réponse métier (montant refusé, solde insuffisant, appel déjà saisi par un collègue) n'est JAMAIS rejouée : elle remonte immédiatement, comme sans coupure."
                    ],
                    recommandations: [
                        "Laissez l'onglet ouvert et patientez pendant un « en attente d'envoi » plutôt que de recharger la page ou de retenter la saisie vous-même.",
                        "Un échec après plusieurs tentatives (coupure prolongée) signifie que rien n'est enregistré : reprenez alors la saisie normalement, ce n'est plus automatique.",
                        "Ce mécanisme ne couvre aujourd'hui que la caisse et l'appel en classe : les autres écrans continuent d'exiger une reprise manuelle en cas de coupure pendant l'envoi."
                    ]
                }
            ]
        }
    ];

    // Les six rubriques du squelette pédagogique, dans l'ordre d'affichage. La clé correspond au champ
    // de l'article ; l'icône provient du sprite partagé (_IconSprite.cshtml) ; `kind` dit à la vue
    // comment rendre le contenu — un paragraphe, des étapes numérotées, ou une liste à puces.
    //
    // `tone` porte une TEINTE, pas un statut : les six rubriques ne sont ni des succès ni des
    // erreurs, ce sont six angles de lecture qu'il s'agit de distinguer d'un coup d'œil quand un
    // tiroir déplié occupe tout l'écran. Six teintes franchement séparées sur la roue chromatique,
    // choisies pour leur sens : bleu pour ce qui informe, vert pour le but atteint, rose pour la
    // douleur qu'on supprime, violet pour le mode opératoire, cyan pour les ondes de propagation,
    // ambre pour la mise en garde. La traduction en classes vit dans la vue, seul endroit qui
    // connaisse le vocabulaire Tailwind (Views/Help/Index.cshtml).
    const RUBRICS = [
        { key: 'definition', label: 'Définition & Concept', icon: 'book', tone: 'blue', kind: 'text' },
        { key: 'objectif', label: 'Objectif & Utilité', icon: 'target', tone: 'emerald', kind: 'text' },
        { key: 'probleme', label: 'Problème résolu', icon: 'lightbulb', tone: 'rose', kind: 'text' },
        { key: 'procedure', label: 'Procédure étape par étape', icon: 'document-text', tone: 'violet', kind: 'steps' },
        { key: 'impacts', label: 'Impacts & Interconnexions', icon: 'link', tone: 'cyan', kind: 'bullets' },
        { key: 'recommandations', label: 'Recommandations & Bonnes pratiques', icon: 'shield', tone: 'amber', kind: 'bullets' }
    ];

    /**
     * Aplatit les six rubriques d'un article en blocs prêts à rendre.
     *
     * La vue itère ces blocs et lit `block.text` / `block.items` DIRECTEMENT — elle n'appelle plus
     * aucune méthode du composant pour aller chercher le contenu. C'est une correction de bug, pas
     * une préférence de style : la vue appelait auparavant `valueOf(article, rubric)`, et le nom
     * `valueOf` existe sur Object.prototype. Or le proxy de portée d'Alpine résout un identifiant
     * ainsi :
     *
     *     get({objects}, name, receiver) {
     *         return Reflect.get(objects.find(o => Reflect.has(o, name)) || {}, name, receiver)
     *     }
     *
     * `Reflect.has` remonte la chaîne de prototypes. Dans un <template x-for>, le premier objet de
     * la pile est la portée de boucle ({ article: … }) : `Reflect.has(portée, 'valueOf')` est donc
     * VRAI par héritage, et c'est Object.prototype.valueOf qui était appelée — laquelle renvoie
     * l'objet lui-même, affiché « [object Object] » par x-text. Renommer la méthode aurait suffi à
     * masquer le symptôme ; ne plus appeler de méthode du tout supprime la classe de bug entière.
     * Voir tests/js/help.test.mjs, qui interdit désormais tout nom hérité d'Object.prototype.
     */
    const blocksOf = article =>
        RUBRICS.map(rubric => {
            const value = article[rubric.key];
            return {
                key: rubric.key,
                label: rubric.label,
                icon: rubric.icon,
                tone: rubric.tone,
                kind: rubric.kind,
                text: Array.isArray(value) ? '' : value,
                items: Array.isArray(value) ? value : []
            };
        });

    // Recherche insensible à la casse ET aux accents : « echeancier » doit trouver « échéancier »,
    // sans quoi la barre de recherche punit l'utilisateur pressé qui ne tape pas les diacritiques.
    const normalize = value =>
        (value || '')
            .toString()
            .toLowerCase()
            .normalize('NFD')
            .replace(/[\u0300-\u036f]/g, '');

    // Texte intégral d'un article, aplati une seule fois au démarrage : c'est sur cette chaîne que
    // porte la recherche, jamais sur le DOM. Filtrer le DOM obligerait à tout rendre puis à le
    // masquer, et surtout la recherche ne verrait pas ce qui est replié dans un tiroir fermé.
    const haystackOf = (section, article) =>
        normalize([
            section.title,
            // Le concept du MODULE est indexé sur chacune de ses fiches : une recherche sur
            // « exercice » ou « affectation » doit ramener le pôle qui les explique, pas seulement
            // les fiches où le mot réapparaît par hasard.
            section.concept,
            article.title,
            article.location,
            (article.roles || []).join(' '),
            article.definition,
            article.objectif,
            article.probleme,
            (article.procedure || []).join(' '),
            (article.impacts || []).join(' '),
            (article.recommandations || []).join(' ')
        ].join(' '));

    document.addEventListener('alpine:init', () => {
        Alpine.data('helpCenter', () => ({
            // Blocs et index de recherche calculés UNE FOIS, au démarrage : la vue n'a plus qu'à
            // lire des propriétés, sans appeler la moindre méthode du composant (voir blocksOf).
            sections: HELP_SECTIONS.map(section => ({
                ...section,
                articles: section.articles.map(article => ({
                    ...article,
                    blocks: blocksOf(article),
                    haystack: haystackOf(section, article)
                }))
            })),

            search: '',
            // Tiroirs ouverts. Le mode « un seul à la fois » (par défaut) garde la page épurée ;
            // l'utilisateur peut basculer en dépliage multiple pour comparer deux modules.
            openIds: [],
            allowMultiple: false,

            get query() {
                return normalize(this.search.trim());
            },

            get isSearching() {
                return this.query.length > 0;
            },

            /** Articles d'une section retenus par la recherche courante. */
            matchingArticles(section) {
                if (!this.isSearching) return section.articles;
                const q = this.query;
                return section.articles.filter(article => article.haystack.includes(q));
            },

            /** Une section disparaît entièrement quand aucun de ses articles ne correspond. */
            visibleSections() {
                return this.sections.filter(section => this.matchingArticles(section).length > 0);
            },

            get resultCount() {
                return this.visibleSections().reduce((total, section) => total + this.matchingArticles(section).length, 0);
            },

            get totalCount() {
                return this.sections.reduce((total, section) => total + section.articles.length, 0);
            },

            isOpen(articleId) {
                return this.openIds.includes(articleId);
            },

            toggle(articleId) {
                if (this.isOpen(articleId)) {
                    this.openIds = this.openIds.filter(id => id !== articleId);
                    return;
                }
                this.openIds = this.allowMultiple ? [...this.openIds, articleId] : [articleId];
            },

            /** Bascule « un seul tiroir » ⇄ « plusieurs tiroirs » : on ne garde alors que le dernier ouvert. */
            toggleMultiple() {
                this.allowMultiple = !this.allowMultiple;
                if (!this.allowMultiple && this.openIds.length > 1) {
                    this.openIds = [this.openIds[this.openIds.length - 1]];
                }
            },

            expandAll() {
                this.allowMultiple = true;
                this.openIds = this.visibleSections().flatMap(section =>
                    this.matchingArticles(section).map(article => article.id)
                );
            },

            collapseAll() {
                this.openIds = [];
            },

            clearSearch() {
                this.search = '';
                this.openIds = [];
            },

            /** Une seule réponse : on l'ouvre d'emblée, l'utilisateur a déjà désigné ce qu'il cherchait. */
            onSearchInput() {
                if (!this.isSearching) {
                    this.openIds = [];
                    return;
                }
                const results = this.visibleSections().flatMap(section => this.matchingArticles(section));
                this.openIds = results.length === 1 ? [results[0].id] : [];
            }
        }));

        /**
         * Bouton « Haut de page » du Centre d'aide.
         *
         * Le piège tient en une ligne : dans cette application, ce n'est PAS la fenêtre qui défile.
         * _Layout.cshtml pose `<body class="h-screen overflow-hidden">` et confie le défilement à
         * `<main class="overflow-y-auto">`. `window.scrollY` vaut donc 0 en permanence, quel que soit
         * l'endroit où l'on se trouve dans la page, et `window.scrollTo(0, 0)` n'a aucun effet
         * observable. C'est sur `<main>` qu'il faut écouter, et c'est `<main>` qu'il faut ramener.
         *
         * Seuil à 400px plutôt qu'au premier pixel : le bouton ne doit apparaître qu'une fois le
         * champ de recherche réellement hors de vue, sans quoi il propose de remonter là où l'on est.
         */
        Alpine.data('backToTop', () => ({
            visible: false,
            scroller: null,
            handler: null,

            init() {
                this.scroller = this.$el.closest('main') || document.querySelector('main');
                if (!this.scroller) return;

                this.handler = () => {
                    const next = this.scroller.scrollTop > 400;
                    // Affectation seulement au CHANGEMENT : un écouteur de défilement se déclenche
                    // des dizaines de fois par seconde, et écrire la même valeur dans un proxy
                    // Alpine réveille malgré tout ses effets.
                    if (next !== this.visible) this.visible = next;
                };

                this.scroller.addEventListener('scroll', this.handler, { passive: true });
                this.handler();
            },

            destroy() {
                if (this.scroller && this.handler) {
                    this.scroller.removeEventListener('scroll', this.handler);
                }
            },

            toTop() {
                if (!this.scroller) return;

                // Un défilement animé de plusieurs dizaines d'écrans est précisément ce que la
                // préférence « animations réduites » cherche à éviter : on saute alors directement.
                const reduced = typeof window.matchMedia === 'function'
                    && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

                this.scroller.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' });
            }
        }));
    });
})();
