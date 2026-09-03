namespace SamaEcole.Web.Content;

/// <summary>
/// Contenu ÉDITORIAL de la vitrine publique (/vitrine et /modules). Aucune logique métier, aucune
/// requête, aucune dépendance : uniquement du texte constant que les vues Razor parcourent.
///
/// POURQUOI ICI, ET PAS DANS wwwroot/js COMME LE CENTRE D'AIDE ?
/// Le Centre d'aide (/aide, wwwroot/js/help.js) tient son contenu en JavaScript parce que sa
/// fonctionnalité première est une RECHERCHE plein texte côté client. La vitrine a le besoin
/// opposé : elle doit être indexable et lisible sans JavaScript. Un catalogue rendu côté serveur
/// met les 14 modules dans le HTML livré — Alpine ne fait plus que filtrer et déplier du DOM déjà
/// présent. Accessoirement, cela évite de charger sur une page d'accueil les 198 Ko de help.js,
/// écrits au registre « mode d'emploi » (« Ouvrez Paramètres, puis l'onglet… ») et non commercial.
///
/// RÈGLE DE RÉDACTION : rien ici ne doit affirmer ce que le code ne fait pas. Chaque module cite
/// l'écran réel qui l'implémente ; chaque mécanisme de sécurité est vérifiable dans le dépôt. Pas
/// de chiffre de clientèle, pas de prix (la grille FCFA du CDC §11.1 est marquée « indicative, non
/// validée commercialement »), pas de portail parents (hors périmètre V1, voir AGENTS.md).
/// La source des textes de module est HELP_SECTIONS dans wwwroot/js/help.js — si un module y est
/// ajouté ou renommé, le refléter ici.
/// </summary>
public static class MarketingCatalog
{
    // ═══════════════════════════════════════════════════════════════════════ Types

    /// <param name="Number">Numéro du module, aligné sur HELP_SECTIONS (help.js).</param>
    /// <param name="Slug">Ancre stable de l'URL (/modules#module-inscriptions) — ne pas renommer.</param>
    /// <param name="Category">Clé d'une <see cref="Category"/> ci-dessous, pour le filtre.</param>
    /// <param name="Icon">Nom d'un symbole du sprite (Views/Shared/_IconSprite.cshtml).</param>
    /// <param name="Screen">Écran réel qui implémente le module — sert de preuve, pas d'ornement.</param>
    public sealed record Module(
        int Number,
        string Slug,
        string Title,
        string Category,
        string Icon,
        string Tagline,
        string Description,
        IReadOnlyList<string> Features,
        IReadOnlyList<string> Roles,
        string Screen);

    public sealed record Category(string Key, string Label);

    public sealed record Family(string Icon, string Title, string Description, string Modules);

    public sealed record Problem(string Icon, string Title, string Description);

    public sealed record Profile(
        string Key,
        string Name,
        string Icon,
        string Promise,
        IReadOnlyList<string> Capabilities,
        string Note);

    public sealed record Step(string Number, string Title, string Description);

    public sealed record Guarantee(string Icon, string Title, string Description);

    public sealed record Plan(
        string Name,
        string Audience,
        IReadOnlyList<string> Includes,
        bool Recommended);

    public sealed record Question(string Ask, string Answer);

    public sealed record Figure(string Value, string Label);

    // ═══════════════════════════════════════════════════════════════════════ Catégories

