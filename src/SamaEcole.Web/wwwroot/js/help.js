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
                    id: 'assistant-premier-parametrage',
                    title: 'Assistant de premier paramétrage express',
                    location: 'Barre supérieure — bouton dédié, sur tout écran',
                    href: '/parametres',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Un parcours guidé en six étapes — année scolaire, configuration pédagogique, " +
                        "grille tarifaire, fiches enseignants, première inscription, paramètres SIMEN " +
                        "— accessible depuis un bouton dédié de la barre supérieure, distinct du Centre " +
                        "d'aide. L'assistant ne configure RIEN lui-même : chaque étape renvoie vers " +
                        "l'écran déjà en place et se contente de LIRE l'état réel de l'établissement, " +
                        "via les mêmes routes et les mêmes gardes que ces écrans, pour cocher ce qui " +
                        "est fait.",
                    objectif:
                        "Répondre à une seule question au tout premier accès — par quoi commencer, et " +
                        "dans quel ordre — pour qu'un Directeur ne tente pas d'inscrire un élève avant " +
                        "d'avoir créé ses classes, ni de saisir une note avant d'avoir déclaré ses " +
                        "matières. Pour la direction, c'est la garantie qu'aucune étape fondatrice n'est " +
                        "oubliée par simple méconnaissance de l'application.",
                    probleme:
                        "Un établissement qui découvre Unikol sans guide tâtonne : il ouvre l'écran des " +
                        "élèves en premier, découvre qu'aucune classe n'existe, crée une classe, " +
                        "découvre qu'aucun barème n'est défini, et ainsi de suite — une découverte par " +
                        "l'échec, module après module, alors que l'ordre à suivre est connu d'avance et " +
                        "toujours le même.",
                    procedure: [
                        "Le bouton de l'assistant s'affiche sur TOUTE la barre supérieure, quel que soit l'écran ouvert — inutile de naviguer vers une adresse particulière.",
                        "À la toute première connexion d'un compte Directeur ou Secrétariat, le parcours s'ouvre automatiquement, une seule fois par navigateur.",
                        "Chaque étape affiche pourquoi elle importe et propose un raccourci direct vers l'écran correspondant : « Activer l'année scolaire », « Niveaux & classes », « Configurer les frais »…",
                        "Une étape n'est déverrouillée que si les précédentes, quand elles s'appliquent, sont déjà faites : le parcours impose l'ordre qui évite les blocages en cascade.",
                        "Traitez l'étape depuis son propre écran, puis revenez à l'assistant et cliquez sur « Revérifier » : son état se recalcule depuis la base, jamais par une simple coche manuelle.",
                        "La grille tarifaire se marque automatiquement « Non applicable » pour un établissement public : elle disparaît du calcul d'avancement plutôt que de rester indéfiniment « à faire »."
                    ],
                    impacts: [
                        "Toutes les étapes renvoient vers des écrans déjà documentés ailleurs dans ce guide — l'assistant n'introduit aucun nouvel écran, il n'en est que la porte d'entrée ordonnée.",
                        "Une route interrogée qui échoue (réseau, droit insuffisant) laisse simplement son étape à « à faire » : l'assistant ne coche jamais par excès de confiance.",
                        "Le pourcentage d'avancement affiché exclut les étapes non applicables, pour ne jamais laisser croire à un établissement public qu'il lui manque une grille tarifaire qui ne le concerne pas."
                    ],
                    recommandations: [
                        "Suivez l'ordre proposé même si l'envie est de foncer directement sur les élèves : chaque étape verrouillée l'est pour une raison réelle, pas par excès de prudence.",
                        "Rouvrez l'assistant à tout moment via son bouton — il ne s'ouvre automatiquement qu'une fois, mais reste accessible en permanence pour vérifier ce qu'il reste à faire.",
                        "Un compte Finance, Enseignant ou Surveillant ne voit jamais ce bouton : ce n'est pas une restriction à signaler, ces rôles n'ont rien à paramétrer au démarrage de l'établissement."
                    ]
                },
                {
                    id: 'panneau-parametres',
                    title: 'Le panneau Paramètres — ses onze sections',
                    location: 'Paramètres',
                    href: '/parametres',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "L'écran Paramètres regroupe tout ce que l'établissement configure une fois pour toutes. Une barre " +
                        "d'onglets horizontale défilante en tête donne accès à onze sections : Profil de l'établissement ; " +
                        "Intégration étatique (SIMEN) ; Formats & signatures officielles ; Années scolaires ; Notation & " +
                        "mentions ; Mensualités & autorisations de caisse ; Facturation & historique ; Paramètres système ; " +
                        "Utilisateurs & rôles ; Journal d'audit ; Notifications SMS. L'onglet visible est rappelé dans " +
                        "l'adresse (?tab=…), ce qui rend chaque section partageable par un lien direct, sans rechargement.",
                    objectif:
                        "Rassembler en un seul écran des réglages qui, dispersés, seraient introuvables — et n'exposer chaque " +
                        "section qu'aux rôles concernés. Pour la direction, c'est le tableau de commande de l'établissement : " +
                        "identité imprimée sur les documents, seuils de mention, mensualités, délégations de droits, sécurité de " +
                        "session, et journal de tout ce qui a été modifié.",
                    probleme:
                        "Quand les réglages sont éparpillés — un dans l'écran des notes, un autre dans celui des frais, un " +
                        "troisième nulle part —, personne ne sait plus où se trouve celui qu'il cherche, ni qui a le droit d'y " +
                        "toucher. Les délégations de droits, en particulier, se donnent alors verbalement et ne se retrouvent " +
                        "jamais.",
                    procedure: [
                        "Profil de l'établissement : nom, adresse, ville, logo, mentions légales, description, et publication dans l'annuaire public.",
                        "Intégration étatique (SIMEN) : nom de l'établissement sur le bulletin, code établissement national, coordonnées GPS, rattachement IA/IEF — indispensables aux exports Planète et STATEDUC.",
                        "Formats & signatures officielles : gabarits de matricule, signatures numérisées (Directeur, Secrétariat, Caissier, Surveillant Général) et cachet officiel apposés sur les PDF.",
                        "Années scolaires : création, activation et clôture des exercices, découpage en trimestres (fiche dédiée « Ouverture et clôture d'une année académique »).",
                        "Notation & mentions : rappel du barème automatique par cycle, seuils de mention du bulletin, et délégation « le Secrétariat gère la configuration des notes ».",
                        "Mensualités & autorisations de caisse : nombre de mensualités par an, et deux interrupteurs réservés au Directeur — « la Finance peut modifier les montants de frais », « la Finance peut supprimer des frais ».",
                        "Facturation & historique : formule d'abonnement Unikol de l'établissement et historique des paiements d'abonnement (Directeur).",
                        "Paramètres système : format des dates, délai de déconnexion automatique, type d'établissement (Privé / Public — un changement masque ou révèle le module Finance), et la Zone de danger (réinitialisation en mode bac à sable, passage en mode réel).",
                        "Utilisateurs & rôles : création des comptes du personnel et attribution des rôles (Directeur).",
                        "Journal d'audit : registre consultable de toutes les opérations sensibles — auteur, date, valeur antérieure (Directeur).",
                        "Notifications SMS : expéditeur, gabarits et suivi des envois — section de la formule Premium, visible hors formule avec un badge (Directeur)."
                    ],
                    impacts: [
                        "Documents officiels : nom, logo, mentions légales, signatures et cachet proviennent d'ici et s'impriment sur reçus, attestations et bulletins.",
                        "Navigation : le type d'établissement (Paramètres système) fait apparaître ou disparaître les menus Finance et Caisse pour tout le personnel.",
                        "Droits : les délégations de « Notation & mentions » et de « Mensualités & autorisations de caisse » sont écrites par le Directeur seul et lues en direct par les écrans Notes et Frais.",
                        "Visibilité : un rôle qui n'a pas accès à une section ne voit pas son onglet — Facturation, Utilisateurs, Journal d'audit et Notifications SMS sont réservés au Directeur.",
                        "Écriture réservée : hors Directeur, les autres rôles consultent les valeurs en lecture seule ; l'API refuse toute modification (403)."
                    ],
                    recommandations: [
                        "Renseignez Profil, Formats & signatures et Intégration étatique AVANT la première édition de documents de l'année : un logo ou un cachet ajouté après coup ne réécrit pas les PDF déjà remis.",
                        "N'accordez une délégation (notation, frais) que lorsqu'elle est réellement nécessaire, et retirez-la quand elle ne l'est plus : chaque interrupteur activé élargit ce qu'un rôle peut faire sans contrôle.",
                        "Le type d'établissement (Privé / Public) n'est pas un réglage anodin : il masque des modules entiers. Ne le changez qu'en connaissance de cause.",
                        "Après tout incident, ouvrez le Journal d'audit plutôt que d'interroger les agents : il rétablit la chronologie exacte sans mettre personne en cause."
                    ]
                },
                {
                    id: 'corriger-profil-utilisateur',
                    title: 'Corriger le profil d’un compte utilisateur',
                    location: 'Paramètres › Utilisateurs & rôles',
                    href: '/parametres?tab=utilisateurs',
                    roles: ['Directeur'],
                    definition:
                        "Le Directeur peut corriger le nom complet et/ou l'e-mail d'un compte qu'il a lui-même créé — " +
                        "Secrétariat, Finance, Enseignant, Surveillant — directement depuis la liste des utilisateurs, " +
                        "sans passer par la voie en libre-service réservée au titulaire du compte, et sans jamais " +
                        "pouvoir cibler sa PROPRE fiche par cette voie.",
                    objectif:
                        "Corriger une faute de frappe repérée après coup sur un prénom ou un e-mail saisi à la création " +
                        "d'un compte, sans devoir demander à la personne concernée de le faire elle-même — utile en " +
                        "particulier si elle n'a pas encore sa propre session ouverte.",
                    probleme:
                        "Une adresse e-mail mal saisie à la création d'un compte de secrétariat ou d'enseignant " +
                        "bloquait la personne concernée (aucune notification, aucune réinitialisation de mot de passe " +
                        "possible), sans que le Directeur ait de recours simple pour la corriger.",
                    procedure: [
                        "Ouvrez Paramètres › Utilisateurs & rôles.",
                        "Repérez le compte à corriger et ouvrez « Modifier le profil ».",
                        "Corrigez le nom complet et/ou l'e-mail, puis enregistrez.",
                        "Aucun mot de passe n'est demandé : c'est le Directeur qui corrige la fiche d'autrui, pas le titulaire qui change ses propres accès."
                    ],
                    impacts: [
                        "Compte visé : le nouvel e-mail devient immédiatement l'identifiant de connexion de ce compte.",
                        "Votre propre fiche : cette voie est refusée si le compte visé est le VÔTRE — un Directeur corrige son propre e-mail par « Changer mon e-mail » (voir « Sécurité de votre compte »), jamais par ici.",
                        "Unicité : un e-mail déjà utilisé par un autre compte de l'établissement est refusé."
                    ],
                    recommandations: [
                        "Prévenez la personne concernée après une correction d'e-mail : c'est sa nouvelle adresse de connexion, elle doit la connaître avant sa prochaine tentative.",
                        "Réservez cette voie aux erreurs de saisie : un changement d'e-mail voulu par la personne elle-même passe par sa propre voie en libre-service, quand son rôle y a accès."
                    ]
                },
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
                        "Si le calendrier se décale en cours d'exercice, une année en cours ou à venir reste corrigeable : prolonger la période recale les trimestres sans jamais altérer les notes déjà saisies.",
                        "SUPPRIMER UNE ANNÉE (icône corbeille) : réservé au Directeur, et il faut recopier le libellé exact de l'année — « 2025-2026 » — pour débloquer le bouton. En mode bac à sable, l'année et toutes ses données sont effacées ; en mode réel, seule une année qui n'a JAMAIS servi peut être retirée (elle est alors archivée), et l'application refuse en expliquant ce qui la retient dès qu'une inscription, une note ou un appel y est rattaché.",
                        "Si l'année supprimée était l'année active, l'établissement rebascule tout seul sur l'année ouverte la plus proche. S'il n'en reste aucune, l'écran vous demande d'en activer ou d'en créer une : sans année active, aucune inscription ni aucune note ne peut être saisie."
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
                        "Une année révolue est volontairement verrouillée. Une donnée qui s'y révèle erronée fait l'objet d'une régularisation historisée sur l'exercice courant, jamais d'une réécriture du passé.",
                        "Avant de supprimer une année en mode bac à sable, exportez-la si elle contient des saisies que vous souhaitez relire : l'effacement est définitif et l'export ZIP (icône de téléchargement) reste la seule copie."
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
                },
                {
                    id: 'mode-sandbox-golive',
                    title: 'Mode bac à sable, passage en mode réel et verrouillage définitif',
                    location: 'Paramètres › Paramètres système — Zone de danger',
                    href: '/parametres?tab=securite',
                    roles: ['Directeur'],
                    definition:
                        "Un établissement démarre en MODE BAC À SABLE : tout ce qui y est saisi peut être effacé d'un " +
                        "geste pour repartir d'une base vierge. Le PASSAGE EN MODE RÉEL et le retour en mode test sont " +
                        "deux bascules PLEINEMENT RÉVERSIBLES, aussi souvent que voulu — aucune des deux ne verrouille " +
                        "quoi que ce soit par elle-même. Seul le VERROUILLAGE DÉFINITIF, une action manuelle distincte " +
                        "des deux précédentes, ferme la réinitialisation pour toujours, quel que soit le régime " +
                        "ultérieur.",
                    objectif:
                        "Laisser l'établissement s'exercer sans crainte de « salir » ses données définitives — tester, " +
                        "réinitialiser, refaire des essais, basculer en réel puis revenir tester encore — jusqu'au jour " +
                        "où le Directeur choisit lui-même de fermer définitivement cette porte de secours.",
                    probleme:
                        "Sans distinction entre essai et réel, un établissement qui teste l'application avec de vraies " +
                        "données se retrouve avec des élèves fictifs mélangés aux vrais. Et une bascule test/réel qui " +
                        "fermerait la purge à la première tentative punirait un Directeur qui hésite encore, ou qui a " +
                        "besoin de refaire un essai après avoir déjà basculé une fois.",
                    procedure: [
                        "La pastille de la barre supérieure indique en permanence le régime en cours : « Mode test » (orange) ou « Mode réel ». Pour le Directeur, elle mène directement à Paramètres › Sécurité, tout en bas de la section.",
                        "« Réinitialiser l'école » (disponible en mode test, tant qu'aucun verrouillage définitif n'a été posé) efface tout ce que vous avez saisi ET paramétré : élèves, inscriptions, notes, bulletins, transactions, classes, matières, enseignants, barème des frais, inventaire, paie, comptes du personnel.",
                        "Sont CONSERVÉS par une réinitialisation : votre compte Directeur, la fiche et les réglages de l'établissement, votre abonnement, les années scolaires et leurs trimestres, les mentions, les bâtiments et salles, et le journal d'audit.",
                        "« Passer en mode réel » : saisissez « CONFIRMER » ou le nom exact de l'établissement. La réinitialisation devient alors indisponible, mais UNIQUEMENT le temps du mode réel.",
                        "« Repasser en mode test » (visible et ACTIF une fois en mode réel) : saisissez « TEST » ou le nom de l'établissement. Cette bascule n'efface aucune donnée et rouvre aussitôt la réinitialisation — sauf si le verrouillage définitif a été posé entre-temps.",
                        "« Verrouiller définitivement l'établissement » : une action séparée, à son propre encart de la Zone de danger. Saisissez « VERROUILLER » ou le nom de l'établissement. Au-delà, la réinitialisation disparaît pour toujours, quel que soit le régime test/réel dans lequel l'établissement se trouvera ensuite."
                    ],
                    impacts: [
                        "Tous les modules : chaque écran continue de fonctionner à l'identique quel que soit le régime — seule la disponibilité de la réinitialisation en dépend.",
                        "Journal d'audit : jamais effacé, y compris par une réinitialisation ; chaque bascule (mode réel, retour en test, verrouillage) y est elle-même consignée.",
                        "Comptes du personnel : supprimés par une réinitialisation ; il faudra les recréer. Seul votre compte Directeur survit.",
                        "Groupe scolaire : un compte ayant aussi accès à un autre établissement n'est pas supprimé — il perd seulement son accès à l'école réinitialisée."
                    ],
                    recommandations: [
                        "N'utilisez PAS le verrouillage définitif comme un réflexe précoce : tant qu'il n'est pas posé, vous gardez le droit à l'erreur — le passage en mode réel, lui, ne ferme plus rien tout seul depuis le 19/09/2026.",
                        "Posez le verrouillage définitif une fois vos données réelles installées et vérifiées, si vous voulez exclure tout risque de purge accidentelle par la suite — c'est la seule étape réellement irréversible de tout ce dispositif.",
                        "Réinitialisez librement pendant la phase de test, y compris après un ou plusieurs allers-retours en mode réel : rien ne consomme ce droit tant que le verrouillage définitif n'est pas posé.",
                        "Une fois verrouillé définitivement, une donnée erronée se corrige au cas par cas dans son module d'origine : aucune commande ne remet ce verrou à faux."
                    ]
                },
                {
                    id: 'comprendre-abonnement',
                    title: 'Comprendre votre abonnement — Primaire, Standard, Premium',
                    location: 'Paramètres › Facturation',
                    href: '/parametres?tab=facturation',
                    roles: ['Directeur'],
                    definition:
                        "Chaque établissement souscrit à l'UNE de trois formules : Primaire (formule d'entrée), Standard, " +
                        "ou Premium. Le point essentiel : les trois donnent accès à L'INTÉGRALITÉ du socle métier — élèves, " +
                        "classes, inscriptions, notes, bulletins, caisse, encaissements, documents officiels, inventaire, " +
                        "examens, RH & paie, trésorerie, vie scolaire, intégration étatique. Une formule supérieure " +
                        "n'ajoute pas des modules, elle débloque trois fonctionnalités précises, et elles seules.",
                    objectif:
                        "Permettre au Directeur de choisir la formule qui correspond réellement à ses besoins, sans payer " +
                        "pour des options qu'il n'utilisera pas ni découvrir trop tard qu'une fonction attendue relevait " +
                        "d'un niveau supérieur.",
                    probleme:
                        "Sans une lecture claire de ce que chaque formule apporte, un établissement souscrit au hasard : " +
                        "il prend Premium « pour être tranquille » alors que le socle Primaire lui suffit, ou reste en " +
                        "Primaire puis se heurte à un bouton d'export grisé le jour du conseil d'administration.",
                    procedure: [
                        "FORMULE PRIMAIRE : tout le socle métier, rien de plus. Convient à un établissement qui gère ses élèves, ses notes, sa caisse et ses documents sans envoi de SMS aux parents ni consolidation financière avancée.",
                        "FORMULE STANDARD : le socle, PLUS les rapports financiers consolidés (ventilation des recettes par cycle, classe et mode de paiement) et l'export comptable au format .xlsx — l'écran Comptabilité › Rapports financiers.",
                        "FORMULE PREMIUM : le socle et les rapports financiers, PLUS les notifications SMS et WhatsApp sortantes vers les parents (retards, absences, impayés, reçus, convocations, bulletins), PLUS le mode groupe scolaire — plusieurs établissements pilotés depuis un même compte.",
                        "La formule en cours et sa date d'expiration se consultent dans Paramètres › Facturation ; un changement de formule se fait auprès de l'éditeur, pas depuis cet écran.",
                        "TARIFICATION INDICATIVE (à confirmer auprès de l'éditeur) : Primaire environ 10 000 FCFA / mois (100 000 / an), Standard environ 25 000 / mois (250 000 / an), Premium environ 45 000 / mois (450 000 / an)."
                    ],
                    impacts: [
                        "Rapports financiers : en formule Primaire, le bouton d'export existe mais reste désactivé et signalé « Standard » — il n'est jamais masqué, pour que le Directeur voie ce qu'une formule supérieure lui apporterait.",
                        "SMS / WhatsApp : en formule Primaire ou Standard, l'option d'envoi est visible mais marquée « Premium » et le serveur refuse tout envoi — le contrôle n'est jamais porté par le seul badge de l'interface.",
                        "Plafonds d'effectif : les repères « jusqu'à 500 élèves », « jusqu'à 2 000 » sont COMMERCIAUX, pas techniques. L'application n'empêche pas d'inscrire un élève de plus : seules les trois fonctionnalités ci-dessus sont réellement verrouillées.",
                        "Expiration : à l'échéance, l'établissement passe en mode restreint (lecture seule, export des données toujours possible) plutôt qu'un blocage total — pour ne jamais empêcher une école de récupérer ses données. Des alertes sont envoyées au Directeur 30, 15 et 7 jours avant.",
                        "Premier paiement : un établissement nouvellement approuvé mais dont le premier règlement n'est pas encore confirmé n'a accès qu'à l'écran de paiement, à l'exclusion de tout autre module."
                    ],
                    recommandations: [
                        "Choisissez d'abord d'après les TROIS fonctionnalités, pas d'après un nombre d'élèves : si vous n'envoyez pas de SMS aux parents et n'avez qu'un établissement, Premium ne vous apporte que les rapports financiers — que Standard offre déjà.",
                        "Prenez Standard dès que vous devez présenter un état des recettes ventilé à un conseil d'administration ou à un comptable : reconstituer ces chiffres à la main coûte plus cher que l'écart de formule.",
                        "Ne laissez pas l'abonnement expirer sans surveillance : le mode restreint bloque toute saisie, et un établissement en pleine rentrée s'en trouve paralysé. Les alertes à 30, 15 et 7 jours sont là pour l'éviter.",
                        "Le mode groupe scolaire (Premium) ne se justifie que si vous pilotez réellement plusieurs établissements distincts depuis un seul compte — deux cycles d'une même école ne sont pas deux établissements."
                    ]
                },
                {
                    id: 'matricules-format-numero',
                    title: 'Matricules — format, numéro de départ et correction',
                    location: 'Paramètres › Formats & signatures officielles',
                    href: '/parametres?tab=formats-signatures',
                    roles: ['Directeur'],
                    definition:
                        "Le matricule identifie un élève ou un enseignant à l'intérieur de l'établissement. Il est " +
                        "composé de deux parties. Le GABARIT fixe la forme — par exemple « ELEV-{YEAR}-{SEQ:4} », où " +
                        "{YEAR} est l'année scolaire et {SEQ:4} un compteur complété à quatre chiffres. Le COMPTEUR " +
                        "{SEQ} est attribué automatiquement à l'enregistrement, dans l'ordre, sans trou. Trois réglages " +
                        "sont ouverts au Directeur, et à lui seul : le gabarit, le numéro de départ du compteur pour " +
                        "l'année en cours, et la correction ponctuelle d'un matricule déjà attribué.",
                    objectif:
                        "Laisser l'établissement adopter sa propre convention de numérotation — reprendre celle d'un " +
                        "ancien logiciel, réserver une plage, préfixer par un sigle maison — sans renoncer à la garantie " +
                        "d'unicité et de continuité que l'attribution automatique apporte.",
                    probleme:
                        "Une numérotation entièrement libre, saisie à la main pour chaque élève, produit tôt ou tard " +
                        "des doublons, des trous et des formats incohérents d'une classe à l'autre. Une numérotation " +
                        "entièrement rigide, elle, empêche une école qui migre depuis un autre système de conserver les " +
                        "numéros que familles et administration connaissent déjà.",
                    procedure: [
                        "GABARIT : ouvrez Paramètres › Formats & signatures officielles, section « Format des matricules ». Modifiez le modèle des élèves et/ou des enseignants. Deux jetons seulement sont admis : {YEAR} et {SEQ} (ou {SEQ:n} pour compléter à n chiffres). Un modèle sans {SEQ} est refusé — tous les matricules seraient identiques.",
                        "NUMÉRO DE DÉPART : dans la même section, encart « Numéro de départ (année en cours) », saisissez le prochain numéro voulu — par exemple 1000 — puis cliquez sur « Appliquer ». Le compteur saute directement à cette valeur pour la prochaine inscription.",
                        "Le numéro de départ ne peut qu'AUGMENTER : l'application refuse une valeur inférieure ou égale au dernier numéro déjà attribué cette année, car elle réémettrait des matricules en circulation. Le dernier numéro attribué est rappelé sous le champ.",
                        "CORRECTION D'UN MATRICULE : ouvrez la fiche détaillée de l'élève ou de l'enseignant concerné, cliquez sur le crayon à côté du matricule, saisissez le bon, puis validez. L'application refuse un matricule déjà porté par un autre dossier de l'établissement.",
                        "La correction est journalisée (auteur, date, ancienne valeur) et ne modifie pas le compteur : corriger « ELEV-2026-0007 » en « ELEV-2026-0071 » ne change ni le dernier numéro attribué ni le prochain."
                    ],
                    impacts: [
                        "Compteur par année : les numéros sont propres à chaque année scolaire (bascule d'octobre) et repartent naturellement à la rentrée suivante. Le numéro de départ se règle pour l'année en cours.",
                        "Documents officiels : reçus, cartes scolaires, bulletins et listes reprennent le matricule tel qu'il est enregistré — une correction s'y reflète à la prochaine édition.",
                        "Reçus d'inscription : leur propre numérotation (« REC-{YEAR}-{SEQ} ») a un gabarit FIXE, non réglable — un reçu est une pièce comptable, sa forme ne se paramètre pas.",
                        "Import Excel de masse : il génère toujours un matricule automatique et ne reprend pas une colonne « matricule » du fichier ; pour aligner des dossiers repris d'un ancien système, la correction ponctuelle après import est la voie prévue.",
                        "Réinitialisation en mode bac à sable : elle remet les compteurs de matricules à zéro (voir « Mode bac à sable »)."
                    ],
                    recommandations: [
                        "Arrêtez le gabarit AVANT la première inscription de l'année : le changer ensuite ne réécrit pas les matricules déjà émis, et l'établissement se retrouve avec deux formes en circulation.",
                        "Réglez le numéro de départ juste après avoir activé la nouvelle année, avant d'inscrire : c'est le moment où le compteur est encore à zéro et où la plage est libre.",
                        "Réservez la correction d'un matricule aux vrais cas — erreur de reprise, alignement sur un dossier administratif : ce n'est pas un champ d'édition ordinaire, et chaque correction laisse une trace.",
                        "Ne cherchez pas à « rattraper » un trou de numérotation en corrigeant des matricules un par un : un trou est sans conséquence, la cohérence de la série l'est davantage."
                    ]
                },
                {
                    id: 'activer-desactiver-module',
                    title: 'Activer ou désactiver un module de l’application',
                    location: 'Paramètres › Modules & fonctionnalités',
                    href: '/parametres?tab=modules',
                    roles: ['Directeur'],
                    definition:
                        "Chaque grand pôle de l'application — Pédagogie, Comptabilité & Finance, Internat, et la filière " +
                        "Coranique/Franco-Arabe — se masque ou se révèle d'un interrupteur, indépendamment de la formule " +
                        "d'abonnement (Primaire/Standard/Premium). PÉDAGOGIE recouvre Matières, Notes & bulletins, Cahier " +
                        "de texte, Examens officiels, Intégration étatique et toute la Surveillance générale (appel, " +
                        "billets, discipline, convocations, pointage). FINANCE recouvre Caisse, Frais, Paie, Fiscalité, " +
                        "Trésorerie et Rapports financiers.",
                    objectif:
                        "Adapter le menu et le vocabulaire de l'application à ce que l'établissement utilise réellement, " +
                        "sans imposer à un établissement purement administratif un module Pédagogie qu'il ne remplira " +
                        "jamais, ni l'inverse à une école qui n'encaisse rien elle-même.",
                    probleme:
                        "Une application qui affiche vingt entrées de menu, dont la moitié ne concerne jamais " +
                        "l'établissement, noie l'utilisateur — et surtout NOUVEAU, qui ne sait plus distinguer ce qu'il " +
                        "doit remplir de ce qui ne le concerne pas.",
                    procedure: [
                        "Ouvrez Paramètres › Modules & fonctionnalités.",
                        "Basculez l'interrupteur du module voulu. L'enregistrement est immédiat, sans bouton « Valider » séparé.",
                        "Un module désactivé disparaît de la barre latérale et de la barre de navigation rapide pour TOUS les comptes de l'établissement, quel que soit leur rôle — pas seulement pour vous.",
                        "Pédagogie et Finance sont activés PAR DÉFAUT : un établissement qui n'y touche jamais conserve le comportement historique de l'application, tous modules ouverts.",
                        "Internat, lui, est désactivé par défaut : l'activer ouvre réellement l'écran /internat et ses règles d'hébergement — ce n'est pas un réglage anticipé comme Pédagogie/Finance."
                    ],
                    impacts: [
                        "Aucune perte de données : désactiver un module le masque, il ne supprime ni n'archive rien. Le réactiver restitue l'accès à l'identique.",
                        "API : la garde réelle est posée sur chaque contrôleur concerné ([RequireModule]) — masquer un lien dans le menu est un confort d'affichage, la protection véritable répond par 403 même en visant l'adresse directement.",
                        "Filière Coranique/Franco-Arabe : au-delà de son propre menu, ce réglage déclenche aussi le bulletin bilingue Français/Arabe sur TOUTES les matières où un nom arabe est renseigné (voir « Bulletin bilingue »).",
                        "Barre de navigation rapide (QuickNav) : un module désactivé y disparaît exactement comme dans la barre latérale, les deux lisant le même réglage."
                    ],
                    recommandations: [
                        "Ne désactivez jamais un module en cours d'exercice sans en avertir le personnel concerné : un enseignant qui perd soudainement l'accès aux Notes en pleine saisie de trimestre s'inquiète à raison.",
                        "Un établissement 100 % administratif (pas de pédagogie propre, ex. une structure de coordination) peut désactiver Pédagogie sans risque : rien n'y dépend d'un effectif d'élèves suivi ailleurs.",
                        "N'activez Internat que si l'établissement héberge réellement des élèves : le module ajoute un vocabulaire (dortoir, pension, régime) qui n'a pas sa place ailleurs.",
                        "Si un module reste invisible après activation, faites d'abord contrôler votre propre rôle : la garde par module se combine avec la garde par rôle, les deux doivent être satisfaites."
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
                "responsabilité de chaque note portée au dossier d'un élève. L'EMPLOI DU TEMPS lui-même — la grille " +
                "des créneaux hebdomadaires, consultable par enseignant ou par classe — se construit sur ce même " +
                "écran, à partir de ces affectations.",
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
                        "Ouvrez Gestion Scolaire › Enseignants, puis cliquez sur « Ajouter un enseignant ».",
                        "Renseignez l'état civil, le téléphone, l'adresse électronique et la spécialité. Le matricule est attribué automatiquement à l'enregistrement, jamais à l'ouverture du formulaire.",
                        "Précisez la nature du rattachement — permanent ou vacataire — ainsi que les éléments contractuels utiles à la paie.",
                        "Depuis la fiche détaillée, ajoutez les affectations : pour chaque matière enseignée, désignez la ou les classes concernées.",
                        "Créez, si nécessaire, le compte utilisateur associé depuis Paramètres › Utilisateurs & rôles, en lui attribuant le rôle « Enseignant ».",
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
                },
                {
                    id: 'rattacher-compte-enseignant',
                    title: 'Rattacher ou changer le compte de connexion d’un enseignant',
                    location: 'Gestion Scolaire › Enseignants — fiche détaillée',
                    href: '/enseignants',
                    roles: ['Directeur'],
                    definition:
                        "Le rattachement d'un compte de connexion (rôle Enseignant) à une fiche enseignant n'est plus " +
                        "figé à la création : le Directeur peut l'ajouter après coup, le changer, ou le retirer, depuis " +
                        "la fiche détaillée elle-même — un bouton distinct à côté du matricule et du statut.",
                    objectif:
                        "Coller au fonctionnement réel des établissements : la fiche RH d'un enseignant existe souvent " +
                        "de longue date avant qu'un compte de connexion ne lui soit ouvert, et un compte créé par erreur " +
                        "doit pouvoir être corrigé sans recréer la fiche entière.",
                    probleme:
                        "Auparavant, le compte de connexion ne pouvait être posé qu'À LA CRÉATION de la fiche — ce qui " +
                        "imposait de créer le compte AVANT la fiche enseignant, dans le mauvais ordre pour la plupart " +
                        "des établissements, ou de vivre avec un mauvais rattachement fait par erreur.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Enseignants et ouvrez la fiche détaillée de l'enseignant concerné.",
                        "À côté du matricule, cliquez sur l'icône de rattachement : « Rattacher un compte » si aucun compte n'est encore lié, « Changer le compte rattaché » sinon.",
                        "Choisissez le compte dans la liste — seuls les comptes de rôle Enseignant de votre établissement, pas déjà rattachés à une autre fiche, sont proposés.",
                        "Enregistrez. Pour retirer un rattachement sans en poser un autre, choisissez « Aucun » dans la même liste.",
                        "Aucun mot de passe n'est demandé pour cette opération : c'est le Directeur qui agit sur la fiche, pas le titulaire du compte qui prouve son identité."
                    ],
                    impacts: [
                        "Emploi du temps et Cahier de texte : c'est ce rattachement qui détermine quel compte peut consulter son propre emploi du temps et journaliser ses propres séances.",
                        "Un compte déjà rattaché à une autre fiche enseignant est refusé : un compte ne peut être lié qu'à UNE seule fiche à la fois.",
                        "Fiche enseignant : le rattachement est une opération SÉCURISÉE, distincte de la correction des champs administratifs (état civil, contact) — elle ne se fait jamais depuis la modale « Modifier la fiche »."
                    ],
                    recommandations: [
                        "Vérifiez l'identité du compte avant de le rattacher : une fois lié, ce compte hérite de tout ce que la fiche enseignant autorise (appel, notes, cahier de texte).",
                        "Si un enseignant quitte l'établissement, retirez le rattachement (« Aucun ») avant d'archiver ou de réutiliser le compte pour quelqu'un d'autre.",
                        "Un compte introuvable dans la liste est presque toujours un compte du mauvais rôle ou déjà rattaché ailleurs — vérifiez d'abord Paramètres › Utilisateurs & rôles plutôt que de conclure à un bug."
                    ]
                },
                {
                    id: 'emploi-du-temps',
                    title: 'Emploi du temps — grille des créneaux hebdomadaires',
                    location: 'Gestion Scolaire › Enseignants — section « Emploi du temps »',
                    href: '/enseignants',
                    roles: ['Directeur', 'Secrétariat', 'Enseignant'],
                    definition:
                        "La grille des créneaux d'une semaine type : chaque créneau associe un enseignant, une classe, " +
                        "une matière, un jour, une heure de début et une heure de fin, et un numéro de salle facultatif. " +
                        "La grille se lit de deux façons au choix, par un jeu d'onglets : « Par Enseignant » montre la " +
                        "semaine d'un professeur, « Par Classe » montre celle d'un groupe d'élèves — ce sont deux vues du " +
                        "même jeu de créneaux, jamais deux saisies séparées.",
                    objectif:
                        "Donner à l'établissement une répartition hebdomadaire tenue à un seul endroit, cohérente entre le " +
                        "point de vue de l'enseignant et celui de la classe. Pour la direction, c'est l'instrument qui " +
                        "révèle immédiatement un professeur doublement programmé à la même heure, ou une classe laissée " +
                        "sans cours sur un créneau.",
                    probleme:
                        "Un emploi du temps tenu sur une feuille par classe et recopié à part pour chaque enseignant " +
                        "diverge dès la première modification : la classe croit avoir cours, le professeur pense être " +
                        "libre, et personne ne détient la version qui fait foi. Les collisions — deux classes pour un " +
                        "même professeur, deux professeurs pour une même salle — ne se voient qu'une fois les élèves " +
                        "devant la porte.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Enseignants et faites défiler jusqu'à la section « Emploi du temps ».",
                        "Choisissez le mode de lecture : « Par Enseignant » puis un professeur dans la liste, ou « Par Classe » puis une classe.",
                        "Cliquez sur « Nouveau Créneau » : renseignez le jour, l'heure de début et de fin, l'enseignant, la classe, la matière, et le numéro de salle si l'établissement le suit.",
                        "Enregistrez : le créneau apparaît aussitôt dans les DEUX vues — celle de l'enseignant et celle de la classe.",
                        "Pour corriger ou retirer un créneau, ouvrez-le depuis la grille : la modification et la suppression se font au même endroit.",
                        "Un enseignant connecté avec son propre compte ne voit et ne propose des créneaux que pour lui-même ; la Direction et le Secrétariat voient et modifient toute la grille."
                    ],
                    impacts: [
                        "Affectations : un créneau s'appuie sur les couples matière + classe déjà déclarés sur la fiche de l'enseignant — une matière qu'il n'enseigne pas ne lui est pas proposée.",
                        "Salles : le numéro de salle est un simple libellé indicatif porté par le créneau ; le module Infrastructures reste la référence de la capacité et de l'existence réelle des locaux.",
                        "Pointage des heures : les heures effectivement faites se saisissent séparément (Comptabilité › Paie, Pointage Profs) et ne se déduisent pas automatiquement de la grille — l'emploi du temps est un prévisionnel, le pointage un constat.",
                        "Navigation : depuis un créneau, la matière, la classe ou l'enseignant sont cliquables et ouvrent l'écran correspondant, filtré sur l'élément visé."
                    ],
                    recommandations: [
                        "Renseignez les affectations de chaque enseignant AVANT de bâtir sa semaine : sans elles, aucune matière ne peut être placée.",
                        "Vérifiez la grille dans les deux modes de lecture après une série de modifications : une collision invisible « Par Enseignant » saute aux yeux « Par Classe », et inversement.",
                        "Ne comptez pas sur l'emploi du temps pour la paie : c'est le pointage des heures qui fait foi sur ce qui est dû, la grille n'est qu'une prévision.",
                        "Un numéro de salle sur un créneau ne réserve pas la salle : si l'établissement veut éviter les doubles occupations, il lui faut une convention de nommage stricte et un contrôle humain, la grille ne l'impose pas."
                    ]
                },
                {
                    id: 'cahier-de-texte',
                    title: 'Cahier de texte — journal de classe',
                    location: 'Gestion Scolaire › Cahier de texte',
                    href: '/cahier-de-texte',
                    roles: ['Directeur', 'Secrétariat', 'Surveillant', 'Enseignant'],
                    definition:
                        "Une entrée par séance RÉELLEMENT tenue — jamais un programme prévisionnel — pour une classe et " +
                        "une matière données : le sujet traité, le contenu résumé, et les devoirs éventuellement donnés " +
                        "avec leur date de rendu. C'est un document pédagogique PARTAGÉ, consultable par toute la chaîne " +
                        "d'encadrement, et non un carnet privé propre à chaque enseignant.",
                    objectif:
                        "Assurer la continuité pédagogique — un remplaçant sait exactement où reprendre — et donner à la " +
                        "direction une preuve datée de ce qui a réellement été enseigné, classe par classe et matière par " +
                        "matière, sans dépendre d'un cahier papier qui reste dans le sac de l'enseignant.",
                    probleme:
                        "Un cahier de texte tenu sur papier ne voyage qu'avec son auteur : en cas d'absence imprévue, le " +
                        "remplaçant improvise faute de savoir ce qui a été vu, et la direction n'a aucun moyen de vérifier " +
                        "qu'un programme annoncé a effectivement été suivi — seul le bulletin de fin de trimestre le " +
                        "révèle, bien trop tard pour agir.",
                    procedure: [
                        "CONSULTATION (tous rôles) : ouvrez Gestion Scolaire › Cahier de texte. Filtrez par classe, par matière et par plage de dates ; la liste se pagine et reste stable d'une page à l'autre.",
                        "SAISIE (Enseignant uniquement) : cliquez sur « Journaliser une séance », choisissez la classe et la matière, la date de la séance — jamais future —, puis renseignez le sujet et le contenu.",
                        "L'application n'accepte la saisie que pour une classe et une matière où VOUS êtes affecté cette année, ET où un créneau existe ce jour de la semaine dans votre emploi du temps : sinon, un message explicite renvoie vers le Directeur ou le Secrétariat.",
                        "DEVOIRS (facultatif) : si un devoir est donné, renseignez-le et fixez sa date de rendu — elle ne peut pas précéder la date de la séance, et un champ sans devoir associé est refusé.",
                        "CORRECTION : l'auteur corrige librement sa propre entrée pendant 15 jours après la séance. Passé ce délai, seuls le Directeur et le Secrétariat peuvent encore la corriger — jamais l'auteur lui-même.",
                        "La classe, la matière et la date de séance ne se corrigent JAMAIS après coup, par personne : seules les entrées « Sujet », « Contenu » et « Devoirs » restent modifiables — une séance mal datée se supprime et se ressaisit, elle ne se déplace pas."
                    ],
                    impacts: [
                        "Emploi du temps et affectations : la saisie s'appuie exactement sur les mêmes créneaux et les mêmes affectations classe/matière que l'écran Emploi du temps — aucun double paramétrage.",
                        "Surveillance : le Surveillant lit le journal en consultation seule ; les actions de correction et de suppression n'apparaissent jamais dans son interface, quel que soit le délai des 15 jours.",
                        "Module Pédagogie : le Cahier de texte disparaît de la barre latérale, comme Notes et Examens, si le Directeur désactive le module Pédagogie dans Paramètres › Modules & fonctionnalités.",
                        "Réinitialisation en mode bac à sable : les entrées du cahier de texte font partie des données effacées, comme les notes et les bulletins."
                    ],
                    recommandations: [
                        "Journalisez la séance le jour même : au-delà de quelques jours, les détails s'estompent et le journal perd sa valeur de continuité.",
                        "Ne confondez pas ce journal avec l'Appel en classe (Surveillance) : l'un dit CE QUI a été enseigné, l'autre QUI était présent — deux registres distincts, jamais fusionnés.",
                        "Si une séance manque après le délai de 15 jours, ne demandez pas à l'enseignant de la recréer : seuls le Directeur ou le Secrétariat peuvent encore corriger l'entrée existante.",
                        "N'attendez pas une absence imprévue pour découvrir qu'un enseignant n'a rien journalisé depuis des semaines : consultez le journal par classe à échéance régulière, c'est un indicateur discret de suivi pédagogique."
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
                        "Ouvrez Gestion Scolaire › Élèves, puis cliquez sur « Ajouter un élève ».",
                        "Recherchez d'abord le nom dans le registre existant : cette précaution élémentaire évite la quasi-totalité des doublons.",
                        "Saisissez l'état civil en le recopiant sur l'extrait de naissance, orthographe et accents compris — c'est cette graphie qui figurera sur tous les documents officiels.",
                        "Renseignez le tuteur légal : nom, lien de parenté, téléphone et adresse. Ce numéro est celui qui recevra les notifications par SMS ou WhatsApp.",
                        "Ajoutez la photographie de l'élève : elle est automatiquement compressée avant transmission et alimente la carte scolaire.",
                        "Enregistrez. Le matricule est généré dans la transaction même, ce qui garantit l'absence de trou et de collision dans la numérotation.",
                        "À l'enregistrement, la fenêtre « Élève ajouté » propose trois suites : « Inscrire maintenant » ouvre l'écran Inscriptions déjà pré-réglé pour cet élève ; « Ajouter un autre » enchaîne sur une nouvelle fiche ; « Plus tard » referme sans inscrire — la fiche existe alors à l'annuaire, mais l'élève n'a AUCUNE inscription pour l'année active.",
                        "Pour inscrire dans la foulée, choisissez « Inscrire maintenant » : sélectionnez la classe et consultez le panneau « Frais », qui n'est qu'une aide au calcul — aucun encaissement ne s'y fait —, puis validez.",
                        "Éditez et remettez à la famille l'attestation d'inscription au format PDF, puis orientez-la vers la Caisse pour le règlement."
                    ],
                    impacts: [
                        "Comptabilité : la validation de l'inscription fige le montant total dû, ligne par ligne, et ouvre le dossier financier de l'élève ; elle n'enregistre aucun versement.",
                        "Échéancier : les échéances de règlement sont établies à partir du barème de la classe.",
                        "Classe : l'effectif de la classe s'accroît immédiatement et se confronte à la capacité de la salle affectée.",
                        "Notes : l'élève apparaît dès la validation dans les listes de saisie des notes et dans les feuilles d'appel.",
                        "« Plus tard » : une fiche seule ne vaut pas inscription — l'élève reste hors des effectifs, des frais et des présences tant qu'il n'est pas inscrit pour l'année active. Il se retrouve dans la vue « Non inscrits » de la liste (voir la fiche « Inscription différée »).",
                        "Documents : l'attestation d'inscription et la carte scolaire sont produites à partir de cet état civil ; le reçu de paiement, lui, est délivré par la Caisse lors du règlement."
                    ],
                    recommandations: [
                        "N'ouvrez jamais deux formulaires de création simultanément sur deux postes pour le même élève.",
                        "Le matricule n'est jamais réservé à l'avance : un formulaire abandonné ne consomme aucun numéro. Ne cherchez donc pas à « garder » un matricule.",
                        "Si la classe définitive n'est pas encore arrêtée, choisissez « Plus tard » sans crainte : la vue « Non inscrits » de la liste garde l'élève sous la main, avec un bouton « Inscrire cette année » qui l'inscrit en un clic le moment venu.",
                        "Une erreur d'état civil se corrige par la fiche élève, et la correction est historisée. Ne créez jamais un second élève pour rectifier le premier.",
                        "Le secrétariat n'encaisse rien : après l'inscription, la famille se présente à la Caisse, qui constate le versement et délivre le reçu portant la mention réglementaire invitant à le conserver.",
                        "Contrôlez le numéro de téléphone du tuteur au moment de la saisie : un numéro erroné rend inopérante toute la chaîne de relance."
                    ]
                },
                {
                    id: 'notification-inscription-directeur',
                    title: 'Notification automatique du Directeur à chaque inscription',
                    location: 'Boîte e-mail du Directeur — aucun écran dédié',
                    href: '/eleves',
                    roles: ['Directeur'],
                    definition:
                        "À chaque inscription validée, un e-mail automatique part vers TOUS les comptes Directeur actifs " +
                        "de l'établissement — jamais vers l'adresse générique de l'école. Aucun réglage ne l'active ou ne " +
                        "le désactive : c'est un comportement permanent du module Inscriptions.",
                    objectif:
                        "Tenir la direction informée du rythme réel des inscriptions sans qu'elle ait à ouvrir " +
                        "l'application pour le constater — utile en particulier pour un Directeur qui partage la " +
                        "supervision avec un adjoint, les deux recevant le même e-mail.",
                    probleme:
                        "Sans notification, une inscription saisie par le secrétariat un jour d'affluence pouvait passer " +
                        "totalement inaperçue de la direction jusqu'à la consultation d'un rapport, des semaines plus " +
                        "tard.",
                    procedure: [
                        "Rien à activer ni à configurer : la notification part d'elle-même dès qu'une inscription est validée.",
                        "Consultez simplement votre boîte e-mail : chaque compte Directeur actif de l'établissement reçoit son propre message.",
                        "Si aucun Directeur actif n'existe dans l'établissement au moment de l'inscription, l'e-mail n'est simplement envoyé à personne — l'inscription elle-même n'est jamais bloquée pour autant."
                    ],
                    impacts: [
                        "Inscription : l'envoi se déclenche APRÈS que l'inscription est réellement enregistrée — un e-mail reçu garantit donc que l'inscription a bien abouti.",
                        "Fiabilité : un échec d'envoi isolé (panne du serveur de messagerie) est journalisé côté serveur et n'empêche ni l'inscription, ni la notification des autres Directeurs.",
                        "Comptes suspendus : un compte Directeur suspendu ou d'une autre école ne reçoit jamais cette notification."
                    ],
                    recommandations: [
                        "Vérifiez que l'adresse e-mail de chaque compte Directeur est correcte et surveillée : c'est le seul canal de cette notification, il n'existe pas de rappel dans l'application elle-même.",
                        "Si les e-mails n'arrivent jamais, vérifiez d'abord les courriers indésirables avant de conclure à une panne : c'est la cause la plus fréquente.",
                        "Ne comptez pas sur cet e-mail comme preuve comptable de l'inscription : le reçu et l'attestation PDF, remis à la famille, restent les pièces officielles."
                    ]
                },
                {
                    id: 'inscription-differee',
                    title: 'Inscription différée : « Plus tard », vue « Non inscrits » et « Inscrire cette année »',
                    location: 'Gestion Scolaire › Élèves — vue « Non inscrits » et fiche élève',
                    href: '/eleves',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Créer une fiche élève et l'inscrire pour l'année active sont DEUX actes distincts. Le premier ajoute " +
                        "l'enfant à l'annuaire de l'établissement ; le second le rattache à une année, une classe et un barème. " +
                        "Quand on répond « Plus tard » à la fenêtre « Élève ajouté », la fiche existe seule : l'élève est " +
                        "enregistré mais NON INSCRIT. La liste des élèves offre alors un interrupteur à trois positions — " +
                        "« Inscrits cette année » (par défaut), « Non inscrits », « Tous les élèves » — et un raccourci " +
                        "« Inscrire cette année » qui ouvre l'écran Inscriptions déjà pré-réglé pour cet élève, sans nouveau " +
                        "matricule (chemin Réinscription : la fiche existe déjà).",
                    objectif:
                        "Permettre au secrétariat d'enregistrer un enfant dès que son dossier arrive, sans attendre que sa classe " +
                        "soit tranchée ni que les frais soient reconduits — puis de retrouver sans effort tous ceux qui restent à " +
                        "inscrire et de les traiter un par un, en un clic. Pour la direction, c'est la garantie qu'un élève accueilli " +
                        "n'est jamais perdu de vue entre son admission et son inscription effective.",
                    probleme:
                        "Sans distinction entre « fiche créée » et « inscrit », soit le secrétariat invente une classe provisoire " +
                        "pour pouvoir enregistrer l'élève — et fausse aussitôt l'effectif et les frais —, soit il note le nom sur un " +
                        "papier en attendant, et l'oublie. Les élèves accueillis mais non inscrits deviennent alors invisibles : " +
                        "ils ne réapparaissent qu'au moment des bulletins ou des examens, quand il est trop tard pour régulariser.",
                    procedure: [
                        "À la création d'une fiche, la fenêtre « Élève ajouté » rappelle que l'élève n'est pas encore inscrit et propose « Inscrire maintenant », « Ajouter un autre » ou « Plus tard ». « Plus tard » referme sans inscrire.",
                        "Un bandeau ambré en tête de la liste Élèves annonce le nombre d'élèves ajoutés mais non inscrits pour l'année active, et renvoie vers la vue qui les isole.",
                        "Dans la barre de filtres, l'interrupteur « Inscrits cette année / Non inscrits / Tous les élèves » borne la liste : « Non inscrits » (segment rouge) ne montre QUE les élèves de l'annuaire sans inscription vivante pour l'année active.",
                        "Sur chaque ligne de la vue « Non inscrits », le bouton « Inscrire cette année » ouvre l'écran Inscriptions pré-réglé (élève déjà sélectionné, mode Réinscription) : il ne reste qu'à choisir la classe et valider.",
                        "Depuis la fiche d'un élève non inscrit, un bandeau « Élève non inscrit pour [année active] » affiche le même bouton « Inscrire maintenant ». La pastille d'en-tête indique alors « Non inscrit » au lieu de « Inscrit ».",
                        "Une fois l'inscription validée, l'élève quitte la vue « Non inscrits », entre dans les effectifs, son échéancier est créé et il apparaît en Caisse, aux notes et aux présences."
                    ],
                    impacts: [
                        "Effectifs, frais, présences : un élève non inscrit n'y figure pas — c'est voulu, et c'est pourquoi le bandeau ambré le signale plutôt que de le laisser silencieusement absent.",
                        "Aucun nouveau matricule : « Inscrire cette année » passe par le chemin Réinscription ; la fiche et son matricule d'origine sont conservés.",
                        "Inscriptions : le raccourci ne fait que pré-remplir l'écran — la classe, le contrôle du barème et la validation restent identiques à une réinscription ordinaire.",
                        "Fiche élève : la pastille « Inscrit » / « Non inscrit » et le bandeau sont dérivés de l'historique scolaire chargé, jamais d'un statut écrit à part — ils se mettent à jour dès l'inscription faite.",
                        "Aucun appel réseau supplémentaire : la vue « Non inscrits » et la bannière reposent sur les données déjà chargées (liste et fiche)."
                    ],
                    recommandations: [
                        "Utilisez « Plus tard » chaque fois qu'un doute subsiste sur la classe : mieux vaut un élève clairement « non inscrit » qu'un élève rangé dans une mauvaise classe « pour le faire entrer quelque part ».",
                        "En début de campagne, passez la liste en vue « Non inscrits » à intervalle régulier : c'est la liste de travail des inscriptions restant à faire.",
                        "N'activez la nouvelle année et ne reconduisez les barèmes qu'ensuite : une inscription enregistrée avant le barème se retrouve sans montant dû.",
                        "Le bouton « Inscrire cette année » ne dispense pas du contrôle du montant et de l'échéancier sur l'écran Inscriptions : il fait gagner la recherche de l'élève, pas la vérification.",
                        "Un élève créé par pure erreur de saisie et jamais inscrit se supprime depuis sa fiche (la fiche est archivée) ; ne le laissez pas encombrer indéfiniment la vue « Non inscrits »."
                    ]
                },
                {
                    id: 'apercu-frais-inscription',
                    title: "Panneau « Frais » à l'inscription — aide au calcul, sans encaissement",
                    location: 'Gestion Scolaire › Inscriptions',
                    href: '/inscriptions',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Panneau affiché à droite du formulaire d'inscription. Il reprend, pour la classe " +
                        "sélectionnée, le barème paramétré par la Comptabilité — frais d'inscription, tenue, " +
                        "mensualité unitaire — ainsi que le total dû pour l'année. Un simulateur y calcule, à " +
                        "titre purement indicatif, le sous-total correspondant au nombre de mensualités d'avance " +
                        "que l'on saisit. Ce panneau ne comporte AUCUNE saisie d'encaissement : ni case " +
                        "« réglé », ni mode de règlement, ni montant versé.",
                    objectif:
                        "Donner au secrétariat le montant exact à annoncer à la famille sans recourir à une " +
                        "calculatrice, et fonder sur ce chiffre l'attestation d'inscription. La séparation des " +
                        "rôles est nette : le secrétariat calcule et inscrit, il n'encaisse rien. Le versement — " +
                        "y compris le tout premier — se constate à la Caisse, seul service habilité à délivrer " +
                        "un reçu de paiement.",
                    probleme:
                        "Quand le même écran calcule les frais et enregistre le paiement, la frontière entre " +
                        "l'inscription et l'encaissement s'efface : un versement finit saisi par le secrétariat, " +
                        "hors session de caisse, sans rattachement à une journée comptable ni à un caissier " +
                        "responsable. Le rapprochement de caisse en fin de journée devient alors impossible à tenir.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Inscriptions et sélectionnez la classe d'affectation.",
                        "Le panneau « Frais » affiche aussitôt le détail du barème de cette classe et le total dû pour l'année.",
                        "Pour chiffrer une avance de plusieurs mensualités, ajustez le champ « Mensualités à régler d'avance » : le sous-total simulé se recalcule à l'écran.",
                        "Ce sous-total est indicatif : rien n'est enregistré, aucune donnée financière n'est modifiée.",
                        "Communiquez le montant à la famille, puis validez l'inscription : seul le dû annuel est alors figé, ligne à ligne.",
                        "Éditez l'attestation d'inscription et orientez la famille vers la Caisse pour le règlement.",
                        "À la Caisse, le caissier ouvre sa session, recherche l'élève et constate le versement, qui donne lieu au reçu de paiement."
                    ],
                    impacts: [
                        "Inscription : la validation fige le montant dû, mais ne crée aucun paiement et laisse le solde entièrement dû.",
                        "Caisse : l'élève fraîchement inscrit y apparaît avec l'intégralité de son solde à encaisser, échéancier compris.",
                        "Attestation d'inscription : elle annonce ce qu'il y a à régler ; elle ne vaut jamais reçu de paiement.",
                        "Barème : le panneau reflète en lecture seule la grille de la Comptabilité et ne permet pas de la modifier.",
                        "Journal d'audit : aucune écriture financière n'étant produite ici, seule l'inscription elle-même est tracée."
                    ],
                    recommandations: [
                        "N'attendez aucun encaissement de cet écran : il n'en propose pas, et c'est délibéré.",
                        "Le sous-total simulé n'engage à rien — il aide seulement à annoncer un montant juste à la famille.",
                        "Orientez systématiquement la famille vers la Caisse après l'inscription : le premier versement s'y constate comme tous les suivants.",
                        "Une remise ou un échelonnement particulier se traite en amont — barème, puis échéancier personnalisé —, jamais en minorant le montant annoncé ici."
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
                        "Éditez l'attestation de réinscription, remettez-la à la famille et orientez-la vers la Caisse pour le règlement."
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
                        "FICHE PAPIER : pour noter dans la salle, choisissez l'évaluation (Devoir 1, Devoir 2 ou Composition) puis cliquez sur « Fiche papier ». Le PDF obtenu est une grille vierge — élèves par ordre alphabétique, cases Note et Appréciation à remplir au stylo — que vous imprimez depuis l'aperçu, puis reportez à l'écran.",
                        "Enregistrez. Chaque saisie est horodatée et attribuée à son auteur.",
                        "Une note erronée se corrige sur ce même écran ; la modification est consignée dans le journal d'audit. Un enseignant corrige ses notes, ou celles de sa matière et de sa classe, pendant le délai fixé par le Directeur (7 jours par défaut, réglable dans Paramètres › Notation & mentions). Passé ce délai, la cellule est grisée : le Directeur ou le Secrétariat peuvent alors la corriger, sans limite de délai."
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
                        "Relisez vos notes dans le délai de correction : une fois celui-ci écoulé, seule la direction ou le Secrétariat peut les modifier.",
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
                    id: 'bulletin-bilingue-arabe',
                    title: 'Bulletin bilingue Français / Arabe (filière Coranique / Franco-Arabe)',
                    location: 'Paramètres › Modules & fonctionnalités, puis Gestion Scolaire › Matières',
                    href: '/matieres',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Une fois la filière Coranique/Franco-Arabe activée, le bulletin imprime AUTOMATIQUEMENT, sous " +
                        "chaque nom de matière, son intitulé en arabe si l'établissement l'a renseigné — un bloc arabe " +
                        "complet, écrit de droite à gauche, jamais un mélange mot à mot avec le français sur une même " +
                        "ligne. Aucune traduction automatique : le nom arabe est saisi tel quel par l'établissement.",
                    objectif:
                        "Servir les établissements franco-arabes ou coraniques dans les deux langues de leur " +
                        "enseignement, sur le même document officiel que les autres écoles, sans dupliquer le bulletin " +
                        "en deux versions distinctes à concilier.",
                    probleme:
                        "Un établissement franco-arabe qui ne peut imprimer ses matières qu'en français doit recopier un " +
                        "second bulletin à la main pour les familles qui lisent l'arabe — une double saisie, source " +
                        "d'écarts entre les deux versions.",
                    procedure: [
                        "Activez la filière Coranique/Franco-Arabe dans Paramètres › Modules & fonctionnalités (voir « Activer ou désactiver un module »).",
                        "Ouvrez Gestion Scolaire › Matières et, pour chaque matière concernée, renseignez son nom en arabe.",
                        "Une matière sans nom arabe imprime le bulletin sans son second nom, jamais une valeur inventée ou une case vide qui prendrait la place.",
                        "Générez le bulletin normalement (Gestion Scolaire › Notes et bulletins) : le bilinguisme s'applique automatiquement, sans case à cocher supplémentaire à l'édition.",
                        "Prévisualisez un bulletin témoin après la première saisie de noms arabes, pour vérifier l'alignement des deux blocs avant impression d'une classe entière."
                    ],
                    impacts: [
                        "Matières : le nom arabe est un champ de la fiche matière, au même titre que le nom français — il se corrige au même endroit.",
                        "Bulletin PDF : seul le document change ; la saisie des notes, les moyennes et les mentions restent identiques, quelle que soit la langue d'affichage des matières.",
                        "Module Pédagogie : le bilinguisme dépend du module Coran, pas de Pédagogie — désactiver Pédagogie masque Notes et Matières, mais ne désactive pas le bilinguisme si Coran reste actif."
                    ],
                    recommandations: [
                        "Renseignez les noms arabes en une seule fois pour tout le niveau plutôt que matière par matière au fil de l'eau : un bulletin à moitié bilingue paraît inachevé.",
                        "Faites relire l'orthographe arabe par une personne compétente avant la première édition de masse : l'application ne corrige ni ne traduit rien.",
                        "N'activez ce module que si l'établissement enseigne réellement en arabe : l'activer sans jamais renseigner de nom arabe n'apporte rien et encombre la fiche matière d'un champ vide."
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
                        "Définissez les seuils de mention dans Paramètres › Notation & mentions : libellé et moyenne minimale, exprimée sur vingt.",
                        "Saisissez les notes : les moyennes, les totaux et le rang se recalculent à chaque enregistrement.",
                        "Consultez la synthèse de la classe pour vérifier la cohérence d'ensemble avant le conseil.",
                        "APPRÉCIATIONS : le moteur du bulletin propose une distinction à partir de la moyenne générale (Félicitations à partir de 16, Tableau d'honneur à partir de 14, Encouragements à partir de 12) ; le conseil des professeurs la reprend, la corrige, ou choisit « Sans distinction » — sa décision prévaut TOUJOURS sur la proposition automatique, jamais l'inverse.",
                        "Renseignez la décision du conseil lorsque la période concernée l'exige.",
                        "Tant qu'aucune appréciation n'a été saisie, le bulletin imprime les cases vierges plutôt qu'une valeur inventée."
                    ],
                    impacts: [
                        "Barèmes hétérogènes : chaque note est ramenée au barème de référence avant pondération, de sorte qu'un 45/60 et un 18/24 pèsent identiquement.",
                        "Coefficients : ils déterminent le total des points et la moyenne générale ; une erreur s'y propage jusqu'au rang.",
                        "Rang : il se recalcule automatiquement dès qu'une note de la classe est modifiée.",
                        "Bulletin : mention, rang et appréciations y sont imprimés tels qu'ils sont calculés ou saisis. Une colonne T.H. (Tableau d'Honneur par matière, à partir de 14/20 ramené au barème du cycle) signale, matière par matière, les résultats qui s'en approchent.",
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
                    title: 'Encaissement au guichet, guichet rapide et reçus de paiement',
                    location: 'Comptabilité › Caisse (Encaissements)',
                    href: '/caisse',
                    roles: ['Directeur', 'Secrétariat', 'Finance'],
                    definition:
                        "Acte de caisse par lequel un versement de la famille est imputé sur le solde d'une inscription. " +
                        "L'inscription elle-même n'encaisse rien : tout versement, y compris le tout premier, se constate " +
                        "ici. Deux chemins mènent au même résultat. Le GUICHET RAPIDE — une modale qui s'ouvre d'elle-même " +
                        "dès qu'on sélectionne un élève ayant des frais échus — présente une case à cocher par frais dû, " +
                        "toutes cochées, et produit en un geste un unique reçu ventilé. Le PANNEAU DÉTAILLÉ, accessible " +
                        "derrière « Annuler », garde l'échéancier ligne à ligne, le règlement d'un montant libre et " +
                        "l'historique des versements, pour les cas particuliers. Chaque encaissement donne lieu à un reçu " +
                        "numéroté au format PDF, portant la mention réglementaire invitant les parents à conserver leur reçu.",
                    objectif:
                        "Constater sans délai tout versement, en délivrer la preuve à la famille, et tenir en permanence un solde " +
                        "exact par élève. Le guichet rapide répond au cas le plus courant — un tuteur qui vient régler, muni de " +
                        "la fiche du secrétariat, tout ce qui est dû à l'entrée de l'élève (inscription + tenue + premier mois) — " +
                        "en UN SEUL versement, donc UN SEUL reçu, là où enchaîner un « Régler » par ligne d'échéancier " +
                        "fabriquait plusieurs reçus séparés pour un montant que le secrétariat n'avait consigné qu'une fois.",
                    probleme:
                        "Le carnet à souches se perd, se recopie mal et ne se totalise qu'en fin de journée. Les " +
                        "contestations de paiement sont alors indémontrables dans un sens comme dans l'autre, et le " +
                        "rapprochement entre la caisse physique et le registre relève de la reconstitution. Et même sur écran, " +
                        "un guichet qui n'offre qu'un règlement ligne à ligne oblige la caissière à additionner de tête, puis à " +
                        "valider trois fois, pour un seul montant annoncé.",
                    procedure: [
                        "Ouvrez Comptabilité › Caisse (Encaissements) et ouvrez votre session de caisse en déclarant le fonds initial — sans session ouverte, ni la recherche d'élève ni le formulaire n'apparaissent.",
                        "Recherchez l'élève par son matricule ou par son nom. Chaque ligne de résultat porte un repère : « Aucun versement · Montant échu : X » (élève tout juste inscrit), « Frais du jour réglés · reste X », « Soldé », ou « Aucune inscription active ».",
                        "GUICHET RAPIDE : à la sélection d'un élève ayant un montant échu, la modale « Encaissement des frais dus » s'ouvre — en-tête élève, une case cochée par frais échu, montant perçu pré-rempli avec le total coché, mode de règlement.",
                        "Ajustez la sélection si besoin : décocher une ligne décoche AUSSI les suivantes (les frais se règlent dans l'ordre : inscription, tenue, puis mensualités). On ne peut donc régler qu'un bloc contigu depuis le plus ancien ; les lignes non cochées restent « En attente ».",
                        "Cliquez sur « Valider et imprimer le reçu » : un SEUL encaissement est enregistré, et le reçu porte une ligne par frais réglé (reçu ventilé). Un montant perçu différent du total coché est encaissé tel quel, sur un reçu à ligne unique.",
                        "« Annuler » referme la modale SANS rien encaisser et laisse le panneau détaillé accessible ; l'élève reste sélectionné et un bouton « ⚡ Encaisser les frais dus (XX XXX FCFA) » réapparaît sous le récapitulatif du solde pour rouvrir le guichet rapide, cases contiguës déjà cochées, sans re-rechercher l'élève.",
                        "PANNEAU DÉTAILLÉ (cas particuliers) : dans le formulaire, « Régler les échéances dues (X) » reporte en un montant tout ce qui est arrivé à échéance ; « Régler le solde intégral » reporte le reste à payer ; le bouton « Régler » de chaque ligne d'échéancier sert à un règlement partiel volontaire.",
                        "Le solde est mis à jour dans la transaction même : deux encaissements concurrents sur le même élève ne peuvent produire de sur-crédit — un message « le solde a changé entre-temps » invite à recharger.",
                        "Éditez le reçu PDF et remettez-le à la famille en attirant son attention sur la nécessité de le conserver.",
                        "CORRECTION : un encaissement erroné est ANNULÉ — statut « annulé », conservé dans l'historique —, jamais effacé. La saisie correcte est ensuite reprise."
                    ],
                    impacts: [
                        "Montant échu : le repère de la recherche et le guichet rapide affichent l'ENGAGEMENT INITIAL (frais ponctuels + premier mois), calculé par le serveur — jamais le cumul annuel, qui effraie sans informer.",
                        "Reçu ventilé : une ligne par poste réglé n'apparaît que si le montant perçu correspond EXACTEMENT au total coché et que chaque poste porte sa catégorie de frais ; sinon le reçu retombe sur sa ligne unique — un tableau qui ne balance pas serait un faux.",
                        "Imputation : le règlement s'applique aux échéances de la plus ancienne à la plus récente ; seul un préfixe des frais dus peut donc passer proprement à « Réglé », ce que reflète la sélection contiguë.",
                        "Solde et échéancier : le cumul des versements s'impute immédiatement ; les échéances honorées se soldent au fur et à mesure.",
                        "Recouvrement : l'élève sort automatiquement de la liste des débiteurs dès que son solde est apuré.",
                        "Clôture de caisse : chaque encaissement alimente le journal de la session ouverte ; seules les espèces entrent dans les espèces attendues au comptage.",
                        "Fausse manœuvre : le guichet rapide ne se ferme NI sur un clic à côté, NI sur la touche Échap — seuls « Annuler » et la croix ✕ le referment, pour ne pas perdre la saisie."
                    ],
                    recommandations: [
                        "N'encaissez jamais sans avoir ouvert votre session de caisse : le versement ne serait rattaché à aucune journée comptable.",
                        "Fiez-vous au montant échu affiché, pas au total dû annuel : c'est lui que le secrétariat a programmé à l'inscription et que la famille vient régler.",
                        "Le premier versement d'un élève se constate ici comme tous les autres : l'écran d'inscription ne prend aucun paiement et n'édite aucun reçu de caisse.",
                        "Si le parent ne règle qu'une partie aujourd'hui, décochez à partir de la première ligne qu'il ne paie pas — ou saisissez un montant libre : le reste demeure « En attente » pour un passage ultérieur.",
                        "Remettez systématiquement le reçu, même pour un versement partiel. C'est l'unique preuve dont dispose la famille.",
                        "Le service Finance ne modifie jamais un montant dû issu d'une inscription : il ne fait qu'y imputer des versements. Toute correction du montant dû relève du Secrétariat ou de la Direction.",
                        "En cas de micro-coupure réseau pendant l'envoi, l'application retente l'enregistrement en arrière-plan (bandeau « en attente d'envoi ») tant que l'onglet reste ouvert, sans jamais créer de doublon. Si la coupure persiste, rien n'est enregistré et vous en êtes averti : reprenez alors la validation vous-même.",
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
                    id: 'rapport-assiduite-detaille',
                    title: "Rapport d'assiduité détaillé",
                    location: 'Gestion Scolaire › Rapports',
                    href: '/rapports/assiduite',
                    roles: ['Directeur', 'Secrétariat', 'SuperAdmin'],
                    definition:
                        "Consolidation, sur une période choisie, de tous les appels soumis par les " +
                        "enseignants : taux de présence par classe, liste des élèves les plus absents, " +
                        "et détail exportable par élève.",
                    objectif:
                        "Détecter un décrochage naissant AVANT qu'il ne devienne irréversible, en rendant " +
                        "visible, classe par classe, ce qui reste invisible dans un cahier d'appel " +
                        "consulté séance après séance.",
                    probleme:
                        "Un absentéisme qui s'installe progressivement — un jour par-ci, un retard par-là " +
                        "— échappe à l'observation au fil de l'eau ; il ne se voit qu'une fois consolidé " +
                        "sur plusieurs semaines, et c'est précisément ce que fait ce rapport.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Rapports et choisissez la période à analyser.",
                        "Sélectionnez, si besoin, une classe précise pour affiner la lecture.",
                        "Consultez le taux de présence par classe et la liste des élèves les plus concernés par l'absentéisme.",
                        "Sur toute ligne présentant au moins un retard ou une absence sur la période affichée, cliquez sur « Convoquer » (Directeur et SuperAdmin uniquement) : la convocation s'ouvre avec un motif DÉJÀ PRÉ-REMPLI des chiffres réellement comptés — retard(s), absence(s) — sans ressaisie, et reste modifiable avant validation.",
                        "Exportez le détail pour le transmettre au conseil de classe ou l'annexer à un dossier de convocation."
                    ],
                    impacts: [
                        "Appel en classe : ce rapport ne fait que consolider les appels déjà soumis — un créneau non appelé n'y figure pas.",
                        "Convocations : le bouton « Convoquer » ouvre directement l'avis PDF depuis le bilan, sans passer par l'écran Convocations parent ; il n'apparaît qu'au croisement des rôles autorisés sur les deux écrans (Directeur, SuperAdmin) — le Secrétariat, qui a accès à ce rapport mais pas à l'écran Convocations parent, ne voit pas cette action.",
                        "Aucun seuil automatique : le rapport propose la donnée chiffrée, mais c'est toujours un agent qui décide de convoquer — aucune convocation n'est jamais générée par lot ni déclenchée d'elle-même."
                    ],
                    recommandations: [
                        "Consultez ce rapport à échéance régulière — chaque fin de mois, par exemple — plutôt qu'au moment du conseil de classe uniquement, où il est déjà tard pour agir.",
                        "Un taux de présence anormalement bas sur UNE seule classe trahit parfois un problème d'appel non fait, plus qu'un absentéisme réel — vérifiez avant d'alerter.",
                        "Relisez le motif pré-rempli avant de valider la convocation : les chiffres portent sur la période affichée à l'écran, pas sur l'année entière — ajustez la période avant de convoquer si le motif doit couvrir tout l'exercice."
                    ]
                },
                {
                    id: 'rapport-financiers-export',
                    title: 'Rapports financiers et export comptable',
                    location: 'Comptabilité › Rapports financiers',
                    href: '/rapports/financiers',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Consolidation avancée des recettes de l'établissement — ventilée par cycle, par " +
                        "classe et par mode de paiement — doublée d'un état de l'ancienneté des créances " +
                        "(débiteurs par tranche de retard). Réservé aux formules Standard et Premium.",
                    objectif:
                        "Fournir au comptable de l'établissement et au conseil d'administration un état " +
                        "des recettes détaillé et exportable, sans reconstitution manuelle à partir des " +
                        "reçus de caisse.",
                    probleme:
                        "Sans consolidation avancée, produire un état des recettes présentable à un conseil " +
                        "d'administration suppose de ressaisir, dans un tableur externe, des données déjà " +
                        "présentes dans l'application — une double saisie, source d'écarts.",
                    procedure: [
                        "Ouvrez Comptabilité › Rapports financiers.",
                        "Choisissez la période à consolider.",
                        "Consultez la ventilation des recettes par cycle, par classe et par mode de paiement, ainsi que l'état de l'ancienneté des créances.",
                        "Exportez l'état au format .xlsx : le fichier téléchargé porte EXACTEMENT les mêmes chiffres que l'écran, sur les mêmes bornes de période."
                    ],
                    impacts: [
                        "Caisse : chaque encaissement validé au guichet alimente cette consolidation.",
                        "Recouvrement : l'état de l'ancienneté des créances recoupe directement la liste des débiteurs du module Comptabilité.",
                        "Trésorerie : le tableau de bord Trésorerie donne une lecture rapide des mêmes flux ; ce rapport en donne le détail exportable et ventilé."
                    ],
                    recommandations: [
                        "Exportez et archivez ce rapport à chaque fin de trimestre : c'est la photographie de l'exercice à cette date, utile en cas de question a posteriori.",
                        "Ce module dépend de la formule d'abonnement de l'établissement (Standard ou Premium) : un compte qui n'y accède pas n'a rien à corriger, c'est une question d'abonnement, pas de droit."
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
                        "est clôturée le soir en déclarant les ESPÈCES RÉELLEMENT COMPTÉES dans le tiroir-caisse — cette " +
                        "saisie est obligatoire. L'application compare ce comptage aux espèces attendues (fonds initial + " +
                        "encaissements en espèces de la session, JAMAIS les virements, chèques ou mobile money, qui ne " +
                        "transitent jamais par le tiroir) et exige un motif dès que le compte n'y est pas — trop-perçu " +
                        "comme manquant.",
                    objectif:
                        "Arrêter chaque journée sur un chiffre incontestable, détecter tout manquant ou surplus le jour " +
                        "même plutôt qu'en fin de mois, et fixer la responsabilité de chaque caissier sur sa propre " +
                        "session. Pour la direction, c'est la protection la plus élémentaire contre les fuites de caisse ; " +
                        "pour le caissier lui-même, c'est la garantie qu'un manquant survenu ailleurs ou un autre jour ne " +
                        "pourra jamais lui être imputé, et que son explication reste attachée à sa propre clôture.",
                    probleme:
                        "Une caisse jamais comptée formellement à la clôture rend tout écart indétectable : lorsqu'un " +
                        "manquant apparaît en fin de mois, il devient impossible d'en déterminer le jour, l'opération ou " +
                        "l'agent responsable. Le soupçon se répand alors sur l'ensemble du service, faute d'avoir isolé " +
                        "l'écart à la journée où il s'est produit.",
                    procedure: [
                        "À l'ouverture du guichet, ouvrez votre session de caisse en déclarant le fonds de caisse initial — un bandeau l'impose avant tout encaissement.",
                        "Effectuez la journée d'encaissement : chaque versement est rattaché à cette session nominative, visible en temps réel dans le bandeau (total encaissé, nombre de versements).",
                        "En fin de journée, comptez physiquement le numéraire du tiroir-caisse AVANT d'ouvrir la clôture — ne regardez le chiffre attendu qu'après avoir compté, pour ne pas vous y aligner inconsciemment.",
                        "Cliquez sur « Clôturer la caisse » et saisissez le montant compté dans le champ dédié : ce champ est obligatoire, la clôture ne se valide pas sans lui.",
                        "Si le montant saisi ne correspond pas aux espèces attendues, un champ « Motif de l'écart » apparaît : il est obligatoire, qu'il s'agisse d'un manquant ou d'un surplus — décrivez la cause probable (rendu de monnaie, fonds d'ouverture mal compté…).",
                        "Confirmez : une session clôturée ne se rouvre plus, et ne se re-clôture jamais.",
                        "Téléchargez immédiatement le rapport de clôture au format PDF depuis la fenêtre de confirmation — il détaille désormais le solde théorique, le montant compté, l'écart et le motif.",
                        "Faites contresigner le rapport par la direction selon l'usage de l'établissement, et classez-le."
                    ],
                    impacts: [
                        "Encaissements : aucun versement ne peut être enregistré hors d'une session de caisse ouverte — la recherche d'élève et le formulaire restent masqués tant qu'elle ne l'est pas.",
                        "Espèces attendues : seuls les versements en ESPÈCES entrent dans ce calcul — un virement, un chèque ou un mobile money encaissé pendant la session ne modifie jamais le montant que le tiroir-caisse doit contenir.",
                        "Trésorerie : les recettes de la journée clôturée alimentent la position de trésorerie de l'établissement.",
                        "Rapports financiers : la consolidation des recettes repose sur les sessions clôturées.",
                        "Audit : l'ouverture et la clôture sont tracées avec leur auteur, leur horodatage, le montant compté et le motif d'écart éventuel.",
                        "Responsabilité : chaque session est nominative et porte son propre écart, ce qui circonscrit tout manquant à un agent et à une journée précis."
                    ],
                    recommandations: [
                        "Clôturez chaque jour, sans exception : une session laissée ouverte plusieurs jours ruine l'intérêt du dispositif.",
                        "Comptez le numéraire AVANT de saisir le montant compté et avant de regarder le total affiché à l'écran, afin de ne pas s'aligner inconsciemment sur le chiffre attendu.",
                        "Rédigez un motif d'écart factuel et vérifiable (« deux pièces de 500 rendues en trop à 14h ») plutôt qu'une formule vague : c'est ce motif, et lui seul, qu'un contrôle ultérieur pourra confronter aux faits.",
                        "N'encaissez jamais sous la session d'un collègue : la responsabilité — et l'écart constaté — en seraient faussés.",
                        "Conservez les rapports de clôture : ils constituent la pièce justificative de la comptabilité de caisse, écart compris.",
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
                        "facultative peut s'ajouter à l'un comme à l'autre, ainsi qu'un MOYEN DE PAIEMENT — espèces, virement " +
                        "bancaire, Wave ou Orange Money — et sa référence (numéro de compte ou de mobile money), distincts du " +
                        "moyen de paiement utilisé côté élèves en caisse. Le contrat est ACTIF jusqu'à sa clôture, qui " +
                        "n'efface jamais rien : un contrat clôturé demeure consultable, mais ne peut plus produire de " +
                        "nouvelle fiche de paie.",
                    objectif:
                        "Fixer, pour chaque membre du personnel, le régime, le montant et le canal de versement qui serviront " +
                        "de base à chaque fiche de paie, sans avoir à les ressaisir chaque mois. Pour la direction, c'est " +
                        "l'unique référence salariale de l'établissement, opposable en cas de litige et alignée sur ce qui " +
                        "figure réellement sur chaque bulletin remis.",
                    probleme:
                        "Un salaire négocié verbalement et jamais consigné se traduit, en fin de mois, par une fiche de " +
                        "paie approximative ou par un désaccord entre l'employé et l'établissement sur le montant convenu — " +
                        "et un moyen de paiement non consigné oblige à le redemander à chaque virement.",
                    procedure: [
                        "Ouvrez Comptabilité › Paie, onglet Contrats, puis « Nouveau contrat ».",
                        "Choisissez le type d'employé : un enseignant déjà fiché, ou un utilisateur non-enseignant (personnel administratif).",
                        "Sélectionnez le régime — Permanent ou Vacataire — puis renseignez le salaire de base ou le taux horaire selon le cas, et la prime de transport le cas échéant.",
                        "Renseignez le moyen de paiement de l'employé — espèces par défaut, ou virement bancaire, Wave, Orange Money avec la référence du compte correspondant.",
                        "Enregistrez : le contrat devient immédiatement sélectionnable pour la génération d'une fiche de paie.",
                        "RÉVISION : depuis la ligne du contrat, modifiez le salaire, le taux horaire, la prime ou le moyen de paiement, en indiquant obligatoirement le motif — une augmentation annuelle ou un changement de compte, par exemple.",
                        "CLÔTURE : en cas de départ, clôturez le contrat en précisant la date et le motif. Le contrat n'est jamais supprimé, seulement fermé à toute nouvelle fiche.",
                        "ATTESTATION DE TRAVAIL : téléchargez à tout moment le PDF de l'attestation, depuis la ligne du contrat concerné."
                    ],
                    impacts: [
                        "Fiches de paie : seul un contrat ACTIF (non clôturé) peut servir de base à une nouvelle fiche.",
                        "Heures des vacataires : le régime Vacataire conditionne l'accès au pointage des heures.",
                        "Déclarations fiscales : les charges sociales consolidées procèdent des fiches de paie, elles-mêmes adossées aux contrats.",
                        "Historique : toute révision de salaire, de taux horaire ou de moyen de paiement est conservée avec son auteur, sa date et son motif — un changement de moyen de paiement seul n'est jamais confondu avec un changement de rémunération."
                    ],
                    recommandations: [
                        "Enregistrez le contrat dès l'embauche, avant toute première fiche de paie : une fiche ne peut jamais précéder son contrat.",
                        "Motivez systématiquement une révision salariale : le motif est ce qui rend l'historique compréhensible des mois plus tard.",
                        "Clôturez sans délai le contrat d'un employé qui quitte l'établissement, pour prévenir toute fiche de paie émise par erreur.",
                        "Ne confondez jamais Permanent et Vacataire à la création : le régime choisi détermine les champs attendus lors de la génération de la fiche.",
                        "Vérifiez le numéro de compte ou de mobile money AVANT le premier versement : une référence erronée retarde le paiement de l'employé, pas seulement celui de la famille en caisse."
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
                        "Si le message « Aucun enseignant vacataire n'a de contrat actif » s'affiche : c'est qu'aucun CONTRAT de type Vacataire n'a encore été créé. Marquer un enseignant « Vacataire » sur sa fiche (statut STATEDUC) ne suffit pas — créez d'abord son contrat dans Comptabilité › Paie › Contrats.",
                        "Sélectionnez le contrat concerné, puis ajoutez chaque heure : date, nombre d'heures (les demi-heures sont admises) et une note facultative.",
                        "Consultez, filtré par mois et par année, le récapitulatif des heures déjà pointées pour ce contrat.",
                        "Téléchargez la fiche d'heures au format PDF si une pièce signée est requise.",
                        "Ouvrez ensuite l'onglet Fiches de paie et cliquez sur « Générer une fiche » : choisissez le contrat, le mois et l'année.",
                        "Pour un contrat Vacataire, cliquez sur « Suggérer les heures depuis le pointage » : l'application propose le total déjà pointé pour ce mois, et signale — pour information seulement — les jours où l'heure pointée diffère du créneau planifié à l'emploi du temps.",
                        "Un clic sur « Utiliser cette suggestion » reporte ce total dans le champ « Heures travaillées » ; la suggestion reste modifiable, et rien n'empêche de générer la fiche sans jamais l'avoir consultée.",
                        "Pour un contrat Permanent, le champ « Heures travaillées » reste sans objet — aucune suggestion ne s'y propose.",
                        "Validez : la fiche calcule le brut et le net, puis devient immédiatement imprimable en bulletin PDF."
                    ],
                    impacts: [
                        "Salaire du vacataire : le brut de sa fiche se calcule directement du produit des heures saisies par le taux horaire de son contrat — la suggestion n'écrit jamais ce champ toute seule, seul un clic explicite la reprend.",
                        "Contrat vs fiche enseignant : le sélecteur de Pointage Profs liste les CONTRATS Vacataire de la Paie, jamais le statut administratif « Vacataire » de la fiche STATEDUC — un enseignant sans contrat Vacataire n'y apparaît pas.",
                        "Emploi du temps : le rapprochement s'appuie sur les créneaux planifiés de l'enseignant ; un contrat lié à un utilisateur non-enseignant n'a pas d'emploi du temps de classe et ne reçoit que le total agrégé, sans rapprochement.",
                        "Déclarations fiscales : chaque fiche générée entre dans l'agrégat des charges sociales du mois correspondant.",
                        "Historique : les heures pointées restent consultables mois par mois, indépendamment des fiches déjà générées.",
                        "Bulletin PDF : le document remis à l'employé restitue exactement les montants calculés, sans reprise manuelle."
                    ],
                    recommandations: [
                        "Consultez la suggestion avant de saisir les heures à la main : elle repose sur le même pointage que la fiche d'heures PDF, donc sur les mêmes chiffres qu'un contrôle croisé retrouverait.",
                        "Un écart signalé entre heures pointées et emploi du temps n'est ni une erreur ni un blocage : vérifiez-le si le nombre paraît surprenant, mais la génération de la fiche reste possible sans y répondre.",
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
                    href: '/fiscalite',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "La déclaration fiscale consolide, pour un mois donné, les charges sociales et fiscales dues à " +
                        "l'État sur l'ensemble des fiches de paie déjà générées : cotisations de retraite (IPRES), de " +
                        "sécurité sociale (CSS), et les prélèvements VRS et BRS. Le même document y ajoute la TVA de la " +
                        "période — collectée, déductible et nette — de sorte que la déclaration totalise en une seule " +
                        "fois tout ce que l'établissement doit verser à l'État ce mois-là. L'écran dédié Fiscalité " +
                        "(/fiscalite) l'affiche en cartes ; le même document reste consultable depuis l'onglet " +
                        "Déclarations fiscales de Comptabilité › Paie.",
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
                        "Ouvrez Comptabilité › Fiscalité — ou, de façon équivalente, Comptabilité › Paie, onglet Déclarations fiscales.",
                        "Cliquez sur « Générer une déclaration », renseignez le mois et l'année, puis validez.",
                        "Consultez le détail par poste — IPRES, CSS, VRS, BRS — et le total dû à l'État qui en résulte.",
                        "Conservez ou transmettez ce récapitulatif pour la déclaration administrative effective auprès des organismes concernés."
                    ],
                    impacts: [
                        "Fiches de paie : une fiche générée APRÈS la déclaration du mois n'y figure pas — régénérez la déclaration si une fiche a été ajoutée en retard.",
                        "Contrats : un contrat clôturé en cours de mois continue de peser sur la déclaration via les fiches déjà émises pour lui.",
                        "Rapports financiers : les charges sociales consolidées éclairent la charge salariale totale de l'établissement.",
                        "TVA : la déclaration reprend la TVA collectée et déductible de la période, déjà connue de la comptabilité de l'établissement — elle ne la recalcule pas."
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
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'integration-etatique',
            number: 11,
            title: 'Intégration étatique — IEN, Planète, STATEDUC et mutations',
            icon: 'document',
            summary: "Les pièces et fichiers réglementaires dus au ministère : identifiant national de l'élève, export Planète, rapport annuel STATEDUC, certificat de mutation et livret de compétences.",
            concept:
                "Ce module produit les FICHIERS ET PIÈCES que l'établissement doit à l'administration, dans les formats " +
                "qu'elle attend. Il ne dialogue avec AUCUN système du ministère : à ce jour, aucune interface " +
                "informatique publique du SIMEN n'est ouverte. Unikol prépare donc des documents que vous transmettez " +
                "par la voie habituelle — dépôt, courriel, remise à l'IEF. Cette limite est affichée dans " +
                "l'application, et non masquée derrière un bouton qui échouerait : le jour où le ministère ouvrira " +
                "une interface, l'action de transmission apparaîtra d'elle-même. Le fil conducteur de tout le module " +
                "est l'IEN, l'identifiant qui suit l'élève d'un établissement à l'autre et par lequel l'administration " +
                "rattache un parcours à une personne.",
            articles: [
                {
                    id: 'ien-eleve',
                    title: "Identifiant National de l'Élève (IEN)",
                    location: 'Élèves › Fiche élève › Identifiant national',
                    href: '/eleves',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "L'IEN est le numéro attribué par l'administration centrale qui suit un élève sur tout son " +
                        "parcours scolaire, d'un établissement à l'autre. Il est DISTINCT du matricule, lequel est " +
                        "interne à votre établissement : deux écoles peuvent porter le même matricule pour deux élèves " +
                        "différents, jamais le même IEN. Le champ est facultatif dans Unikol — un élève fraîchement " +
                        "inscrit n'en a légitimement aucun tant que le ministère ne l'a pas délivré.",
                    objectif:
                        "Permettre à l'administration de rattacher un élève à son parcours antérieur lors d'un transfert, " +
                        "et à Unikol de produire des fichiers Planète et un rapport STATEDUC exploitables. C'est la clé " +
                        "de rapprochement de tous les échanges avec le ministère.",
                    probleme:
                        "Sans IEN, un élève transféré est un nouvel élève pour l'administration : sa scolarité antérieure " +
                        "est perdue, et l'établissement d'accueil ne peut ni vérifier son niveau réel, ni justifier son " +
                        "affectation. À l'échelle d'un fichier transmis, des lignes sans identifiant sont écartées du " +
                        "traitement national sans que l'école en soit avertie.",
                    procedure: [
                        "Ouvrez la fiche de l'élève, puis le champ « Identifiant national (IEN) ».",
                        "Saisissez le numéro OFFICIEL tel qu'il figure sur la liste transmise par l'IEF, puis enregistrez. C'est le mode normal, et le seul qui produise un identifiant opposable.",
                        "Si votre établissement n'a reçu aucun numéro, vous pouvez demander la génération d'un IEN PROVISOIRE : laissez le champ vide et utilisez l'action « Générer un numéro provisoire ».",
                        "Un numéro provisoire commence toujours par la lettre P et s'affiche partout avec la mention « provisoire ». Il permet de ne pas bloquer vos traitements internes en attendant le vrai numéro.",
                        "Dès réception du numéro officiel, saisissez-le : il remplace le provisoire, qui disparaît définitivement.",
                        "La génération d'un provisoire exige que le code établissement national soit renseigné dans Paramètres › Intégration étatique (SIMEN) : sans lui, le numéro fabriqué ne rattacherait l'élève à aucune école."
                    ],
                    impacts: [
                        "Export Planète : les lignes portant un IEN provisoire sont signalées comme telles dans le fichier, et le nombre d'identifiants fabriqués vous est annoncé AVANT le téléchargement.",
                        "Rapport STATEDUC : les élèves sans IEN sont comptés dans une ligne distincte, jamais fondus dans les effectifs déclarés.",
                        "Certificat de mutation : l'IEN y est imprimé, avec la mention « provisoire » le cas échéant — l'école d'accueil doit savoir ce qu'elle recopie.",
                        "Unicité : deux élèves de votre établissement ne peuvent pas porter le même IEN. Unikol refuse la saisie en nommant l'élève qui le détient déjà."
                    ],
                    recommandations: [
                        "Un IEN provisoire n'a AUCUNE valeur officielle : ne le communiquez jamais à un tiers comme s'il s'agissait d'un numéro délivré par le ministère.",
                        "Unikol contrôle la FORME du numéro saisi (longueur, caractères, clé de contrôle), ce qui attrape les fautes de frappe. Il ne peut pas vérifier son authenticité auprès du SIMEN : ce contrôle n'est pas une validation.",
                        "Un numéro provisoire ne peut jamais remplacer un IEN officiel déjà enregistré : cette perte serait irréversible, l'application la refuse.",
                        "Saisissez les IEN au fil de leur réception plutôt qu'en une seule campagne : c'est la condition pour que vos exports soient exploitables toute l'année."
                    ]
                },
                {
                    id: 'export-planete',
                    title: 'Export « Planète Ready » des élèves',
                    location: 'Intégration étatique › Export Planète',
                    href: '/integration-etatique',
                    roles: ['Directeur'],
                    definition:
                        "Un fichier récapitulant l'état civil scolaire de tous les élèves d'une année, au format " +
                        "d'échange attendu par le ministère. Disponible en CSV (ouvrable dans Excel) et en JSON. Il " +
                        "peut être restreint à une seule classe pour contrôle avant transmission globale.",
                    objectif:
                        "Vous éviter la ressaisie manuelle de plusieurs centaines d'élèves dans un tableur, et garantir " +
                        "que le fichier transmis porte exactement les colonnes attendues, dans l'ordre attendu.",
                    probleme:
                        "Un fichier constitué à la main diverge du format officiel à la première colonne oubliée ou " +
                        "renommée, et l'ensemble du lot est rejeté — plusieurs jours plus tard, sans explication " +
                        "détaillée. La correction impose alors de recommencer le fichier entier en pleine période de " +
                        "remontée.",
                    procedure: [
                        "Vérifiez d'abord que le code établissement national est renseigné dans Paramètres › Intégration étatique (SIMEN) : sans lui, l'export refuse de s'exécuter.",
                        "Ouvrez Intégration étatique › Export Planète et choisissez l'année scolaire concernée.",
                        "Choisissez le format : CSV pour un contrôle dans Excel, JSON si le portail du ministère le demande.",
                        "Lisez les compteurs affichés avant de télécharger : nombre d'élèves, nombre d'identifiants provisoires, nombre d'élèves sans IEN.",
                        "Si le nombre de provisoires ou de lignes sans IEN vous paraît élevé, renoncez au téléchargement et complétez d'abord les identifiants.",
                        "Téléchargez, ouvrez le fichier pour un dernier contrôle visuel, puis transmettez-le par la voie habituelle (dépôt, courriel, remise à l'IEF)."
                    ],
                    impacts: [
                        "Le fichier recense les élèves ayant une INSCRIPTION non annulée sur l'année demandée : un élève parti en janvier y figure, un élève préinscrit pour l'année suivante n'y figure pas.",
                        "La classe indiquée est celle de l'inscription, figée : un élève transféré en cours d'année apparaît dans la classe où il était inscrit.",
                        "Aucune donnée financière ne sort de l'établissement : le ministère reçoit un état civil scolaire, pas la situation de paiement des familles.",
                        "L'export est journalisé : la date et l'auteur de chaque extraction sont conservés."
                    ],
                    recommandations: [
                        "Les cases vides du fichier sont VOULUES : Unikol n'invente jamais une donnée manquante. Ne les complétez pas au hasard avant transmission — une donnée plausible et fausse est pire qu'une absence.",
                        "Ouvrez le CSV dans Excel sans le réenregistrer : une réécriture par le tableur peut modifier le format des dates et rendre le fichier inexploitable.",
                        "Faites un export limité à une classe pour valider le contenu avant de générer le fichier de tout l'établissement.",
                        "Cet export sort l'état civil de tous vos élèves : ne le transmettez qu'au destinataire officiel, et ne le laissez pas circuler par messagerie personnelle."
                    ]
                },
                {
                    id: 'rapport-stateduc',
                    title: 'Rapport annuel STATEDUC',
                    location: 'Intégration étatique › Rapport STATEDUC',
                    href: '/integration-etatique',
                    roles: ['Directeur'],
                    definition:
                        "L'état statistique annuel de l'établissement : effectifs par niveau, pyramide des âges, ratios " +
                        "filles/garçons, qualifications et statuts du personnel enseignant, infrastructures. Éditable en " +
                        "PDF (le formulaire à signer et déposer) et en classeur Excel (pour la consolidation à l'IEF).",
                    objectif:
                        "Produire en quelques secondes, à partir de vos données réelles, un état qui demande " +
                        "habituellement plusieurs jours de comptage manuel — et le produire de façon reproductible, " +
                        "année après année.",
                    probleme:
                        "Un comptage manuel est faux dès qu'un élève arrive ou part pendant sa réalisation, et personne " +
                        "ne peut le vérifier après coup. Les effectifs déclarés finissent par ne plus correspondre ni " +
                        "aux registres de l'école, ni à ceux du ministère, et l'écart n'est jamais explicable.",
                    procedure: [
                        "Ouvrez Intégration étatique › Rapport STATEDUC et choisissez l'année scolaire.",
                        "Renseignez la DATE D'OBSERVATION : c'est la date à laquelle les effectifs sont arrêtés et les âges calculés. Laissez la date du jour pour un état courant.",
                        "Consultez le rapport à l'écran et lisez en premier l'encadré « Données incomplètes », s'il apparaît.",
                        "Complétez les informations manquantes signalées (IEN, diplômes et genre des enseignants) puis régénérez le rapport.",
                        "Téléchargez le PDF, faites-le signer et revêtir du cachet, puis déposez-le auprès de l'IEF.",
                        "Téléchargez le classeur Excel si l'agent chargé de la consolidation vous le demande : il contient les mêmes chiffres, sous forme exploitable en formules."
                    ],
                    impacts: [
                        "Les effectifs comptés sont ceux des INSCRIPTIONS non annulées de l'exercice, et non les élèves présents en base au moment de l'édition.",
                        "Les âges sont calculés à la date d'observation : rééditer le rapport six mois plus tard avec la même date d'observation redonne exactement la même pyramide.",
                        "Est « qualifié » un enseignant porteur d'un diplôme PROFESSIONNEL (CEAP, CAP, CAEM, CAES). Un titulaire d'un Master sans titre pédagogique n'est pas compté comme qualifié : c'est la définition officielle du ministère.",
                        "Les enseignants archivés sont exclus : le formulaire décrit le personnel en poste, pas l'historique des passages.",
                        "Le PDF, l'Excel et l'écran sortent du même calcul : ils ne peuvent pas se contredire."
                    ],
                    recommandations: [
                        "Ne signez jamais un rapport sans avoir lu l'encadré « Données incomplètes » : il vous dit exactement ce qui manque, et c'est vous qui répondez du formulaire déposé.",
                        "Un taux de qualification faible peut décrire votre établissement OU l'état de votre saisie : le nombre d'enseignants sans diplôme renseigné est affiché juste à côté pour trancher.",
                        "Complétez le genre des enseignants avant l'édition : le formulaire officiel ventile tout le personnel en hommes/femmes, et Unikol refuse de deviner.",
                        "Notez la date d'observation retenue : c'est elle, et non la date d'impression, qui explique un écart entre deux exemplaires du même rapport.",
                        "Une tranche « âge non déterminé » signale des dates de naissance aberrantes, souvent issues d'un import : corrigez-les, ne les ignorez pas."
                    ]
                },
                {
                    id: 'certificat-mutation',
                    title: 'Certificat de mutation et livret de compétences',
                    location: 'Élèves › Fiche élève › Délivrer un certificat de mutation',
                    href: '/eleves',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Le CERTIFICAT DE MUTATION est la pièce officielle remise à l'élève qui quitte l'établissement, " +
                        "exigée par l'école d'accueil avant toute réinscription. Il porte un numéro officiel et un QR " +
                        "code de vérification. Le LIVRET DE COMPÉTENCES l'accompagne : il retrace le parcours de l'élève " +
                        "compétence par compétence, période par période, là où le bulletin ne note qu'une période.",
                    objectif:
                        "Permettre à l'école d'accueil de vérifier, en scannant le QR, que le certificat présenté est " +
                        "authentique et n'a pas été révoqué — et lui transmettre un état pédagogique exploitable dès " +
                        "l'arrivée de l'élève.",
                    probleme:
                        "Un certificat rédigé à la main ou dans un traitement de texte est infalsifiable par personne et " +
                        "vérifiable par personne. L'école d'accueil ne peut ni confirmer son origine, ni savoir qu'il a " +
                        "été annulé depuis. Sans livret, elle affecte l'élève sur la seule foi de son âge.",
                    procedure: [
                        "Ouvrez la fiche de l'élève, puis « Délivrer un certificat de mutation ».",
                        "Choisissez l'année scolaire concernée et le motif de la mutation. Si vous choisissez « Autre », la précision devient obligatoire.",
                        "Renseignez l'établissement et la localité de destination s'ils sont connus. Ces champs peuvent rester vides : le certificat reste valable sans destination nommée.",
                        "Validez. Le certificat est délivré, numéroté, enregistré, et le PDF s'ouvre immédiatement.",
                        "Imprimez-le, faites-le signer et cacheter, puis remettez-le au tuteur avec le livret de compétences.",
                        "En cas d'erreur, ne modifiez rien : RÉVOQUEZ le certificat et délivrez-en un nouveau."
                    ],
                    impacts: [
                        "Chaque délivrance consomme un numéro officiel de la série de l'établissement : deux clics produisent deux certificats distincts, jamais le même deux fois.",
                        "Le QR code renvoie vers une page de vérification qui répond « valide », « révoqué » ou « inconnu ». Il ne contient AUCUNE donnée de l'élève : un QR photographié sur un bureau est lisible par n'importe qui.",
                        "La classe indiquée est figée à la délivrance : un changement de classe ultérieur ne réécrit jamais un certificat déjà remis.",
                        "Le certificat mentionne si l'élève était à jour de ses frais AU JOUR de la délivrance — une phrase, jamais un montant.",
                        "Un certificat délivré n'est jamais modifiable : c'est ce qui garantit qu'il n'existe pas deux versions contradictoires du même numéro."
                    ],
                    recommandations: [
                        "Un solde impayé N'EMPÊCHE PAS la délivrance, et c'est volontaire : refuser un certificat de mutation à un élève débiteur revient à le retenir de force dans l'établissement, ce que la réglementation interdit. Poursuivez le recouvrement par les voies ordinaires.",
                        "Remettez toujours le livret de compétences avec le certificat : c'est ce que l'école d'accueil utilisera pour affecter l'élève au bon niveau.",
                        "Sur le livret, une case VIDE signifie « compétence non évaluée sur cette période » — jamais « non acquis ». Ne la complétez pas à la main avant remise.",
                        "Vérifiez que l'IEN figure bien sur le certificat avant de le remettre : c'est par lui que l'école d'accueil rattachera le dossier national de l'élève.",
                        "Conservez une copie du certificat remis : la version papier signée fait foi, et c'est elle que présentera la famille."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'inventaire',
            number: 12,
            title: 'Inventaire — Patrimoine, stock et prêts de matériel',
            icon: 'archive',
            summary: "Le registre des biens de l'établissement, son journal de mouvements et le suivi de ce qui est prêté.",
            concept:
                "Trois notions se succèdent et ne se recouvrent jamais. La CATÉGORIE et le BIEN décrivent " +
                "le patrimoine — ce que l'établissement possède, par lot plutôt que par unité individuelle. " +
                "Le MOUVEMENT DE STOCK est l'écriture qui fait varier une quantité : réception, sortie " +
                "définitive, ajustement d'inventaire. Le PRÊT est un mouvement d'un genre différent — la " +
                "quantité disponible baisse, mais le bien reste au patrimoine, car il doit revenir. Deux " +
                "invariants tiennent tout le module : la quantité disponible d'un bien ne s'écrit JAMAIS " +
                "directement, elle ne varie que dans la transaction d'un mouvement ; et le journal de stock " +
                "est APPEND-ONLY — une erreur de saisie se corrige par un mouvement inverse, jamais par une " +
                "modification ou une suppression de la ligne fautive.",
            articles: [
                {
                    id: 'patrimoine-categories',
                    title: 'Catégories et fiches de biens',
                    location: 'Inventaire › Catalogue',
                    href: '/inventaire',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Le catalogue organise le patrimoine de l'établissement en catégories — mobilier, " +
                        "matériel pédagogique, informatique, manuels scolaires — et en biens, chacun décrit " +
                        "par un état dominant (Neuf, Bon, À réparer, Hors service) et une quantité totale. Un " +
                        "bien est un LOT, pas une unité individuelle numérotée : une école qui veut " +
                        "distinguer 120 tables-bancs en bon état de 50 à réparer crée deux lots distincts.",
                    objectif:
                        "Disposer d'un inventaire fiable du patrimoine, opposable à un contrôle de l'IEF ou " +
                        "à un inventaire de fin d'année, sans dépendre d'un cahier tenu à la main dans un " +
                        "bureau. Pour la direction, c'est la base sur laquelle s'appuie tout arbitrage de " +
                        "renouvellement ou de réparation.",
                    probleme:
                        "Sans catalogue centralisé, le patrimoine d'une école se reconstitue de mémoire à " +
                        "chaque inventaire, les dotations reçues de l'État ou de la mairie ne sont tracées " +
                        "nulle part, et un vol ou une perte de matériel passe inaperçu faute de référence à " +
                        "laquelle le comparer.",
                    procedure: [
                        "Ouvrez Inventaire, puis créez d'abord les catégories dont l'établissement a besoin.",
                        "Pour chaque catégorie, ajoutez les biens qu'elle regroupe : nom, état dominant et quantité totale de départ.",
                        "Renseignez un état SINCÈRE — l'état réel du lot, pas l'état théorique du bien à l'achat.",
                        "La quantité totale saisie ici est le POINT DE DÉPART du journal de stock : toute variation ultérieure passe exclusivement par un mouvement, jamais par une correction directe de cette fiche.",
                        "Un bien devenu obsolète ou intégralement sorti est archivé, jamais supprimé : l'historique de ses mouvements reste consultable."
                    ],
                    impacts: [
                        "Mouvements de stock : chaque bien créé ici devient une cible de mouvement — réception, sortie, ajustement.",
                        "Prêts et attributions : seuls les biens du catalogue peuvent être prêtés à un bénéficiaire.",
                        "Rapport d'inventaire : la synthèse PDF de fin d'année part de ce catalogue, catégorie par catégorie."
                    ],
                    recommandations: [
                        "Créez les catégories avant les biens : un bien orphelin, sans catégorie, complique la lecture du rapport d'inventaire.",
                        "Ne créez pas un lot par salle si l'établissement ne compte pas en assurer le suivi séparément — la granularité choisie ici engage le fonctionnement du module pour toute son existence.",
                        "Un bien manifestement irréparable se marque « Hors service » plutôt que de rester « À réparer » indéfiniment : c'est cette distinction qui rend le rapport d'inventaire utile à la décision.",
                        "Archivez plutôt que de supprimer : aucune donnée de gestion n'est physiquement effacée dans l'application."
                    ]
                },
                {
                    id: 'mouvements-stock',
                    title: 'Journal de stock — entrées, sorties, ajustements',
                    location: 'Inventaire › Mouvements',
                    href: '/inventaire',
                    roles: ['Directeur', 'Secrétariat', 'Surveillant'],
                    definition:
                        "Le journal de stock enregistre chaque variation de quantité d'un bien : ENTRÉE " +
                        "(dotation, achat, don), SORTIE définitive (consommable distribué, transfert vers " +
                        "un autre établissement), et AJUSTEMENT positif ou négatif (recomptage après " +
                        "inventaire physique). Chaque ligne porte une quantité TOUJOURS positive — le sens " +
                        "de la variation tient au type de mouvement, jamais au signe du nombre.",
                    objectif:
                        "Rendre la quantité disponible d'un bien à tout instant EXACTE et JUSTIFIABLE : " +
                        "chaque variation porte une date, un auteur et un motif, de sorte qu'un contrôle " +
                        "puisse remonter du chiffre affiché jusqu'à l'écriture qui l'explique.",
                    probleme:
                        "Une quantité modifiée directement sur la fiche d'un bien — « on avait 40 tables, " +
                        "on en a compté 35, on corrige à 35 » — efface la trace de ce qui s'est réellement " +
                        "passé : perte, vol, casse ou simple erreur de comptage initial se confondent en un " +
                        "seul chiffre sans histoire, invérifiable un an plus tard.",
                    procedure: [
                        "Ouvrez Inventaire, onglet Mouvements, et sélectionnez le bien concerné.",
                        "Choisissez le type de mouvement : Entrée, Sortie, Ajustement positif ou Ajustement négatif.",
                        "Saisissez la quantité concernée — toujours un nombre positif, quel que soit le sens du mouvement — et un motif explicite.",
                        "Validez. La quantité disponible du bien est mise à jour dans la même transaction que l'écriture du mouvement : aucun écart n'est possible entre le journal et le solde affiché.",
                        "Une erreur de saisie NE SE CORRIGE PAS en modifiant la ligne : enregistrez un mouvement inverse qui compense l'écriture fautive, avec un motif qui le dit explicitement."
                    ],
                    impacts: [
                        "Catalogue : la quantité disponible affichée sur la fiche d'un bien est un SOLDE calculé depuis ce journal, jamais une valeur saisie directement.",
                        "Prêts : un prêt et son retour produisent chacun une écriture de ce même journal, avec un type dédié — le prêt ne baisse que la quantité disponible, jamais le total, car le bien reste au patrimoine.",
                        "Rapport d'inventaire : la synthèse de fin d'année recoupe le total, le disponible et l'historique des mouvements de la période."
                    ],
                    recommandations: [
                        "Motivez systématiquement un ajustement : un « Ajustement négatif » sans motif est aussi peu exploitable qu'une case vide.",
                        "Ne cumulez pas plusieurs corrections dans un seul mouvement : une ligne, une cause.",
                        "Faites un inventaire physique au moins une fois par an et enregistrez l'écart constaté par un ajustement — c'est ce recoupement régulier qui donne sa valeur au journal.",
                        "Le rôle applicatif de la base de données n'a que lecture et écriture SEULE sur ce journal (append-only) : ne cherchez jamais à faire corriger une ligne en base par un tiers technique, la voie normale est le mouvement inverse."
                    ]
                },
                {
                    id: 'prets-attributions',
                    title: 'Prêts et attributions de matériel',
                    location: 'Inventaire › Prêts',
                    href: '/inventaire',
                    roles: ['Directeur', 'Secrétariat', 'Surveillant'],
                    definition:
                        "L'attribution est le prêt d'un bien à un bénéficiaire — un enseignant, une classe, " +
                        "un membre du personnel — pour une durée déterminée ou indéterminée. Contrairement " +
                        "à une sortie définitive, un prêt ne retire jamais le bien du patrimoine : seule sa " +
                        "quantité DISPONIBLE diminue, jusqu'au retour qui la restaure.",
                    objectif:
                        "Savoir à tout instant qui détient quoi, sans dépendre d'un carnet de décharges " +
                        "manuscrites égaré au fond d'un tiroir, et produire une pièce écrite opposable en " +
                        "cas de litige sur la restitution d'un matériel.",
                    probleme:
                        "Le matériel prêté de la main à la main — un vidéoprojecteur pour une classe, un " +
                        "jeu de manuels pour l'année — se perd de vue dès que la personne qui l'a confié " +
                        "change de poste ou oublie l'accord verbal. L'établissement découvre alors, des " +
                        "mois plus tard, un matériel introuvable et personne pour en répondre.",
                    procedure: [
                        "Ouvrez Inventaire, onglet Prêts, et créez une nouvelle attribution.",
                        "Sélectionnez le bien et la quantité prêtée, puis désignez le bénéficiaire.",
                        "Validez. Une FICHE DE DÉCHARGE est générée au format PDF : faites-la signer par le bénéficiaire avant remise du matériel.",
                        "Au retour du matériel, ouvrez l'attribution et enregistrez le retour — total ou partiel.",
                        "La quantité disponible du bien est restaurée dans la même transaction que l'enregistrement du retour."
                    ],
                    impacts: [
                        "Journal de stock : le prêt et son retour produisent chacun une écriture datée et attribuée, consultable dans l'historique du bien.",
                        "Catalogue : un bien intégralement prêté affiche une quantité disponible à zéro, sans que sa quantité totale n'en soit affectée — il reste au patrimoine.",
                        "Rapport d'inventaire : les attributions en cours à la date du rapport y figurent, pour distinguer ce qui est disponible de ce qui est simplement prêté."
                    ],
                    recommandations: [
                        "Faites toujours signer la fiche de décharge avant de remettre le matériel : c'est la seule pièce qui protégera l'établissement en cas de contestation.",
                        "Enregistrez le retour LE JOUR MÊME où le matériel revient : un retour non saisi laisse un bien faussement indisponible pendant des semaines.",
                        "Un prêt qui n'est jamais retourné n'est pas une perte silencieuse : requalifiez-le en sortie définitive par un mouvement de stock, motivé, plutôt que de le laisser ouvert indéfiniment.",
                        "Pour un matériel de valeur (informatique, vidéoprojecteurs), préférez des attributions individuelles nommées à une attribution collective à « la classe de… », plus difficile à faire répondre en cas de litige."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'surveillance',
            number: 13,
            title: 'Vie scolaire — Appel, billets, discipline et convocations',
            icon: 'eye',
            summary: "La surveillance générale au quotidien : qui est présent, qui entre ou sort en dehors des horaires, et le suivi disciplinaire.",
            concept:
                "Quatre registres distincts couvrent la journée d'un élève, et il ne faut jamais les " +
                "confondre entre eux. L'APPEL constate qui est présent, absent ou en retard, classe par " +
                "classe et créneau par créneau — c'est un CONSTAT, renouvelé à chaque séance. Le BILLET " +
                "documente un mouvement individuel en dehors du rythme normal — une entrée tardive, une " +
                "sortie anticipée — et en trace la raison et, pour une sortie, la personne venue chercher " +
                "l'élève. Le REGISTRE DE DISCIPLINE sanctionne un FAIT déjà constaté — avertissement, " +
                "blâme, retenue, exclusion — et produit un procès-verbal. La CONVOCATION, enfin, n'est " +
                "pas une sanction mais un ENTRETIEN programmé avec un parent ou un tuteur, pour un motif " +
                "libre — discipline, assiduité, résultats — distinct d'un fait disciplinaire déjà acté.",
            articles: [
                {
                    id: 'appel-classe',
                    title: 'Appel en classe et feuille de présence',
                    location: 'Surveillance › Appel en classe',
                    href: '/presences',
                    roles: ['Enseignant', 'Directeur', 'Secrétariat'],
                    definition:
                        "L'appel constate, pour une classe, une matière et un créneau donnés, le statut de " +
                        "chaque élève inscrit : Présent, Absence justifiée, Absence injustifiée, ou Retard " +
                        "— ce dernier accompagné du nombre de minutes. Un enseignant ne voit et ne renseigne " +
                        "que les classes et matières pour lesquelles il est affecté.",
                    objectif:
                        "Constituer, séance après séance, l'historique d'assiduité qui fonde le rapport " +
                        "d'assiduité de la direction et, le cas échéant, l'alerte auprès de la famille d'un " +
                        "élève en décrochage.",
                    probleme:
                        "Un appel tenu sur un cahier de classe ne remonte jamais à la direction en temps " +
                        "réel : un absentéisme chronique se découvre en fin de trimestre, une fois le mal " +
                        "fait, au lieu d'être détecté dès les premières semaines où une intervention " +
                        "aurait encore un effet.",
                    procedure: [
                        "Ouvrez Surveillance › Appel en classe et sélectionnez la classe, la matière et le créneau.",
                        "La liste nominative des élèves inscrits s'affiche, chacun par défaut marqué Présent.",
                        "Modifiez le statut de chaque élève concerné : Absence justifiée, Absence injustifiée, ou Retard — en précisant alors le nombre de minutes.",
                        "Validez l'appel. La fiche est enregistrée avec son auteur et son horodatage.",
                        "Un appel déjà soumis reste consultable par la Direction et le Secrétariat, mais ne se ressaisit pas au même créneau : une correction passe par le rapport d'assiduité, non par un second appel."
                    ],
                    impacts: [
                        "Billets : un retard constaté à l'appel peut donner lieu à un billet d'entrée, produit séparément par la Surveillance.",
                        "Rapport d'assiduité : chaque appel soumis alimente directement le rapport détaillé par classe et par élève.",
                        "Convocations : un absentéisme répété visible sur plusieurs appels motive fréquemment une convocation de parent."
                    ],
                    recommandations: [
                        "Faites l'appel à chaque séance, sans exception : un rapport d'assiduité troué de créneaux non appelés ne dit rien de fiable sur l'élève qu'il est censé décrire.",
                        "Ne marquez « Absence justifiée » qu'en présence d'un motif réellement établi ; à défaut, « Absence injustifiée » et la régularisation vient ensuite, jamais l'inverse.",
                        "Un retard systématique du même élève à la même heure trahit souvent une contrainte de transport plutôt qu'une négligence — vérifiez avant de sanctionner."
                    ]
                },
                {
                    id: 'billets-entree-sortie',
                    title: "Billets d'entrée tardive et de sortie anticipée",
                    location: 'Surveillance › Billets d’entrée',
                    href: '/billets',
                    roles: ['Directeur', 'Surveillant'],
                    definition:
                        "Le billet documente un mouvement individuel d'élève en dehors des horaires " +
                        "normaux. Une ENTRÉE TARDIVE motive le retard et l'autorise ; une SORTIE ANTICIPÉE " +
                        "précise, en plus, qui est venu chercher l'élève — exigence de sécurité courante " +
                        "des établissements sénégalais — sauf autorisation écrite permettant une sortie " +
                        "seul. Chaque billet est imprimable au format A5.",
                    objectif:
                        "Donner au surveillant un motif tracé pour autoriser une entrée ou une sortie hors " +
                        "du rythme normal, et à la direction la preuve, en cas de question ultérieure, que " +
                        "le mouvement a été constaté et par qui.",
                    probleme:
                        "Un élève laissé entrer ou sortir sur la seule parole, sans trace écrite, met " +
                        "l'établissement en défaut de vigilance le jour où un parent conteste avoir " +
                        "autorisé le départ de son enfant, ou s'inquiète de ne pas savoir à quelle heure " +
                        "il est arrivé.",
                    procedure: [
                        "Ouvrez Surveillance › Billets d'entrée.",
                        "ENTRÉE TARDIVE : sélectionnez l'élève, renseignez le motif du retard, puis validez.",
                        "SORTIE ANTICIPÉE : sélectionnez l'élève, le motif, et la personne venue le chercher — ou l'autorisation écrite couvrant une sortie seul.",
                        "Imprimez le billet A5 généré et remettez-le à l'élève ou à la personne qui l'accompagne, selon l'usage de l'établissement.",
                        "Le billet reste consultable dans l'historique de l'élève, daté et attribué à son auteur."
                    ],
                    impacts: [
                        "Appel en classe : un élève entré tardivement doit être recompté Présent, non Absent, sur le créneau qu'il a rejoint.",
                        "Registre de discipline : des retards ou sorties répétés et injustifiés peuvent motiver un examen disciplinaire.",
                        "Convocations : un billet à motif inhabituel ou répété est souvent le premier signal qui déclenche une convocation de parent."
                    ],
                    recommandations: [
                        "N'autorisez une sortie anticipée qu'après avoir vérifié l'identité de la personne venue chercher l'élève, en l'absence d'autorisation écrite explicite.",
                        "Ne laissez jamais un élève sortir seul sans autorisation écrite préalable au dossier, même pour un motif qui paraît anodin.",
                        "Conservez le double du billet imprimé si l'établissement pratique la remise en main propre : c'est la pièce qui prouve, a posteriori, que le mouvement a été encadré."
                    ]
                },
                {
                    id: 'registre-discipline',
                    title: 'Registre de discipline et procès-verbal',
                    location: 'Surveillance › Registre Discipline',
                    href: '/discipline',
                    roles: ['Directeur', 'Surveillant'],
                    definition:
                        "Le registre de discipline consigne les sanctions prononcées contre un élève — " +
                        "Avertissement, Blâme, Retenue, Exclusion — pour un fait déjà constaté. Chaque " +
                        "entrée est datée, motivée, et donne lieu à un PROCÈS-VERBAL imprimable au format " +
                        "PDF.",
                    objectif:
                        "Constituer un dossier disciplinaire cohérent et daté, opposable devant un conseil " +
                        "de discipline ou une contestation de la famille, et permettre à la direction de " +
                        "distinguer un incident isolé d'une récidive.",
                    probleme:
                        "Une sanction décidée oralement, sans trace écrite datée, ne résiste à aucune " +
                        "contestation : impossible de prouver qu'un élève a déjà été averti pour un fait " +
                        "similaire, et le conseil de discipline se retrouve à statuer sans dossier.",
                    procedure: [
                        "Ouvrez Surveillance › Registre Discipline et créez une nouvelle entrée.",
                        "Sélectionnez l'élève, décrivez le fait constaté, et choisissez la sanction : Avertissement, Blâme, Retenue ou Exclusion.",
                        "Validez. Le procès-verbal est généré au format PDF, prêt à être imprimé et signé selon l'usage de l'établissement.",
                        "Consultez l'historique disciplinaire complet de l'élève avant de statuer sur un nouveau fait, pour apprécier une éventuelle récidive."
                    ],
                    impacts: [
                        "Bulletin de notes : les distinctions du conseil de classe (§ Notes & Bulletins) sont une décision PÉDAGOGIQUE distincte de ce registre — les deux ne s'alimentent pas automatiquement l'une l'autre.",
                        "Convocations : une sanction grave — retenue, exclusion — motive fréquemment une convocation de parent en complément du procès-verbal.",
                        "Dossier de l'élève : l'historique disciplinaire complet reste consultable depuis la fiche de l'élève."
                    ],
                    recommandations: [
                        "Décrivez le fait avec précision et sans jugement de valeur : c'est ce texte, et lui seul, qui devra convaincre un conseil de discipline ou une famille en désaccord.",
                        "Proportionnez la sanction à la gravité réelle du fait et à l'historique de l'élève — un registre qui ne distingue jamais premier incident et récidive perd sa valeur d'arbitrage.",
                        "Une exclusion se double toujours d'une convocation de parent : ne laissez jamais une sanction de cette gravité reposer sur le seul procès-verbal."
                    ]
                },
                {
                    id: 'convocations-parent',
                    title: 'Convocations de parent ou de tuteur',
                    location: 'Surveillance › Convocations parent',
                    href: '/convocations',
                    roles: ['Directeur', 'Surveillant'],
                    definition:
                        "La convocation programme un entretien avec le parent ou le tuteur d'un élève, " +
                        "pour un motif LIBRE — discipline, assiduité, résultats, tout autre sujet — et " +
                        "n'est donc pas restreinte aux seuls faits disciplinaires. C'est un entretien " +
                        "programmé, distinct d'une sanction déjà prononcée. Un avis de convocation est " +
                        "imprimable au format PDF pour remise à la famille. Une fois l'entretien tenu — ou " +
                        "manqué —, la convocation reçoit une SUITE, posée une seule fois : Honorée, Non " +
                        "honorée ou Reportée, avec un compte rendu obligatoire pour les deux derniers cas.",
                    objectif:
                        "Formaliser la prise de contact avec la famille sur un sujet qui le justifie, et en " +
                        "garder une trace datée — utile aussi bien pour suivre un élève en difficulté que " +
                        "pour documenter les démarches entreprises avant une décision plus lourde.",
                    probleme:
                        "Un appel téléphonique informel au parent, non tracé, ne laisse aucune preuve que " +
                        "l'établissement a effectivement cherché à alerter la famille avant d'aggraver une " +
                        "situation — un manque qui se révèle a posteriori, lors d'une contestation ou d'un " +
                        "conseil de discipline.",
                    procedure: [
                        "Ouvrez Surveillance › Convocations parent et créez une nouvelle convocation, ou utilisez le bouton « Convoquer » du bilan d'assiduité (Gestion Scolaire › Rapports), qui pré-remplit le motif à partir des chiffres de la période.",
                        "Sélectionnez l'élève, précisez le motif de l'entretien et la date proposée.",
                        "Validez. L'avis de convocation est généré au format PDF, à remettre à la famille par le canal habituel de l'établissement.",
                        "À l'issue de l'entretien, ouvrez la convocation et posez sa SUITE : Honorée (l'entretien a eu lieu, le compte rendu reste facultatif), Non honorée ou Reportée (le compte rendu devient alors OBLIGATOIRE).",
                        "Une suite ne se pose qu'UNE SEULE FOIS, et seulement depuis l'état Planifiée : une convocation déjà close ne peut pas être rouverte ni recevoir une seconde suite — c'est refusé plutôt qu'écrasé silencieusement."
                    ],
                    impacts: [
                        "Registre de discipline : une convocation née d'une sanction grave reste liée, dans le dossier de l'élève, au fait qui l'a motivée, même si les deux registres ne se confondent pas.",
                        "Appel en classe : un absentéisme répété visible dans l'historique de présence est un motif fréquent de convocation, et peut désormais convoquer directement depuis le bilan (voir « Rapport d'assiduité détaillé »).",
                        "Dossier de l'élève : l'historique des convocations reste consultable, daté, motivé et assorti de sa suite le cas échéant.",
                        "Registre : les convocations encore SANS SUITE remontent en tête de liste, quelle que soit leur date — une convocation planifiée puis oubliée reste donc visible en premier plutôt que de se noyer chronologiquement parmi les entretiens déjà tenus.",
                        "Avis PDF : il n'est JAMAIS réédité avec la suite — c'est la pièce remise AVANT l'entretien, elle ne peut pas porter un résultat qui n'existait pas encore au moment de son impression."
                    ],
                    recommandations: [
                        "N'attendez pas qu'une situation s'aggrave pour convoquer : une convocation précoce, sur un motif d'assiduité par exemple, prévient souvent une sanction disciplinaire ultérieure.",
                        "Formulez le motif clairement sur l'avis remis à la famille : un parent convoqué sans savoir pourquoi arrive à l'entretien sur la défensive.",
                        "Posez la suite le jour même de l'entretien — ou du rendez-vous manqué : une convocation sans suite reste en tête du registre et signale un dossier resté ouvert.",
                        "Rédigez un compte rendu factuel pour une convocation non honorée ou reportée, même bref : c'est la seule pièce qui montre, en cas de contestation ultérieure, que l'établissement a réellement cherché à rencontrer la famille."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'tresorerie',
            number: 14,
            title: 'Trésorerie — Décaissements et vision consolidée',
            icon: 'payment',
            summary: "Ce que l'établissement dépense, et la vue d'ensemble qui rapproche encaissements et décaissements sur une période.",
            concept:
                "La Caisse enregistre ce que les familles VERSENT à l'établissement ; la Trésorerie " +
                "enregistre ce que l'établissement lui-même DÉPENSE — salaires, maintenance, " +
                "fournitures, charges fixes, loyer, eau et électricité, télécoms, carburant, " +
                "assurances et honoraires. Le tableau de bord Trésorerie ne crée aucun nouveau " +
                "registre de mouvements : il AGRÈGE, sur une période choisie, les encaissements déjà " +
                "connus de la Caisse et les décaissements enregistrés ici, pour donner à la direction " +
                "une vision consolidée des entrées et des sorties.",
            articles: [
                {
                    id: 'decaissements',
                    title: 'Enregistrement des décaissements',
                    location: 'Comptabilité › Trésorerie',
                    href: '/tresorerie',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Un décaissement est une dépense de l'établissement, classée par catégorie — " +
                        "Salaires, Maintenance, Fournitures, Charges fixes, Loyer et charges, Eau et " +
                        "électricité, Télécoms et Internet, Carburant, Assurances et honoraires, ou " +
                        "Divers — datée et rattachée à un montant.",
                    objectif:
                        "Donner à la direction une image complète des sorties d'argent de l'établissement, " +
                        "ventilées par nature de dépense, sans dépendre d'une compilation manuelle de " +
                        "factures et de reçus dispersés.",
                    probleme:
                        "Sans registre centralisé des dépenses, l'établissement ne sait répondre à la " +
                        "question « combien avons-nous dépensé en maintenance ce trimestre ? » qu'en " +
                        "reconstituant, facture par facture, ce qui aurait dû être visible d'un coup " +
                        "d'œil.",
                    procedure: [
                        "Ouvrez Comptabilité › Trésorerie et enregistrez un nouveau décaissement.",
                        "Choisissez la catégorie de dépense la plus proche de la nature réelle de la sortie.",
                        "Renseignez le montant et la date effective de la dépense.",
                        "Validez. Le décaissement rejoint immédiatement l'agrégation affichée sur le tableau de bord Trésorerie."
                    ],
                    impacts: [
                        "Tableau de bord Trésorerie : chaque décaissement enregistré ici modifie instantanément la vue consolidée de la période.",
                        "Rapports financiers : la charge salariale et les charges fixes consolidées éclairent la lecture globale de la santé financière de l'établissement."
                    ],
                    recommandations: [
                        "Choisissez la catégorie la plus proche plutôt que « Divers » par facilité : une trésorerie où tout finit en « Divers » ne renseigne plus sur rien.",
                        "Enregistrez la dépense à sa date réelle, et non à la date de sa saisie dans l'application : c'est cette date qui situe la dépense dans la bonne période lors d'une consultation ultérieure.",
                        "Conservez la pièce justificative papier de chaque décaissement significatif : l'application trace le montant et la catégorie, pas la facture elle-même."
                    ]
                },
                {
                    id: 'tableau-bord-tresorerie',
                    title: 'Tableau de bord Trésorerie',
                    location: 'Comptabilité › Trésorerie',
                    href: '/tresorerie',
                    roles: ['Directeur', 'Finance'],
                    definition:
                        "Vue consolidée, sur une période choisie, des encaissements déjà connus du module " +
                        "Caisse et des décaissements enregistrés dans ce module — sans créer ni dupliquer " +
                        "aucun mouvement : c'est une AGRÉGATION en lecture, pas un registre de plus.",
                    objectif:
                        "Répondre en un coup d'œil à la question centrale de toute direction : sur cette " +
                        "période, qu'est-il entré, qu'est-il sorti, et quel est le solde qui en résulte — " +
                        "sans avoir à consulter séparément la Caisse et un tableur de dépenses.",
                    probleme:
                        "Sans vue consolidée, la santé financière courante de l'établissement se juge au " +
                        "montant en caisse à l'instant présent — une photographie trompeuse qui ne dit rien " +
                        "de la tendance, ni de ce qui a réellement été dépensé sur la période.",
                    procedure: [
                        "Ouvrez Comptabilité › Trésorerie.",
                        "Choisissez la période à consulter — par défaut, du premier jour du mois courant à aujourd'hui.",
                        "Lisez le total des encaissements et celui des décaissements sur cette période, ainsi que le solde qui en résulte.",
                        "Changez la période pour comparer un mois à un autre, ou pour couvrir un trimestre entier."
                    ],
                    impacts: [
                        "Caisse : tout encaissement validé au guichet apparaît immédiatement dans l'agrégation, sans ressaisie.",
                        "Décaissements : toute dépense enregistrée dans ce même module y apparaît de façon symétrique.",
                        "Rapports financiers : le tableau de bord Trésorerie donne une lecture rapide, les rapports financiers avancés en donnent le détail exportable."
                    ],
                    recommandations: [
                        "Consultez le tableau de bord à intervalle régulier plutôt qu'au gré des inquiétudes : une tendance se voit sur plusieurs relevés, jamais sur un seul.",
                        "Un solde négatif sur une courte période n'est pas nécessairement alarmant — une grosse dépense ponctuelle (maintenance lourde, par exemple) peut l'expliquer ; vérifiez la nature des décaissements avant de conclure."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'internat',
            number: 15,
            title: "Internat — Régime d'hébergement et affectation de chambre",
            icon: 'bed',
            summary: "Le régime d'hébergement de chaque élève (externe, demi-pensionnaire, interne), l'affectation aux dortoirs et le tableau de bord d'occupation.",
            concept:
                "Le régime d'hébergement d'un élève — Externe, Demi-pensionnaire ou Interne — se décide à l'inscription et se " +
                "reconfirme CHAQUE année, exactement comme le redoublement : c'est une information portée par l'INSCRIPTION, " +
                "pas par la fiche permanente de l'élève, parce qu'un interne une année peut redevenir externe la suivante " +
                "sans que rien n'efface son passé. L'Internat ne crée AUCUNE nouvelle notion de local : il réutilise les " +
                "bâtiments et salles déjà déclarés dans Infrastructures, en leur donnant le type « Dortoir », et compte la " +
                "capacité au niveau de la CHAMBRE — jamais lit par lit, aucune entité « Lit » distincte n'existe. Le module " +
                "est désactivé PAR DÉFAUT, à l'inverse de Pédagogie et Finance : seul un établissement qui héberge " +
                "réellement des élèves l'active depuis Paramètres. Une fois actif, chaque régime Interne facturé porte une " +
                "ligne de pension distincte, qui suit la même règle que tout montant issu d'une inscription — elle ne se " +
                "retire jamais silencieusement, même quand l'élève quitte l'internat en cours d'année.",
            articles: [
                {
                    id: 'activation-dortoirs',
                    title: 'Activer le module et déclarer les dortoirs',
                    location: 'Paramètres › Modules & fonctionnalités, puis Bâtiments & Salles',
                    href: '/parametres?tab=modules',
                    roles: ['Directeur'],
                    definition:
                        "Section « Internat » de Paramètres › Modules & fonctionnalités : un interrupteur, désactivé par " +
                        "défaut, qui rend le module visible ou invisible pour tout le personnel de l'établissement. Une " +
                        "fois activé, les DORTOIRS se déclarent comme des salles ordinaires depuis Bâtiments & Salles, " +
                        "avec le type « Dortoir » et une capacité en nombre de places — exactement le même écran que pour " +
                        "une salle de classe ou un bureau, aucun écran séparé.",
                    objectif:
                        "Réserver le module aux établissements qui hébergent réellement des élèves, sans imposer à tous " +
                        "les autres un menu et un vocabulaire — pension, dortoir, régime — qui ne les concernent pas ; et " +
                        "réutiliser l'inventaire des locaux déjà tenu par Infrastructures plutôt que de faire ressaisir " +
                        "une seconde cartographie des bâtiments.",
                    probleme:
                        "Un module actif par défaut pour toutes les écoles ferait apparaître un menu « Internat » et une " +
                        "section « Régime & Hébergement » sur le formulaire d'inscription même pour un externat pur — une " +
                        "complexité et une question posée à chaque famille qui n'a simplement pas lieu d'être. À " +
                        "l'inverse, sans réutiliser les salles déjà déclarées, un établissement qui héberge des élèves " +
                        "devrait tenir deux inventaires de locaux en parallèle, l'un pour les classes, l'autre pour les " +
                        "dortoirs, aussitôt sujets à diverger.",
                    procedure: [
                        "Ouvrez Paramètres › Modules & fonctionnalités et activez l'interrupteur « Internat », réservé au Directeur.",
                        "Ouvrez ensuite Gestion Scolaire › Bâtiments & Salles et créez, si besoin, un bâtiment dédié à l'hébergement (« Pavillon Garçons », « Pavillon Filles »).",
                        "Ajoutez ses salles comme d'habitude, mais choisissez le type « Dortoir » et renseignez une capacité SINCÈRE — le nombre de lits réellement disponibles dans la chambre.",
                        "Aucune autre déclaration n'est nécessaire : dès qu'au moins un dortoir existe, l'écran Internat et la section « Régime & Hébergement » de l'inscription s'ouvrent d'eux-mêmes.",
                        "Désactiver le module par la suite masque le menu et bloque toute nouvelle affectation (l'API la refuse en 422), mais ne supprime ni les dortoirs ni l'historique des élèves déjà hébergés."
                    ],
                    impacts: [
                        "Bâtiments & Salles : un dortoir reste une salle comme les autres — il compte dans le total de capacité de l'établissement et peut être archivé comme n'importe quelle salle devenue inutilisable.",
                        "Menu et formulaire d'inscription : la visibilité du lien « Internat » et de la section « Régime & Hébergement » suit l'interrupteur côté client, par confort d'affichage — la garde réelle est posée côté serveur et vaut pour tous les rôles, y compris un accès direct par adresse.",
                        "Capacité : elle se compte au niveau de la CHAMBRE (occupants actifs comparés à la capacité de la salle), jamais lit par lit — aucune réservation nominative d'un lit précis n'existe."
                    ],
                    recommandations: [
                        "Activez le module et déclarez vos dortoirs AVANT la campagne d'inscription : une section « Régime & Hébergement » qui apparaît en cours de campagne oblige à revenir sur des dossiers déjà saisis.",
                        "Ne créez pas un dortoir par lit : la granularité du module est la chambre, pas la place individuelle — un suivi plus fin resterait à tenir hors de l'application.",
                        "Distinguez clairement, dans le nom du bâtiment ou de la salle, un dortoir d'une salle de classe ordinaire : c'est ce nom qui apparaît sur le tableau de bord Internat et sur la fiche de l'élève."
                    ]
                },
                {
                    id: 'regime-hebergement-inscription',
                    title: "Régime d'hébergement à l'inscription et pension",
                    location: 'Gestion Scolaire › Inscriptions — section Régime & Hébergement',
                    href: '/inscriptions',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Le formulaire d'inscription — et de réinscription — porte une section « Régime & Hébergement », " +
                        "visible dès qu'au moins un dortoir existe : Externe, Demi-pensionnaire ou Interne. Choisir " +
                        "Interne ouvre un sélecteur de chambre parmi les dortoirs ayant encore de la place ; le régime " +
                        "est enregistré sur l'INSCRIPTION de l'année, jamais sur la fiche permanente de l'élève. Un " +
                        "élève en régime Interne se voit ajouter automatiquement une ligne de PENSION à son échéancier, " +
                        "dès lors qu'une catégorie de frais est marquée comme frais de pension dans le barème de la " +
                        "classe.",
                    objectif:
                        "Capturer le régime d'hébergement au même geste que l'inscription elle-même, pour que la pension " +
                        "soit facturée sans oubli dès le premier jour et que le tableau de bord Internat reflète, dès la " +
                        "rentrée, l'occupation réelle des dortoirs — sans dépendre d'une déclaration séparée, faite à un " +
                        "autre moment par un autre service.",
                    probleme:
                        "Un régime d'hébergement suivi à part — un cahier tenu par le surveillant général, une case " +
                        "cochée sur un formulaire papier — se désynchronise inévitablement de la facturation : un élève " +
                        "devenu interne en cours d'année continue d'être facturé en externe, ou l'inverse, jusqu'à ce " +
                        "qu'une famille s'étonne du montant réclamé.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Inscriptions (première inscription ou réinscription) et sélectionnez la classe comme d'habitude.",
                        "Dans la section « Régime & Hébergement », choisissez Externe, Demi-pensionnaire ou Interne.",
                        "Pour Interne, choisissez le dortoir dans la liste : seuls les dortoirs ayant encore de la place s'y proposent, avec leur occupation courante.",
                        "Validez l'inscription : si une catégorie de frais de pension existe dans le barème de la classe, sa ligne s'ajoute d'elle-même au montant dû — rien à cocher séparément.",
                        "CHAQUE ANNÉE, à la réinscription, reconfirmez le régime : il n'est JAMAIS reconduit automatiquement d'un exercice sur l'autre, au même titre que le redoublement.",
                        "Un changement de régime ou de chambre EN COURS D'ANNÉE se fait depuis le tableau de bord Internat, pas en rouvrant le formulaire d'inscription (voir la fiche « Tableau de bord par chambre, affectation, transfert et libération »)."
                    ],
                    impacts: [
                        "Échéancier : la ligne de pension suit exactement la règle des autres lignes de frais — figée à la validation, jamais modifiée en silence ; une correction relève du Secrétariat ou de la Direction et reste historisée.",
                        "Fiche élève : un badge d'hébergement rappelle le régime en cours et, pour un Interne, sa chambre.",
                        "Libération : faire repasser un élève en Externe, ou le changer de chambre, ne retire JAMAIS la ligne de pension déjà facturée — elle correspond à une période réellement vécue à l'internat.",
                        "Capacité : un dortoir déjà complet n'apparaît plus dans la liste des chambres disponibles proposée par ce formulaire."
                    ],
                    recommandations: [
                        "Ne laissez jamais un élève en régime Interne sans dortoir attribué : le formulaire l'exige, mais vérifiez la cohérence si un dortoir a été archivé après le choix initial.",
                        "Vérifiez le barème de la classe AVANT la campagne d'inscription : sans catégorie de frais marquée « pension », un régime Interne validé n'ajoute aucun montant, ce qui n'est visible qu'après coup.",
                        "Un changement de régime en cours d'année se traite depuis le tableau de bord Internat, jamais en modifiant l'inscription d'origine.",
                        "Rappelez-vous que la reconduction n'est jamais automatique : une famille qui pensait son enfant reconduit en Interne peut se retrouver, faute de reconfirmation, en Externe à la rentrée suivante."
                    ]
                },
                {
                    id: 'tableau-bord-affectation-internat',
                    title: 'Tableau de bord par chambre, affectation, transfert et libération',
                    location: 'Internat',
                    href: '/internat',
                    roles: ['Directeur', 'Secrétariat', 'Surveillant'],
                    definition:
                        "Écran Internat : un tableau de bord qui liste chaque dortoir avec son occupation courante " +
                        "(élèves actifs comparés à sa capacité), une recherche d'élève par nom ou matricule, et une " +
                        "modale d'affectation rapide qui traite en un seul geste l'affectation, le TRANSFERT vers un " +
                        "autre dortoir, et la LIBÉRATION (retour au régime Externe).",
                    objectif:
                        "Donner au personnel de l'internat — Directeur, Secrétariat, Surveillant — un point unique pour " +
                        "savoir qui loge où à l'instant présent, et pour agir sur une affectation sans devoir rouvrir le " +
                        "dossier d'inscription complet de l'élève à chaque mouvement de chambre.",
                    probleme:
                        "Sans tableau de bord dédié, l'occupation réelle des dortoirs ne vit que dans la mémoire du " +
                        "surveillant général : un transfert décidé oralement ne se retrouve nulle part, une chambre " +
                        "présentée comme complète peut en réalité avoir de la place, et un dépassement de capacité ne se " +
                        "découvre qu'au moment où un lit manque physiquement.",
                    procedure: [
                        "Ouvrez Internat : chaque dortoir apparaît avec son nom, sa capacité et son occupation courante.",
                        "Recherchez un élève par nom ou par matricule pour retrouver sa chambre actuelle, ou pour l'affecter s'il est encore en régime Externe ou Demi-pensionnaire.",
                        "Ouvrez la modale d'affectation rapide : elle propose selon le cas d'AFFECTER l'élève à un dortoir, de le TRANSFÉRER vers un autre, ou de le LIBÉRER (retour à Externe).",
                        "Un dortoir déjà complet n'accepte aucune nouvelle affectation : le message l'indique explicitement plutôt que de laisser dépasser la capacité déclarée.",
                        "Validez : l'occupation du ou des dortoirs concernés se met à jour immédiatement sur le tableau de bord.",
                        "Si deux agents modifient la même affectation au même instant, le second à valider reçoit un refus explicite plutôt qu'un écrasement silencieux du premier — rouvrez simplement la fiche pour repartir de l'état à jour."
                    ],
                    impacts: [
                        "Fiche élève : le badge d'hébergement et la chambre affichée se mettent à jour au même instant que le tableau de bord.",
                        "Comptabilité : la libération d'un élève (retour à Externe) ne retire JAMAIS la ligne de pension déjà facturée sur son échéancier — voir la fiche « Régime d'hébergement à l'inscription et pension ».",
                        "Module désactivé : si le Directeur désactive l'Internat depuis Paramètres, toute tentative d'affectation ou de transfert est refusée par le serveur, quel que soit l'écran d'où elle provient — la garde ne dépend jamais du seul masquage du menu.",
                        "Rôles : ce tableau de bord est ouvert au même trio que les convocations de parent — Directeur, Secrétariat, Surveillant — un Enseignant ou un compte Finance n'y a pas accès."
                    ],
                    recommandations: [
                        "Traitez tout mouvement de chambre — même décidé oralement dans l'urgence — depuis cette modale le jour même : c'est elle, et elle seule, qui fait foi de l'occupation réelle.",
                        "Ne tenez pas de registre papier parallèle une fois le module actif : les deux sources finiraient par diverger, et c'est le tableau de bord qui doit rester la référence unique.",
                        "Un dortoir affiché complet qui ne devrait pas l'être signale presque toujours une libération oubliée plutôt qu'une erreur de capacité — vérifiez les occupants avant de revoir la capacité déclarée à la hausse.",
                        "En cas de refus lors d'une modification concurrente, ne réessayez pas mécaniquement : rouvrez la fiche de l'élève pour voir qui a modifié l'affectation entretemps."
                    ]
                }
            ]
        },
        // ═══════════════════════════════════════════════════════════════════════════════════
        {
            id: 'reperes-interface',
            number: 16,
            title: "Repères d'interface — onglets, filtres par année et sélecteurs",
            icon: 'menu',
            summary: "Les repères communs à tous les écrans : la barre d'onglets défilante, le filtrage par année active et les listes déroulantes dynamiques.",
            concept:
                "Un même vocabulaire visuel se retrouve d'un écran à l'autre, et le connaître une fois dispense de le " +
                "réapprendre partout. Trois repères comptent. La BARRE D'ONGLETS HORIZONTALE (une pastille bleue pleine " +
                "marque l'onglet actif) organise les sections d'un module — Paramètres, Examens, Inventaire, fiche élève — " +
                "et défile quand elle dépasse la largeur de l'écran, sans jamais casser la mise en page. Le FILTRE PAR " +
                "ANNÉE ACTIVE borne ce qu'on voit à l'exercice de travail courant, rappelé en permanence dans l'en-tête ; " +
                "quand une liste paraît vide, c'est presque toujours ce filtre qu'il faut interroger avant tout. Les " +
                "SÉLECTEURS DYNAMIQUES, enfin, se peuplent à partir des données déjà saisies : une classe absente d'un menu " +
                "est une classe qui n'a pas encore été créée, pas un défaut de l'écran.",
            articles: [
                {
                    id: 'barres-onglets-defilantes',
                    title: "Barre d'onglets horizontale défilante",
                    location: 'Présente sur Paramètres, Examens, Inventaire, fiche élève, et la plupart des modules',
                    href: '/parametres',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "Une seule et même barre d'onglets équipe désormais tous les écrans à sections : une rangée de " +
                        "pastilles horizontales, l'onglet actif en bleu Unikol plein sous texte blanc, les autres en gris " +
                        "discret. Lorsque les onglets dépassent la largeur disponible — sur mobile, sur tablette, ou quand ils " +
                        "sont nombreux comme aux Paramètres —, la rangée devient DÉFILANTE : pas de retour à la ligne, pas de " +
                        "barre de défilement visible, un dégradé d'estompement et une flèche apparaissent du seul côté encore " +
                        "masqué.",
                    objectif:
                        "Présenter les sections d'un module de façon identique partout, pour qu'un utilisateur qui a compris " +
                        "l'écran Paramètres retrouve le même geste sur Examens ou sur la fiche élève. Le défilement garantit " +
                        "qu'aucun onglet n'est jamais inaccessible, même sur un petit écran, sans que la barre ne repousse le " +
                        "contenu vers le bas.",
                    probleme:
                        "Des barres d'onglets toutes différentes — ici un menu latéral, là des onglets qui passent à la ligne " +
                        "et doublent la hauteur de l'en-tête, ailleurs des onglets simplement coupés au bord de l'écran — " +
                        "obligent à réapprendre la navigation écran par écran, et cachent des sections entières sur mobile.",
                    procedure: [
                        "Repérez l'onglet actif à sa pastille bleue pleine ; les autres sections sont en gris.",
                        "Si un dégradé et une flèche apparaissent à droite ou à gauche de la barre, c'est que d'autres onglets sont hors champ de ce côté.",
                        "Faites défiler la barre : à la molette (le défilement vertical devient horizontal au survol de la barre), en la faisant glisser à la souris, avec les flèches [‹] [›], ou d'un geste tactile horizontal.",
                        "Changez d'onglet d'un clic : l'onglet choisi se recentre automatiquement dans la barre, et le contenu se met à jour sans recharger la page.",
                        "Aux Paramètres, l'onglet ouvert est ajouté à l'adresse (?tab=…) : ce lien rouvre directement la même section, et peut être transmis tel quel.",
                        "Sur la fiche élève, la même barre sépare « Historique scolaire », « Notes & bulletins » et « Paiements » — ce dernier onglet n'apparaît pas pour un compte Enseignant."
                    ],
                    impacts: [
                        "Paramètres : les onglets réservés au Directeur (Facturation, Utilisateurs & rôles, Journal d'audit, Notifications SMS) ne s'affichent pas pour les autres rôles — la barre est plus courte, ce n'est pas une anomalie.",
                        "Adresse : seul l'écran Paramètres synchronise l'onglet actif dans l'URL ; ailleurs, changer d'onglet ne modifie pas le lien de la page.",
                        "Mise en page : la barre ne pousse jamais le contenu — au pire elle défile — donc un onglet manquant à l'écran est toujours atteignable, jamais perdu.",
                        "Exceptions assumées : l'interrupteur « Inscrits cette année / Non inscrits / Tous les élèves » de la liste Élèves garde ses couleurs sémantiques (vert / rouge / bleu), et la vitrine publique a son propre style — ni l'un ni l'autre ne suit ce composant."
                    ],
                    recommandations: [
                        "Sur un petit écran, réflexe utile : si une section attendue manque, faites défiler la barre d'onglets avant de conclure qu'elle n'existe pas.",
                        "Pour renvoyer un collègue vers un réglage précis, copiez l'adresse de la page Paramètres une fois le bon onglet ouvert : elle porte l'onglet.",
                        "La molette agit sur la barre uniquement quand le pointeur est dessus : pour faire défiler la page, éloignez le pointeur de la barre d'onglets."
                    ]
                },
                {
                    id: 'filtres-annee-active-et-selecteurs',
                    title: "Filtre par année active, états vides et sélecteurs dynamiques",
                    location: 'En-tête de l\'application, listes Élèves / Classes / Notes, et tous les menus déroulants',
                    href: '/eleves',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "L'ANNÉE ACTIVE est le contexte implicite de tout le travail quotidien : elle est rappelée en permanence " +
                        "dans l'en-tête, et la plupart des listes s'y bornent d'office. La liste Élèves l'expose par un " +
                        "interrupteur à trois positions — « Inscrits cette année » (défaut), « Non inscrits », « Tous les " +
                        "élèves ». Les MENUS DÉROULANTS (classe, matière, trimestre, enseignant…) sont DYNAMIQUES : ils ne " +
                        "listent que ce qui a déjà été saisi, et se referment collés au champ lorsqu'ils s'ouvrent vers le " +
                        "haut, faute de place en dessous.",
                    objectif:
                        "Épargner à chaque agent de préciser l'exercice à chaque écran, tout en lui laissant un moyen explicite " +
                        "d'en sortir quand il doit consulter une autre année ou un élève pas encore inscrit. Les états vides " +
                        "sont rédigés pour dire POURQUOI une liste est vide et quoi faire, plutôt que d'afficher un simple " +
                        "« Aucun résultat » sans issue.",
                    probleme:
                        "Sans repère d'exercice, deux rentrées se confondent et l'on additionne deux promotions sans s'en " +
                        "apercevoir. Et une liste qui affiche « Aucun résultat » sans explication laisse croire à une panne " +
                        "alors qu'il ne manque qu'un filtre à ajuster ou une donnée à créer en amont.",
                    procedure: [
                        "Vérifiez l'année active dans l'en-tête avant toute saisie : c'est elle qui recevra ce que vous enregistrez.",
                        "Liste Élèves : « Inscrits cette année » ne montre que l'effectif inscrit pour l'année active ; « Non inscrits » isole les élèves de l'annuaire sans inscription vivante ; « Tous les élèves » rebascule sur l'annuaire complet.",
                        "Si une liste semble vide, lisez le message d'état : il indique le filtre à changer (« Essayez Non inscrits ou Tous les élèves »), la donnée à créer, ou le rôle requis.",
                        "Menu déroulant vide ou trop court : c'est que la donnée n'existe pas encore — créez d'abord vos classes, vos matières ou vos enseignants, puis rouvrez le sélecteur.",
                        "Changer un sélecteur qui pilote un calcul (la classe sur l'écran Inscriptions, par exemple) réinitialise ce qui en dépend, pour ne pas mélanger deux barèmes.",
                        "Un menu qui s'ouvre vers le haut reste accolé à son champ : la valeur survolée et le champ restent alignés, sans saut visuel."
                    ],
                    impacts: [
                        "Tableaux de bord et rapports : tous les indicateurs se rapportent à l'année active ; changer d'année change la lecture.",
                        "Effectifs, frais, présences : un élève non inscrit pour l'année active en est absent tant qu'il n'est pas inscrit (voir « Inscription différée »).",
                        "Sélecteurs : un enseignant ne voit dans les menus que ses propres affectations ; une liste courte peut donc être normale selon le rôle.",
                        "Pointage Profs : le menu ne liste que les CONTRATS de type Vacataire (Comptabilité › Paie), pas le statut « Vacataire » de la fiche enseignant — deux notions distinctes portant la même étiquette.",
                        "États vides : ils sont dérivés des données déjà chargées et se mettent à jour sans rechargement dès que la cause disparaît."
                    ],
                    recommandations: [
                        "Devant une liste vide, lisez le message avant de signaler un incident : neuf fois sur dix, il nomme lui-même la cause et la marche à suivre.",
                        "Ne travaillez jamais sans avoir vérifié l'année active affichée dans l'en-tête, surtout juste après une bascule d'exercice.",
                        "Réservez « Tous les élèves » à la gestion d'un cas précis (réinscription, correction) : la vue par défaut « Inscrits cette année » est celle qui reflète l'effectif réel.",
                        "Si une classe ou une matière manque dans un menu, ne la contournez pas : créez-la dans son module (Classes, Matières), c'est la seule source des sélecteurs."
                    ]
                },
                {
                    id: 'guidage-et-raccourcis',
                    title: 'Modales de guidage et raccourcis de navigation',
                    location: 'Commun à tous les écrans',
                    href: '/aide',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "Deux aides transverses, présentes sur l'ensemble des écrans. La MODALE DE GUIDAGE remplace le " +
                        "bandeau rouge « erreur système » quand une action est tentée sans son pré-requis — aucune session " +
                        "de caisse ouverte, aucun enseignant sélectionné, aucune année active : une fenêtre centrée " +
                        "explique alors l'étape manquante, et l'appel voué à l'échec n'est même pas envoyé. Les RACCOURCIS " +
                        "DE NAVIGATION sont les libellés cliquables — un nom de classe, une matière, un enseignant — qui, " +
                        "d'un écran à l'autre, ouvrent l'écran correspondant déjà filtré sur l'élément visé.",
                    objectif:
                        "Faire gagner du temps sur les deux frictions les plus fréquentes : comprendre pourquoi un bouton " +
                        "« ne marche pas », et retrouver une entité mentionnée ailleurs. Pour la direction, c'est moins " +
                        "d'appels au support pour des blocages qui portent en réalité leur propre explication.",
                    probleme:
                        "Un message d'erreur technique sur une condition simplement non remplie — « 422 Unprocessable " +
                        "Entity » là où il fallait d'abord ouvrir la caisse — inquiète l'utilisateur et déclenche un appel " +
                        "au support pour une situation qu'une phrase aurait résolue. Et retrouver « la classe de CM2 B » " +
                        "citée sur un créneau obligeait à repartir de la liste des classes et à la chercher à la main.",
                    procedure: [
                        "Quand une fenêtre de guidage s'ouvre, lisez l'étape qu'elle décrit, faites-la sur l'écran indiqué, puis reprenez l'action : « Compris » referme simplement la fenêtre, elle n'enregistre rien.",
                        "Un libellé souligné ou présenté comme un lien — nom de classe, de matière, d'élève, d'enseignant — est cliquable : il ouvre l'écran de cette entité, déjà filtré sur elle.",
                        "Ces raccourcis ouvrent l'écran dans le même onglet ; utilisez le clic du milieu ou « ouvrir dans un nouvel onglet » du navigateur pour garder l'écran de départ sous les yeux.",
                        "Le Centre d'aide, lui, reste la documentation de fond : la modale de guidage ne le remplace pas, elle traite l'instant présent."
                    ],
                    impacts: [
                        "Tous les écrans : la modale de guidage est un composant unique, monté une seule fois — son apparence et son bouton « Compris » sont identiques partout.",
                        "Caisse, Emploi du temps, Notes : ce sont les écrans où un pré-requis manquant est le plus fréquent, donc ceux où la modale se déclenche le plus.",
                        "Filtres d'écran : un raccourci de navigation applique un filtre à l'arrivée ; l'écran cible s'ouvre donc sur une liste déjà restreinte, qu'un clic sur « effacer » ré-élargit."
                    ],
                    recommandations: [
                        "Ne fermez pas une modale de guidage sans avoir lu l'étape : elle nomme précisément ce qui manque, et refaire l'action sans rien changer rouvrira la même fenêtre.",
                        "Si un écran s'ouvre étrangement vide après un clic sur un raccourci, c'est le filtre d'arrivée : cherchez le bouton qui l'efface plutôt que de conclure à une perte de données.",
                        "Une vraie erreur système garde le bandeau rouge : si vous en voyez un, ce n'est pas un simple pré-requis manquant — là, un signalement au support est justifié."
                    ]
                },
                {
                    id: 'securite-du-compte',
                    title: 'Sécurité de votre compte — mot de passe, verrouillage, session',
                    location: 'Menu profil et écran de connexion',
                    href: '/parametres',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "Quatre protections s'appliquent au compte de chaque utilisateur, indépendamment de son rôle. Le " +
                        "CHANGEMENT DE MOT DE PASSE en libre-service se fait depuis le menu profil, sans passer par un " +
                        "administrateur — pour tous les rôles. Le CHANGEMENT D'E-MAIL, depuis la carte de profil de la " +
                        "barre latérale, est réservé au DIRECTEUR ET AU SUPER ADMIN : un compte créé par le Directeur " +
                        "(Secrétariat, Finance, Enseignant, Surveillant) n'a que le mot de passe en libre-service, jamais " +
                        "son propre e-mail. Quand il est proposé, le changement d'e-mail exige de reconfirmer le mot de " +
                        "passe actuel et révoque TOUTES les sessions ouvertes, y compris celle en cours. Le VERROUILLAGE " +
                        "PROGRESSIF bloque temporairement les tentatives de connexion après plusieurs échecs de mot de " +
                        "passe rapprochés, le délai s'allongeant à chaque " +
                        "nouvel échec. La DÉCONNEXION AUTOMATIQUE ferme la session après une période d'inactivité, dont la " +
                        "durée est réglée par le Directeur dans les Paramètres système.",
                    objectif:
                        "Protéger les données de l'établissement contre un poste laissé ouvert sans surveillance et contre " +
                        "les tentatives répétées de deviner un mot de passe, tout en laissant chaque utilisateur maître de " +
                        "son propre mot de passe sans dépendre du secrétariat.",
                    probleme:
                        "Un mot de passe qu'on ne peut changer soi-même finit noté sur un papier collé à l'écran. Une " +
                        "connexion sans limite de tentatives s'attaque à la machine. Et un poste de secrétariat resté " +
                        "connecté pendant la pause de midi expose élèves, notes et caisse à qui passe devant.",
                    procedure: [
                        "CHANGER SON MOT DE PASSE : ouvrez le menu profil, choisissez « Changer mon mot de passe », saisissez l'actuel puis le nouveau — la robustesse du nouveau est vérifiée à la saisie.",
                        "CHANGER SON E-MAIL (Directeur et Super Admin uniquement) : depuis la carte de profil de la barre latérale, choisissez « Changer mon e-mail », saisissez le nouvel e-mail et votre mot de passe ACTUEL pour confirmer. Vous serez déconnecté de toutes vos sessions, y compris celle-ci : reconnectez-vous avec la nouvelle adresse.",
                        "SI VOUS ÊTES Secrétariat, Finance, Enseignant ou Surveillant : cette option n'apparaît pas sur votre carte de profil. Un e-mail erroné se corrige en demandant au Directeur d'utiliser « Corriger le profil d'un compte » (Paramètres › Utilisateurs & rôles).",
                        "APRÈS PLUSIEURS ÉCHECS DE CONNEXION : patientez le temps indiqué par l'écran de connexion ; réessayer plus tôt ne fait que rallonger le délai. En cas de doute réel sur le mot de passe, demandez une réinitialisation.",
                        "DÉCONNEXION AUTOMATIQUE : si l'application vous a déconnecté après une absence, reconnectez-vous simplement — aucune donnée validée avant l'inactivité n'est perdue, seule une saisie en cours non enregistrée l'est.",
                        "RÉGLER LE DÉLAI (Directeur) : Paramètres › Paramètres système, champ « délai de déconnexion automatique »."
                    ],
                    impacts: [
                        "Paramètres système : le délai d'inactivité y est réglé par le Directeur et s'applique à tous les comptes de l'établissement.",
                        "Saisies en cours : la déconnexion automatique, comme une coupure réseau, laisse une saisie non validée à l'écran ou la perd selon l'écran — elle ne valide jamais rien à votre place (voir « Résilience réseau »).",
                        "Comptes du personnel : la réinitialisation d'un mot de passe oublié par un membre du personnel reste, elle, du ressort du Directeur depuis Paramètres › Utilisateurs & rôles.",
                        "Unicité : un e-mail déjà utilisé par un autre compte de la plateforme est refusé — deux comptes ne peuvent jamais partager la même adresse de connexion."
                    ],
                    recommandations: [
                        "Changez votre mot de passe à la première connexion si un administrateur vous en a communiqué un : tant que vous utilisez le sien, il connaît votre accès.",
                        "Ne réduisez pas le délai de déconnexion automatique au point de gêner le travail réel : un agent sans cesse déconnecté finit par contourner la sécurité autrement.",
                        "Un verrouillage qui se déclenche alors que vous êtes sûr du mot de passe peut signaler une tentative d'intrusion sur votre compte : signalez-le, et changez le mot de passe une fois l'accès rétabli.",
                        "Verrouillez ou fermez votre session en quittant votre poste plutôt que de compter sur la déconnexion automatique : le délai, aussi court soit-il, laisse une fenêtre.",
                        "Après un changement d'e-mail, notez-le immédiatement quelque part de sûr avant de vous déconnecter : la session en cours se ferme dans l'instant, sans confirmation supplémentaire."
                    ]
                },
                {
                    id: 'navigation-rapide-quicknav',
                    title: 'Barre de navigation rapide entre modules',
                    location: 'Sous la barre supérieure, sur tout écran',
                    href: '/tableau-de-bord',
                    roles: ['Directeur', 'Secrétariat', 'Finance', 'Enseignant', 'Surveillant'],
                    definition:
                        "Une rangée de raccourcis vers les modules les plus fréquentés — Élèves, Classes, Enseignants, " +
                        "Notes, Caisse, Internat — affichée sous la barre supérieure de CHAQUE écran, pas seulement " +
                        "depuis la barre latérale. Sous une largeur d'écran réduite, elle se replie en un bouton unique " +
                        "« Saut rapide ».",
                    objectif:
                        "Épargner l'aller-retour par la barre latérale pour passer d'un module courant à un autre, en " +
                        "particulier sur les écrans où la barre latérale est repliée ou peu visible.",
                    probleme:
                        "Naviguer entre deux modules consultés en alternance — Élèves puis Caisse, par exemple — " +
                        "obligeait à revenir chaque fois vers la barre latérale, un aller-retour répété plusieurs fois " +
                        "par heure pour un poste de secrétariat ou de caisse.",
                    procedure: [
                        "Sur un écran large, repérez la rangée d'onglets sous la barre supérieure : cliquez directement sur le module voulu.",
                        "Sur un écran étroit (mobile, fenêtre réduite), ouvrez le bouton « Saut rapide » et choisissez le module dans la liste déroulante.",
                        "L'onglet du module actuellement ouvert est mis en surbrillance, pour se repérer sans lire chaque libellé.",
                        "La barre ne s'affiche que si au moins DEUX modules vous sont accessibles : un rôle qui n'en voit qu'un (le Surveillant, par exemple, limité à Internat) ne voit pas cette barre, redondante avec la barre latérale dans ce cas."
                    ],
                    impacts: [
                        "Rôles et formules : chaque raccourci suit exactement les mêmes règles de visibilité que son équivalent dans la barre latérale — rien n'y apparaît qu'un rôle donné ne pourrait de toute façon pas ouvrir.",
                        "Modules désactivés : un module masqué par Paramètres › Modules & fonctionnalités (Pédagogie, Finance, Internat) disparaît de cette barre exactement comme de la barre latérale.",
                        "Barre latérale : les deux navigations restent INDÉPENDANTES et coexistent ; utiliser l'une n'affecte jamais l'état repliée/dépliée de l'autre."
                    ],
                    recommandations: [
                        "Sur un poste dédié à une seule tâche (un poste de caisse, par exemple), cette barre n'apporte rien de plus que la barre latérale — ignorez-la sans hésiter.",
                        "Sur un poste partagé entre plusieurs tâches dans la même journée, c'est le raccourci le plus rapide d'un module à l'autre : préférez-le à la barre latérale une fois l'habitude prise."
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