    /// <summary>
    /// Filtres de l'explorateur de modules. « Plateforme » n'était pas dans la liste initiale mais
    /// le module 10 (Résilience réseau) n'est pas un domaine métier : le ranger de force dans
    /// « Administration » aurait été un classement faux.
    /// </summary>
    public static readonly IReadOnlyList<Category> Categories =
    [
        new("tous", "Tous"),
        new("scolarite", "Scolarité"),
        new("pedagogie", "Pédagogie"),
        new("finance", "Finance"),
        new("administration", "Administration"),
        new("vie-scolaire", "Vie scolaire"),
        new("examens", "Examens"),
        new("rh", "Ressources humaines"),
        new("patrimoine", "Patrimoine"),
        new("integrations", "Intégrations"),
        new("plateforme", "Plateforme")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Les 14 modules

    public static readonly IReadOnlyList<Module> Modules =
    [
        new(
            Number: 1,
            Slug: "configuration",
            Title: "Configuration initiale & Années scolaires",
            Category: "administration",
            Icon: "calendar",
            Tagline: "L'exercice de travail, et les murs qui l'abritent.",
            Description:
                "Une seule année scolaire est active à la fois, et cette unicité est garantie par la base de données " +
                "elle-même. Inscription, barème, note, bulletin : chaque donnée est rattachée sans ambiguïté à son " +
                "exercice. Les bâtiments et les salles complètent ce socle, avec leur capacité réelle d'accueil.",
            Features:
            [
                "Ouverture et clôture d'une année académique",
                "Découpage en trimestres, base de toutes les saisies de notes",
                "Bascule d'année confirmée par le mot de passe du Directeur",
                "Exercice précédent archivé en lecture seule, jamais effacé",
                "Bâtiments, salles, types et capacités d'accueil"
            ],
            Roles: ["Directeur", "Secrétariat"],
            Screen: "Paramètres › Années scolaires · Bâtiments & Salles"),

        new(
            Number: 2,
            Slug: "structure-pedagogique",
            Title: "Structure pédagogique & Matières modulables",
            Category: "pedagogie",
            Icon: "book",
            Tagline: "Une grille d'évaluation par niveau, pas une seule pour tous.",
            Description:
                "Les classes, rattachées à des niveaux eux-mêmes réunis en cycles. Le moteur d'évaluation est conçu " +
                "pour l'hétérogénéité du système sénégalais : il accepte aussi bien la notation classique par matière " +
                "et coefficient du secondaire que le barème hiérarchique — domaines et activités — que l'Approche par " +
                "les Compétences impose au primaire. C'est ce paramétrage qui détermine la forme exacte des bulletins.",
            Features:
            [
                "Classes, niveaux et cycles",
                "Moteur d'évaluation APC — domaines et activités",
                "Notation classique par matière et coefficient",
                "Matières hiérarchiques à barèmes hétérogènes",
                "Grille d'évaluation propre à chaque niveau"
            ],
            Roles: ["Directeur", "Secrétariat", "Enseignant"],
            Screen: "Gestion Scolaire › Classes · Matières"),

        new(
            Number: 3,
            Slug: "personnel",
            Title: "Gestion du personnel & Enseignants",
            Category: "rh",
            Icon: "users",
            Tagline: "La fiche décrit une personne, le compte ouvre un accès.",
            Description:
                "Deux objets distincts qu'il ne faut jamais confondre : la fiche — identité, matricule interne, " +
                "conditions contractuelles — et le compte utilisateur, qui ouvre un accès à l'application. À la fiche " +
                "se rattachent les affectations, ces couples matière × classe pour lesquels l'enseignant est habilité. " +
                "L'affectation est le pivot : elle borne ce que chacun voit et saisit.",
            Features:
            [
                "Fiches enseignants et matricule interne",
                "Comptes utilisateurs distincts des fiches",
                "Affectations matière × classe",
                "Rattachement contractuel — Permanent ou Vacataire",
                "Accès bornés par affectation, sans réglage manuel"
            ],
            Roles: ["Directeur", "Secrétariat"],
            Screen: "Gestion Scolaire › Enseignants"),

        new(
            Number: 4,
            Slug: "inscriptions",
            Title: "Mouvements d'élèves — Inscriptions & Réinscriptions",
            Category: "scolarite",
            Icon: "document",
            Tagline: "Le point où la scolarité devient une créance.",
            Description:
                "L'inscription est l'acte unique qui relie un élève, une année scolaire, une classe et un barème de " +
                "frais. La première inscription crée l'identité scolaire ; la réinscription prolonge un dossier " +
                "existant sans jamais le dupliquer. La validation fige le montant dû ligne à ligne et ouvre le dossier " +
                "financier de la famille.",
            Features:
            [
                "Première inscription et saisie de l'état civil",
                "Matricule généré dans la transaction, jamais à l'ouverture du formulaire",
                "Réinscription et changement de classe",
                "Panneau « Frais » — aide au calcul, sans encaissement",
                "Affectation automatique des échéanciers financiers",
                "Import des élèves depuis un modèle Excel"
            ],
            Roles: ["Directeur", "Secrétariat", "Finance"],
            Screen: "Gestion Scolaire › Élèves · Inscriptions"),

        new(
            Number: 5,
            Slug: "notes-bulletins",
            Title: "Évaluations, saisie des notes & bulletins PDF",
            Category: "pedagogie",
            Icon: "chart-multiple",
            Tagline: "On saisit des notes ; le reste se calcule.",
            Description:
                "La chaîne est volontairement à sens unique : on saisit des notes, le serveur en dérive les moyennes, " +
                "les rangs et les mentions, et le bulletin ne fait que restituer ce calcul. Aucune moyenne ne se " +
                "saisit à la main. Chaque note est ramenée à une échelle commune avant d'être pondérée par son " +
                "coefficient — un 45/60 et un 18/24 pèsent alors exactement le même poids.",
            Features:
            [
                "Saisie sécurisée des notes par classe et par matière",
                "Devoirs et compositions, trimestre par trimestre",
                "Moyennes, rangs, mentions et appréciations calculés",
                "Barèmes hétérogènes ramenés à une échelle commune",
                "Bulletins officiels générés au format PDF",
                "Import et export Excel des notes"
            ],
            Roles: ["Directeur", "Secrétariat", "Enseignant"],
            Screen: "Gestion Scolaire › Notes et bulletins"),

        new(
            Number: 6,
            Slug: "comptabilite",
            Title: "Comptabilité, frais scolaires & caisse",
            Category: "finance",
            Icon: "wallet",
            Tagline: "Le barème, l'échéancier et l'encaissement ne se confondent jamais.",
            Description:
                "Le barème est ce que l'établissement décide de facturer. L'échéancier est ce qu'une famille doit, et " +
                "à quelles dates. L'encaissement est ce qu'elle a versé. D'où le principe qui gouverne le module : le " +
                "service financier encaisse, il ne fixe ni ne corrige jamais un montant dû — toute révision relève du " +
                "secrétariat ou de la direction, et demeure historisée. C'est cette séparation qui rend une caisse comptable.",
            Features:
            [
                "Barème des frais d'inscription et des mensualités",
                "Encaissement au guichet et reçu de paiement A5",
                "Suivi des recouvrements, des impayés et des relances",
                "Clôture de caisse et journée du secrétariat",
                "Séparation stricte entre encaisser et réviser un montant dû"
            ],
            Roles: ["Directeur", "Finance"],
            Screen: "Comptabilité › Frais Scolaires · Caisse"),

        new(
            Number: 7,
            Slug: "rapports",
            Title: "Rapports, statistiques & audit",
            Category: "administration",
            Icon: "shield",
            Tagline: "Ce module ne produit rien : il restitue.",
            Description:
                "Trois familles d'instruments. Les tableaux de bord donnent la situation à l'instant présent. Les " +
                "rapports exportables sont destinés au comptable et au conseil d'administration. Le journal d'audit " +
                "retrace l'auteur, la date et la valeur antérieure de toute opération sensible. Un indicateur ne vaut " +
                "jamais mieux que les saisies qui l'alimentent — un chiffre surprenant trahit plus souvent une erreur " +
                "d'écriture qu'un événement réel.",
            Features:
            [
                "Tableaux de bord de la direction",
                "Rapport d'assiduité détaillé par classe et par élève",
                "Rapports financiers consolidés et export comptable .xlsx",
                "Journal d'audit — auteur, date, valeur antérieure",
                "Clôture de caisse et journée du secrétariat"
            ],
            Roles: ["Directeur", "Finance", "Secrétariat"],
            Screen: "Tableau de bord · Rapports financiers · Rapport d'assiduité"),

        new(
            Number: 8,
            Slug: "examens",
            Title: "Examens officiels — CFEE, BFEM, BAC",
            Category: "examens",
            Icon: "document-text",
            Tagline: "Chaque étape verrouille la précédente.",
            Description:
                "Un dossier de candidature s'ouvre incomplet, ne devient complet que lorsque les pièces nécessaires " +
                "sont présentes et l'état civil conforme, ne peut être transmis à l'IEF ou à l'IA qu'une fois complet, " +
                "et ne reçoit un résultat qu'après. On ne transmet jamais un dossier auquel il manque une pièce. Le " +
                "numéro de table, comme le matricule, n'est attribué qu'au moment où le centre est arrêté — jamais " +
                "avant, pour qu'aucune suite de numéros ne comporte de trous.",
            Features:
            [
                "Ouverture des sessions d'examen",
                "Constitution et contrôle des dossiers de candidature",
                "Cycle de vie verrouillé : Incomplet → Complet → Transmis → Résultats",
                "Attribution du centre et du numéro de table",
                "Convocations et saisie des résultats de délibération"
            ],
            Roles: ["Directeur", "Secrétariat", "Enseignant"],
            Screen: "Gestion Scolaire › Examens officiels"),

        new(
            Number: 9,
            Slug: "paie",
            Title: "Ressources Humaines & Paie",
            Category: "rh",
            Icon: "payment",
            Tagline: "Deux régimes de rémunération, une seule déclaration.",
            Description:
                "Le permanent perçoit un salaire de base fixe, mensuel, indépendant des heures effectuées. Le " +
                "vacataire est rémunéré au taux horaire, ce qui suppose un pointage préalable des heures réellement " +
                "assurées : sans heures pointées, aucune fiche de paie cohérente ne peut être produite. La fiche de " +
                "paie alimente ensuite la déclaration fiscale mensuelle.",
            Features:
            [
                "Contrats du personnel — Permanent et Vacataire",
                "Pointage des heures des vacataires",
                "Génération des fiches de paie",
                "Déclarations fiscales mensuelles — IPRES, CSS, VRS, BRS",
                "Consolidation des charges sociales sur l'ensemble du personnel"
            ],
            Roles: ["Directeur", "Finance"],
            Screen: "Comptabilité › Paie · Pointage Profs · Fiscalité"),

        new(
            Number: 10,
            Slug: "resilience-reseau",
            Title: "Résilience réseau — travailler sur une connexion instable",
            Category: "plateforme",
            Icon: "globe",
            Tagline: "Une coupure de trois secondes ne doit pas coûter une ressaisie.",
            Description:
                "Unikol est une application 100 % en ligne : aucune donnée n'est enregistrée localement à titre " +
                "définitif, il n'existe ni mode hors ligne ni file de synchronisation entre postes — ce choix élimine " +
                "toute une classe de conflits. Ce qui est offert à la place est une résilience courte : détecter la " +
                "coupure, conserver la saisie en cours, et sur deux écrans précis retenter l'enregistrement soi-même.",
            Features:
            [
                "Badge de connectivité — Connecté, Vérification, Hors ligne",
                "Brouillons de formulaire conservés sur le poste, cloisonnés par établissement",
                "Reprise automatique après coupure sur la Caisse et l'Appel en classe",
                "Aucun doublon : une réponse déjà reçue du serveur n'est jamais rejouée",
                "Message explicite quand rien n'a été enregistré"
            ],
            Roles: ["Directeur", "Secrétariat", "Finance", "Enseignant", "Surveillant"],
            Screen: "Barre supérieure — présente sur toutes les pages"),

        new(
            Number: 11,
            Slug: "integration-etatique",
            Title: "Intégration étatique — IEN, Planète, STATEDUC et mutations",
            Category: "integrations",
            Icon: "building",
            Tagline: "Les pièces réglementaires, aux formats attendus.",
            Description:
                "Ce module produit les fichiers et pièces dus à l'administration, dans les formats qu'elle attend. Il " +
                "ne dialogue avec aucun système du ministère : aucune interface informatique publique du SIMEN n'est " +
                "ouverte à ce jour. Unikol prépare donc ce que vous transmettez par la voie habituelle — dépôt, " +
                "courriel, remise à l'IEF. Cette limite est affichée plutôt que masquée derrière un bouton qui échouerait.",
            Features:
            [
                "Identifiant National de l'Élève (IEN), provisoire ou définitif",
                "Export « Planète Ready » des élèves",
                "Rapport annuel STATEDUC",
                "Certificat de mutation avec QR de vérification publique",
                "Livret de compétences"
            ],
            Roles: ["Directeur", "Secrétariat"],
            Screen: "Intégration étatique · Fiche élève"),

        new(
            Number: 12,
            Slug: "inventaire",
            Title: "Inventaire — Patrimoine, stock et prêts de matériel",
            Category: "patrimoine",
            Icon: "archive",
            Tagline: "Une erreur se corrige par un mouvement inverse, jamais par une gomme.",
            Description:
                "La catégorie et le bien décrivent le patrimoine. Le mouvement de stock est l'écriture qui fait varier " +
                "une quantité. Le prêt est d'un genre différent : la quantité disponible baisse, mais le bien reste au " +
                "patrimoine, car il doit revenir. Deux invariants tiennent le module : la quantité disponible ne " +
                "s'écrit jamais directement, et le journal de stock n'accepte que des ajouts.",
            Features:
            [
                "Catégories et fiches de biens",
                "Journal de stock — entrées, sorties, ajustements d'inventaire",
                "Prêts et attributions de matériel",
                "Quantité disponible dérivée du journal, jamais saisie",
                "Correction par mouvement inverse, sans suppression de ligne"
            ],
            Roles: ["Directeur", "Secrétariat", "Surveillant"],
            Screen: "Inventaire › Catalogue · Mouvements · Prêts"),

        new(
            Number: 13,
            Slug: "vie-scolaire",
            Title: "Vie scolaire — Appel, billets, discipline et convocations",
            Category: "vie-scolaire",
            Icon: "eye",
            Tagline: "Quatre registres distincts, qui ne se recouvrent jamais.",
            Description:
                "L'appel constate qui est présent, absent ou en retard, classe par classe et créneau par créneau. Le " +
                "billet documente un mouvement individuel hors du rythme normal, et trace la personne venue chercher " +
                "l'élève. Le registre de discipline sanctionne un fait déjà constaté et produit un procès-verbal. La " +
                "convocation, enfin, n'est pas une sanction mais un entretien programmé avec un parent ou un tuteur.",
            Features:
            [
                "Appel en classe et feuille de présence",
                "Billets d'entrée tardive et de sortie anticipée, imprimés en A5",
                "Registre de discipline et procès-verbal",
                "Convocations de parent ou de tuteur, avec signalement des retards",
                "Rapport d'assiduité par classe et par élève"
            ],
            Roles: ["Directeur", "Secrétariat", "Enseignant", "Surveillant"],
            Screen: "Surveillance › Appel · Billets · Discipline · Convocations"),

        new(
            Number: 14,
            Slug: "tresorerie",
            Title: "Trésorerie — Décaissements et vision consolidée",
            Category: "finance",
            Icon: "arrow-down",
            Tagline: "La caisse enregistre ce qui entre ; la trésorerie, ce qui sort.",
            Description:
                "Salaires, maintenance, fournitures, loyer, eau et électricité, télécoms, carburant, assurances, " +
                "honoraires : la trésorerie enregistre ce que l'établissement dépense. Son tableau de bord ne crée " +
                "aucun registre nouveau — il agrège, sur une période choisie, les encaissements déjà saisis par la " +
                "caisse et les décaissements saisis ici, pour donner à la direction une vision consolidée.",
            Features:
            [
                "Enregistrement des décaissements",
                "Catégories de dépense — salaires, maintenance, fournitures, charges",
                "Tableau de bord Trésorerie",
                "Vision consolidée des entrées et des sorties sur une période",
                "Historique et rapports par période"
            ],
            Roles: ["Directeur", "Finance"],
            Screen: "Comptabilité › Trésorerie"),
    ];

    // ═══════════════════════════════════════════════════════════════════════ Chiffres vérifiables

    /// <summary>
    /// Bandeau de confiance. Chaque valeur est vérifiable dans le dépôt — pas un seul chiffre de
    /// clientèle, d'ancienneté ou de performance, qu'aucune source ne permettrait d'étayer.
    /// </summary>
    public static readonly IReadOnlyList<Figure> Figures =
    [
        new("14", "modules couvrant l'établissement"),
        new("5", "profils d'accès aux droits distincts"),
        new("8", "documents officiels générés en PDF"),
        new("100 %", "en ligne, sur ordinateur comme sur mobile")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Problèmes

    public static readonly IReadOnlyList<Problem> Problems =
    [
        new("document", "Trop de papier",
            "Dossiers, fiches d'inscription, registres d'appel, souches de reçus et certificats : " +
            "l'information existe, mais elle est dispersée dans des armoires."),
        new("layers", "Informations éparpillées",
            "Les notes dans un classeur, les présences dans un cahier, les paiements dans un tableur, " +
            "les élèves dans un troisième outil. Aucun des trois ne parle aux autres."),
        new("wallet", "Une situation financière floue",
            "Combien reste-t-il à recouvrer cette semaine ? Quelle classe est la plus en retard ? " +
            "Répondre suppose de tout ressaisir, donc de le faire trop tard."),
        new("clock", "Une administration chronophage",
            "Contrats, pointage des heures, dossiers d'examen, inventaire, pièces réglementaires : " +
            "chaque échéance consomme des journées qui devraient revenir à la pédagogie.")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Grandes familles

    public static readonly IReadOnlyList<Family> Families =
    [
        new("users", "Gestion scolaire",
            "Élèves, inscriptions, réinscriptions, classes et dossiers scolaires — de l'accueil au certificat.",
            "Modules 1 et 4"),
        new("book", "Pédagogie",
            "Matières, coefficients, notes, moyennes, rangs et bulletins officiels au format PDF.",
            "Modules 2 et 5"),
        new("wallet", "Finance & comptabilité",
            "Frais scolaires, encaissements, reçus, impayés, recouvrements, décaissements et trésorerie.",
            "Modules 6 et 14"),
        new("shield", "Administration",
            "Paramétrage, personnel, contrats, pointage, tableaux de bord, rapports et journal d'audit.",
            "Modules 1, 3, 7 et 9"),
        new("eye", "Vie scolaire",
            "Appel, présences, retards, billets d'entrée et de sortie, discipline et convocations.",
            "Module 13"),
        new("document-text", "Examens officiels",
            "CFEE, BFEM et BAC : sessions, dossiers de candidature, centres, convocations et résultats.",
            "Module 8"),
        new("archive", "Patrimoine & inventaire",
            "Biens, catégories, journal de stock, mouvements, prêts et attributions de matériel.",
            "Module 12"),
        new("building", "Intégrations institutionnelles",
            "IEN, export Planète, rapport annuel STATEDUC, certificats de mutation et livret de compétences.",
            "Module 11")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Profils

    /// <summary>
    /// Les CINQ rôles d'établissement de Domain/Enums/Role.cs. Le sixième, SuperAdmin, est
    /// l'exploitant de la plateforme et non un utilisateur de l'école : il n'a pas sa place ici.
    /// Aucun rôle Parent ni Élève — le portail correspondant est hors périmètre V1 (AGENTS.md).
    /// </summary>
    public static readonly IReadOnlyList<Profile> Profiles =
    [
        new("directeur", "Directeur", "shield",
            "Gardez une vision globale de votre établissement.",
            [
                "Tableau de bord consolidé : effectifs, présence, encaissements, reste à recouvrer",
                "Rapports financiers et export comptable .xlsx",
                "Journal d'audit de tout l'établissement — auteur, date, valeur antérieure",
                "Activation de l'année scolaire et paramétrage général",
                "Validation des barèmes de frais et de la grille d'évaluation"
            ],
            "Seul rôle habilité à consulter le journal d'audit et à basculer l'année scolaire."),

        new("secretariat", "Secrétariat", "document",
            "Tenez les dossiers à jour, de l'inscription au certificat.",
            [
                "Inscriptions, réinscriptions et changements de classe",
                "Fiches élèves, état civil et identifiant national (IEN)",
                "Classes, matières, enseignants et affectations",
                "Dossiers de candidature aux examens officiels",
                "Certificats de scolarité, cartes scolaires et listes d'émargement"
            ],
            "Peut réviser un montant dû ; c'est précisément ce que la Finance ne peut pas faire."),

        new("finance", "Finance", "wallet",
            "Encaissez, recouvrez, pilotez la trésorerie.",
            [
                "Encaissement au guichet et édition immédiate du reçu A5",
                "Suivi des impayés et des relances, par classe comme par élève",
                "Clôture de caisse et journée du secrétariat",
                "Décaissements et tableau de bord de trésorerie",
                "Fiches de paie et déclarations fiscales mensuelles"
            ],
            "Encaisse sans jamais fixer ni corriger un montant dû — la révision reste au secrétariat ou à la direction."),

        new("enseignant", "Enseignant", "book",
            "Vos classes, vos notes, votre appel.",
            [
                "Saisie des notes de devoir et de composition, matière par matière",
                "Appel en classe et feuille de présence",
                "Consultation des dossiers d'examen de ses classes",
                "Moyennes et rangs calculés, sans ressaisie ni tableur"
            ],
            "Chaque accès est borné aux couples matière × classe pour lesquels il est affecté."),

        new("surveillant", "Surveillant", "eye",
            "Suivez présences, retards, discipline et sorties.",
            [
                "Billets d'entrée tardive et de sortie anticipée, imprimés en A5",
                "Registre de discipline et procès-verbaux",
                "Convocations de parent ou de tuteur, avec alerte sur les retards",
                "Mouvements de stock et prêts de matériel"
            ],
            "Le billet trace la raison de la sortie et la personne venue chercher l'élève.")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Mise en route

    public static readonly IReadOnlyList<Step> Steps =
    [
        new("01", "Configurez votre établissement",
            "Année scolaire et trimestres, cycles, niveaux, classes, matières, bâtiments et barème de " +
            "frais. Ce socle se paramètre une seule fois ; tout ce qui suit s'y rattache."),
        new("02", "Importez vos données",
            "Les élèves s'importent depuis un modèle Excel téléchargeable ; les notes disposent du même " +
            "aller-retour. Enseignants et affectations se saisissent ensuite en quelques minutes."),
        new("03", "Travaillez au quotidien",
            "Inscriptions, encaissements, appel en classe, saisie des notes, discipline, inventaire : " +
            "chaque profil a ses écrans et ses droits, sans réglage à faire."),
        new("04", "Pilotez votre établissement",
            "Tableaux de bord de la direction, rapports financiers exportables, rapport d'assiduité, " +
            "journal d'audit et pièces réglementaires — établis sur les saisies du quotidien.")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Sécurité

    /// <summary>
    /// Chaque garantie correspond à un mécanisme réellement implémenté et vérifiable :
    /// RLS PostgreSQL + filtres applicatifs, JWT porteur du schoolId, journal d'audit en ajout seul
    /// (jusqu'au GRANT), soft delete, verrouillage optimiste xmin → 409, limitation de débit,
    /// webhooks signés HMAC. Ne rien ajouter ici qui ne soit pas dans le code.
    /// </summary>
    public static readonly IReadOnlyList<Guarantee> Guarantees =
    [
        new("lock-closed", "Isolation stricte par établissement",
            "Les données de votre école sont cloisonnées par une double barrière : une politique posée " +
            "dans la base de données elle-même, doublée d'un filtre applicatif. L'application refuse de " +
            "démarrer si cette configuration n'est pas celle attendue."),
        new("key", "Accès déterminé par le rôle",
            "Cinq profils, des permissions distinctes. Le rattachement d'un compte à son établissement " +
            "est porté par son jeton d'authentification — jamais par un paramètre d'adresse qu'un " +
            "navigateur pourrait modifier."),
        new("search", "Journal d'audit en ajout seul",
            "Auteur, date et valeur antérieure de chaque opération sensible. Le journal n'accepte que " +
            "des ajouts, y compris au niveau des droits de la base : personne, pas même le Directeur, " +
            "ne peut en réécrire une ligne. Sa consultation lui est réservée."),
        new("archive", "Aucune suppression définitive",
            "Une donnée de gestion est archivée, jamais effacée. Un élève radié, une salle fermée, un " +
            "barème remplacé : l'historique demeure consultable."),
        new("alert-circle", "Pas d'écrasement silencieux",
            "Deux agents modifient la même note ou le même paiement au même instant ? Le second reçoit " +
            "un refus explicite et voit la valeur à jour, plutôt qu'un écrasement passé inaperçu."),
        new("block", "Protection des points d'entrée",
            "Jeton d'authentification à durée limitée, réinitialisation de mot de passe par lien à " +
            "usage unique, et limitation du nombre de tentatives sur les points d'entrée sensibles."),
        new("checkmark-circle", "Paiements d'abonnement confirmés à la source",
            "Un paiement n'est confirmé que par une notification signée de l'agrégateur. Aucune adresse " +
            "accessible depuis un navigateur ne peut marquer un abonnement comme réglé.")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Formules

    /// <summary>
    /// Les trois formules de Domain/Enums/CommonEnums.cs (SubscriptionPlan) et la matrice
    /// PlanFeatures. AUCUN prix : la grille FCFA du CDC §11.1 est marquée « indicative, non encore
    /// validée commercialement ». Afficher un montant reviendrait à engager le commerce depuis une vue.
    /// </summary>
    public static readonly IReadOnlyList<Plan> Plans =
    [
        new("Primaire", "Établissement à effectif réduit — jusqu'à 500 élèves.",
            [
                "Élèves, classes, inscriptions et réinscriptions",
                "Notes, bulletins PDF et présences",
                "Encaissement, reçus et suivi des impayés",
                "Examens officiels, inventaire et vie scolaire",
                "Documents officiels et pièces réglementaires"
            ],
            Recommended: false),

        new("Standard", "Établissement de taille moyenne — jusqu'à 2 000 élèves.",
            [
                "Tout ce qui est inclus dans Primaire",
                "Rapports financiers consolidés par cycle, classe et mode de paiement",
                "Export comptable au format .xlsx"
            ],
            Recommended: true),

        new("Premium", "Effectif illimité, ou groupe de plusieurs établissements.",
            [
                "Tout ce qui est inclus dans Standard",
                "Notifications SMS aux parents — retards, absences, impayés, reçus",
                "Groupe scolaire : plusieurs établissements pilotés depuis un même compte"
            ],
            Recommended: false)
    ];

    // ═══════════════════════════════════════════════════════════════════════ FAQ

    public static readonly IReadOnlyList<Question> Questions =
    [
        new("Unikol convient-il à une école primaire ?",
            "Oui. Le moteur d'évaluation accepte aussi bien l'Approche par les Compétences du primaire " +
            "sénégalais — domaines, activités, barèmes hétérogènes — que la notation par matière et " +
            "coefficient du secondaire. La formule Primaire est prévue pour les établissements jusqu'à " +
            "500 élèves."),

        new("Peut-on gérer plusieurs années scolaires ?",
            "Oui, mais une seule est active à la fois, et cette unicité est garantie par la base de " +
            "données. Les exercices précédents restent intégralement consultables, en lecture seule : " +
            "une donnée erronée s'y régularise sur l'exercice courant, jamais par une réécriture du passé."),

        new("Peut-on importer les élèves depuis Excel ?",
            "Oui. Un modèle de fichier est téléchargeable depuis l'écran Élèves. Les notes disposent " +
            "également d'un import et d'un export Excel, matière par matière."),

        new("Qui peut accéder aux informations financières ?",
            "Le Directeur et le profil Finance. Et la Finance encaisse sans jamais fixer ni corriger un " +
            "montant dû : toute révision passe par le secrétariat ou la direction, et reste historisée. " +
            "C'est cette séparation des rôles qui rend une caisse comptable."),

        new("Les enseignants ont-ils leur propre espace ?",
            "Oui. Un enseignant saisit ses notes et fait l'appel, borné aux couples matière × classe " +
            "pour lesquels il est affecté. Il ne voit pas les classes des autres."),

        new("Les parents ont-ils un accès à la plateforme ?",
            "Non, pas dans la version actuelle. La communication vers les familles est sortante : SMS " +
            "(formule Premium), convocations et documents remis en main propre. Un portail destiné aux " +
            "parents et aux élèves est prévu pour une version ultérieure."),

        new("Peut-on gérer les examens CFEE, BFEM et BAC ?",
            "Oui : ouverture des sessions, constitution et contrôle des dossiers de candidature, " +
            "attribution du centre et du numéro de table, convocations, puis saisie des résultats de " +
            "délibération. Chaque étape verrouille la précédente."),

        new("Comment suit-on les impayés ?",
            "En temps réel, par classe comme par élève. L'écran Caisse et les rapports financiers " +
            "donnent le reste à recouvrer et le taux de recouvrement ; la formule Premium ajoute les " +
            "relances par SMS."),

        new("Unikol fonctionne-t-il sans connexion Internet ?",
            "Non, et c'est un choix assumé : aucune donnée n'est enregistrée localement à titre " +
            "définitif, ce qui élimine les conflits entre deux postes. En revanche, l'application " +
            "détecte les coupures, conserve votre saisie en cours et, sur la caisse et l'appel en " +
            "classe, retente elle-même l'enregistrement pendant une trentaine de secondes."),

        new("Unikol transmet-il directement les données au ministère ?",
            "Non. Aucune interface informatique publique du SIMEN n'est ouverte à ce jour. Unikol " +
            "prépare les fichiers et les pièces aux formats attendus — export Planète, rapport STATEDUC, " +
            "certificat de mutation — que vous transmettez par la voie habituelle."),

        new("Comment demander une démonstration ?",
            "Décrivez votre établissement dans le formulaire d'inscription : notre équipe revoit chaque " +
            "demande et revient vers vous pour convenir d'un rendez-vous. Vous pouvez aussi nous appeler " +
            "ou nous écrire sur WhatsApp.")
    ];

    // ═══════════════════════════════════════════════════════════════════════ Contact

    public const string PhoneDisplay = "+221 76 354 59 16";
    public const string PhoneHref = "tel:+221763545916";
    public const string WhatsAppHref = "https://wa.me/221763545916";

    // ═══════════════════════════════════════════════════════════════════════ Utilitaires de vue

    public static IEnumerable<Module> ByCategory(string key) =>
        key == "tous" ? Modules : Modules.Where(m => m.Category == key);

    public static string CategoryLabel(string key) =>
        Categories.FirstOrDefault(c => c.Key == key)?.Label ?? key;
}
