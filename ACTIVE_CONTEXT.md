# ACTIVE_CONTEXT — Périmètre V1 de Sama Ecole

État de référence du périmètre livré, tenu à jour à chaque clôture de sprint. Ce fichier ne porte
**aucune règle** : les règles non négociables vivent dans `AGENTS.md`, la spécification fonctionnelle
dans `docs/Volume_1_Cahier_des_Charges.md`. Il répond à une seule question — *qu'est-ce qui est dans
la V1, et qu'est-ce qui n'y est pas ?*

**Dernière mise à jour : 30/08/2026** (Module M « Intégration étatique / Passerelle SIMEN » —
**livré : back-end, persistance, migrations RLS, écrans et documentation**, voir §2 ; JGK-F09 —
contrôle du comptage physique et écarts de caisse à la clôture le 29/08/2026, voir §5 ; câblage de la
session de caisse sur `/caisse` le 27/08/2026 — JGK-F02 était inutilisable en production faute d'écran
d'ouverture/clôture ; écran `/examens` livré le 26/08/2026 — backend Module J déjà complet ; module
Inventaire — API, migration RLS, PDF, tests et écran `/inventaire` livrés).

---

## 1. Hors périmètre V1

### Portail Parents & Élèves + Messagerie (`docs/Volume_1_Cahier_des_Charges.md` §13) — **reporté en V3**

Sont concernés, et **uniquement** eux :

| §  | Sujet | Statut |
|---|---|---|
| 13.1 | Rattachement Parent ↔ Élève | Reporté V3 |
| 13.2 | Compte Élève — restriction de cycle (Collège/Lycée) | Reporté V3 |
| 13.3 | Consultation Parent (lecture seule) | Reporté V3 |
| 13.4 | Consultation Élève (lecture seule) | Reporté V3 |
| 13.5 | Messagerie et annonces | Reporté V3 |
| 13.6 | Isolation et sécurité des portails | Reporté V3 |

Conséquences concrètes pour toute personne — ou tout agent — qui travaille sur ce dépôt :

- Les rôles `Parent` et `Eleve` **ne doivent pas** être ajoutés à l'énumération `Role`, ni apparaître
  dans un `[Authorize]`, tant que la V3 n'est pas ouverte.
- Aucun endpoint public de consultation par un tiers non-personnel de l'établissement.
- La communication avec les parents en V1 passe **exclusivement** par les canaux sortants déjà
  livrés : notification SMS (`SmsDispatcher`, formule Premium), WhatsApp et e-mail transactionnel.
  Ces envois passent désormais par une **file d'attente** (statuts `Pending` → `Sent` → `Delivered`,
  accusés de réception signés) — voir §4 ci-dessous.
- Les convocations de parent/tuteur (`/convocations`) ne sont **pas** un portail : c'est un registre
  interne du module Vie Scolaire, imprimé et remis en main propre.

Ce report ne remet en cause ni le modèle de données ni la RLS : le jour où le portail sera ouvert,
il devra respecter à la lettre la règle #2 d'`AGENTS.md` (Global Query Filter **et** policy RLS).

---

## 2. Modules livrés et intégrés

Livrés, câblés à l'IHM, et couverts par la suite de tests :

| Module | Écran | Backend |
|---|---|---|
| **Paie** | `/paie` | Fiches de paie, contrats employés, heures enseignants |
| **Trésorerie** | `/tresorerie` | `GetTreasuryDashboardQuery` — encaissements + décaissements consolidés |
| **Caisse** | `/caisse` | Encaissements de guichet, reçus |
| **TVA / Fiscalité** | `/fiscalite` | VRS / IPRES, `GenerateTaxDeclarationCommand` |
| **Infrastructures** | `/infrastructures` | Bâtiments & salles |
| **Documents** | Module Documents | Génération et archivage documentaire |
| **Rapports financiers** | `/rapports/financiers` | `GetRevenueConsolidationQuery` + export `.xlsx` |
| **Inventaire** | `/inventaire` | API `/api/v1/inventory` — catalogue, journal de stock, prêts, 2 PDF |
| **Examens officiels** | `/examens` | API `/api/v1/exams` — sessions, dossiers CFEE/BFEM/BAC, audit, attribution centre/table, transmission, résultats, statistiques, export ministériel, convocations |
| **Intégration étatique** | `/integration-etatique` + fiche élève + `/verifier/mutation/{token}` | API `/api/v1/state-integration` — IEN, export Planète (CSV/JSON), rapport STATEDUC (PDF + Excel), certificat de mutation avec QR + registre + révocation, livret de compétences, vérification publique du certificat |

Le socle V1 (Élèves, Inscriptions, Classes, Matières, Enseignants, Notes & Bulletins, Frais,
Présences, Surveillance générale, Abonnements & Facturation, Console Super Admin) est livré depuis
les sprints précédents.

### Inventaire (26/08/2026) — API et écran livrés

Suivi du patrimoine, commun aux écoles publiques (tables-bancs, manuels d'État, consommables) et
privées (parc informatique, tenues, matériel de laboratoire). Quatre tables tenant
(`inventory_categories`, `inventory_items`, `stock_movements`, `item_assignments`), migration
`AddInventoryModule`, 17 endpoints sous `/api/v1/inventory`, deux documents QuestPDF.

**Ce qui est livré :** entités, migration + policies RLS, handlers CQRS, contrôleur, fiche
d'inventaire global (A4 paysage) et fiche de décharge (A5 paysage), tests unitaires et d'intégration,
`openapi.yaml`, Volume 3 §5.9, Volume 4 §21, Volume 7 §15, et l'écran `/inventaire` (Razor + Tailwind +
Alpine, `Views/Inventory/Index.cshtml` + `wwwroot/js/inventory.js`) : 4 onglets (Catalogue, Catégories,
Mouvements de stock, Prêts & Décharges), navigation ajoutée sous « Gestion Scolaire ». Compilation
Razor vérifiée (0 erreur) ; non cliqué dans un navigateur réel faute de base PostgreSQL disponible
dans cet environnement — à valider en local avant mise en production.

**Quatre arbitrages actés, à ne pas rouvrir sans raison :**

1. **Un bien est un LOT, pas une unité.** Suivi à l'unité = lot de quantité 1. Une table
   `inventory_item_units` reste la porte de sortie si un état par unité devient nécessaire ; elle
   n'est pas construite.
2. **`Condition` est l'état dominant du lot.** Le détail par état s'obtient en scindant en deux lots.
3. **Module gratuit** — aucun membre ajouté à `Feature`, accessible à toutes les formules.
4. **`Code` en saisie libre et facultative**, jamais généré : les écoles publiques réutilisent le
   numéro d'immatriculation posé sur le bien par la mairie ou l'État.

Export `.xlsx` de l'inventaire : **écarté pour ce lot** (PDF seul), à arbitrer si le besoin remonte.

### Intégration étatique / Passerelle SIMEN (30/08/2026) — livré (back-end + écrans)

Module M (`docs/Volume_1_Cahier_des_Charges.md` §23, `docs/Volume_3_DDS.md` §5.11,
`docs/Volume_4_API_Design.md` §23, tickets `JGK-M01`..`M07`). Le socle producteur des **pièces et
fichiers réglementaires** dus au ministère de l'Éducation. Migrations `AddStateIntegrationModule` +
`FixMutationCertificateReasonDefault` (colonnes sur `students`/`schools`/`teachers`/`exam_dossiers` +
table `student_mutation_certificates` + policy RLS + fonction
`SECURITY DEFINER verify_mutation_certificate`).

**Cadre non négociable, rappelé dans l'aide en ligne, le contrôleur ET l'écran :** le SIMEN n'expose
**aucune API publique** à ce jour. Le module produit des fichiers que l'école transmet par la voie
habituelle. `ISimenBridgeService` est un contrat seul ; l'implémentation livrée
(`UnavailableSimenBridgeService`) refuse explicitement chaque appel — aucune réussite simulée, jamais
d'état « Transmis » posé par une route client (règle #11). L'écran `/integration-etatique` affiche un
bandeau **« Statut du relais SIMEN »** qui montre l'indisponibilité plutôt qu'une action de
transmission.

**Ce qui est livré :** entités, migrations + RLS, handlers CQRS, `StateIntegrationController`
(11 routes), `PlaneteExportSerializer` (CSV/JSON, une seule définition de colonnes), documents QuestPDF
(`StateducReportDocument` A4 paysage, `StudentMutationCertificateDocument` avec QR,
`SkillsBookletDocument` multi-pages), `StateducReportExcelGenerator`, `openapi.yaml`, Volumes 1/3/4,
Centre d'aide §11.

**Écrans (Razor + Tailwind + Alpine) :**
- `/integration-etatique` (`Views/StateIntegration/Index.cshtml` + `wwwroot/js/state-integration.js`) —
  3 onglets : **Export Planète** (année + périmètre + format CSV/JSON → téléchargement, message
  actionnable si le code établissement manque), **Rapport STATEDUC** (année + date d'observation →
  agrégats à l'écran avec encadré « Données incomplètes » + téléchargement PDF/Excel), **Certificats
  de mutation** (registre paginé + révocation avec motif obligatoire). Réservé Directeur ; le
  Secrétariat n'a que l'onglet Certificats. Entrée de menu sous « Gestion Scolaire ».
- **Fiche élève** (`Views/Students/Index.cshtml` + `students.js`) — ligne « IEN » dans le cartouche
  d'identité (numéro provisoire distingué visuellement) + modale « Gérer l'IEN » : saisie du numéro
  officiel, ou bouton « Générer un numéro provisoire ».
- **Page publique** `/verifier/mutation/{token}` (`Views/StateIntegration/VerifyMutation.cshtml` +
  `verify-mutation.js`, layout `_LayoutPublic`) — scan du QR par l'école d'accueil : statut
  (authentique / révoqué / introuvable) + numéro, date, établissement émetteur. **Aucune donnée
  d'élève.** `unknown` renvoyé en HTTP 200, jamais 404.

**Vérifié au navigateur / `curl` sur PostgreSQL réel (skill `run`, 30/08/2026)** : login Directeur →
IEN officiel + provisoire (`P` + code + millésime + séquence + clé Luhn) → export Planète CSV (BOM,
`;`, cellules vides pour l'absent) → STATEDUC JSON/PDF/Excel → délivrance d'un certificat
(`MUT-2025-0001`, PDF + QR) → révocation (motif obligatoire, 2ᵉ tentative en 409) → vérification
publique anonyme du certificat révoqué. `dotnet build` **0 erreur** ; `SimenComplianceTests`
**26/26** ; `RlsCoverageTests` **vert** ; suite unitaire **1004/1004** ; intégration ciblée
(Students, ReportCards, RLS) **vert**.

**Écart trouvé et corrigé pendant cette vérification :** `StudentMutationCertificate.Reason` portait
un `HasDefaultValue(Autre)`. La valeur CLR par défaut de l'enum (`Demenagement` = 0) étant aussi celle
qu'EF Core lit comme « propriété non affectée », une mutation réellement pour « Déménagement » aurait
été enregistrée « Autre » **en silence**. Défaut base retiré (`FixMutationCertificateReasonDefault`,
additive et réversible) ; le Handler fournit toujours `Reason`. Par ailleurs, la génération d'un IEN
provisoire sans code établissement renvoyait un 500 (`InvalidOperationException` de
`NationalIenGenerator`) — désormais un 409 actionnable, même message que le refus de l'export Planète.

**JGK-M05 câblé (30/08/2026)** : *Paramètres → Établissement* porte un bloc « Intégration étatique
(SIMEN) » (code établissement national, n° d'autorisation, code de circonscription, coordonnées GPS —
les deux ensemble ou aucune) ; la *fiche enseignant* (création + modification) porte un bloc replié
« Informations pour le rapport STATEDUC » (genre, diplômes académique et professionnel, statut
administratif, matricule de solde, date de première prise de service). `UpdateCurrentSchoolCommand` /
`SchoolProfileDto` / `CreateTeacherCommand` / `UpdateTeacherCommand` / `TeacherProfileDto` étendus.
Vérifié : `GET /state-integration/planete/export` passe de 409 à 200 dès que le code établissement est
posé.

**Écrans complétés (30/08/2026)** : l'écran `/integration-etatique` ne se contentait que de lister et
révoquer les certificats — aucun bouton ne permettait de délivrer un certificat (`POST
.../mutation-certificate`, livré côté API depuis JGK-M06 mais orphelin côté écran) ni de télécharger le
livret de compétences (`GET .../skills-booklet`, JGK-M07, jusque-là inaccessible en dehors de l'aide en
ligne). L'onglet Certificats porte maintenant deux modales : « Délivrer un certificat » (recherche
élève par nom/matricule — même widget que `/caisse` — puis motif, destination, téléchargement immédiat
du PDF, et proposition du livret pour le même élève/année une fois délivré) et « Livret de compétences »
(téléchargement autonome, hors mutation). Recherche élève NON rattachée à la fiche élève : ce choix
évite de dépendre d'un écran distinct pour ce dernier kilomètre. `state-integration.js` / `Views/State
Integration/Index.cshtml` étendus, aucun autre écran touché.

**Tests d'intégration ajoutés (30/08/2026)** — comblent le manque ci-dessus :
`tests/SamaEcole.IntegrationTests/StateIntegration/` (`IenProvisionalSequenceTests`,
`StateIntegrationRefusalTests`, `GetStateducReportQueryHandlerTests`), 13 tests, vérifiés contre
PostgreSQL réel (Testcontainers, rôle applicatif) : unicité de la séquence IEN sous 20 générations
concurrentes, rembobinage sur rollback, séquence par école, refus 409 (export Planète et IEN
provisoire) sans code établissement, et agrégats STATEDUC (périmètre = inscriptions non annulées, âge
à la date d'observation, enseignant archivé/sans genre non imputé).

**Carte scolaire & état civil du dossier d'examen — livré (30/08/2026, commits `86e4398` +
`729ce9d`).** Les trois champs « carte scolaire & état civil » du dossier d'examen
(`ExamDossier.ExamCenterCode`, `TableNumber`, `CivilRegistryDocumentStatus` — Volume 1 §23.4), jusque-là
présents en base et dans la configuration EF sans aucun câblage applicatif, sont maintenant saisissables
de bout en bout. `AssignExamCenterCommand` porte le code du centre et le numéro de table
(préserve-si-vide, jamais générés ni effacés par un envoi à blanc — même contrat qu'`ExamCenterName`) ;
`UpdateExamDossierCommand` porte `CivilRegistryDocumentStatus` en NULLABLE (`null` = préserve la valeur
en base, ne la ramène jamais à `NonFourni` en silence) — seul point d'écriture d'`EnRegularisation`,
désormais réellement sélectionnable. Le couple booléen existant (`BirthCertificatePresent` /
`CivilStatusConforming`) reste la source du statut Incomplet/Complet du dossier ; l'enum ne fait que le
COMPLÉTER. Lecture (`GetExamDossierDetail`, `GetExamDossiers`), `ExamsController`, `openapi.yaml` et
tests (Exams unitaires 34/34, intégration 33/33) à jour. Écrans : modale « Attribuer un centre » (code +
numéro de table), modale « Modifier le dossier » (select à 5 états), fiche détaillée (affichage des 3
champs + ligne « régularisation en cours » dans l'encadré pièces manquantes).

**JGK-T01 (30/08/2026) — corrigé** : retirer une matière à un enseignant échouait en 500
(`42501: permission denied for table teacher_subjects`). `UpdateTeacherCommandHandler` fait un
`DELETE` physique ; `AddTeachers` n'accordait que `SELECT, INSERT, UPDATE`. Micro-migration
`GrantDeleteOnTeacherSubjects` (Option A du ticket) : `GRANT DELETE` sur cette table de LIAISON —
« qualifié pour cette matière » n'a aucune valeur d'audit, et le `DELETE` est déjà accordé à des
tables de même nature (`refresh_tokens`, `classrooms`, paie, décaissements). Les tables à donnée
métier historisée restent sans `DELETE`. Test `TeacherSubjectUnassignmentTests` (rôle bridé + contrôle
SQL brut de la disparition physique).

**Trois arbitrages actés, validés par le client :**

1. **`Teacher.Gender` ajouté (nullable).** Le formulaire STATEDUC ventile tout le personnel en
   Hommes/Femmes ; les fiches enseignant existantes ne portent pas le genre. Une **troisième colonne
   « genre non saisi »** (absente du formulaire officiel) est ajoutée plutôt que d'imputer d'office à
   l'une des deux colonnes (faux et indétectable) ou de déduire le genre du prénom (faux en silence).
2. **Coordonnées GPS en deux `numeric(9,6)`** (latitude, longitude), jamais une chaîne « lat,lon ».
   `School.GpsCoordinates` est une propriété **calculée, non mappée** (`builder.Ignore`).
3. **IEN provisoire de secours préfixé `P`.** `NationalIenGenerator` produit des numéros
   `P` + code établissement + millésime + séquence + clé Luhn, marqués `Student.IsIenProvisional` et
   signalés partout. Le format national réel n'est **pas** imité — deux numéros indiscernables
   seraient le pire résultat. L'arrivée d'un IEN officiel écrase le provisoire ; l'inverse est refusé.

### Examens officiels (26/08/2026) — écran livré, backend déjà complet

Écran `/examens` (`Views/Exams/Index.cshtml` + `wwwroot/js/exams.js`) : 4 onglets (Sessions, Dossiers,
Audit, Statistiques). Le backend (routes, migration RLS, PDF, export) couvrait déjà l'intégralité du
backlog Module J (JGK-J01 à J08) ; seul l'écran manquait. Un `Enseignant` charge l'onglet Dossiers en
lecture seule (liste + fiche détaillée), borné par le serveur à ses classes assignées (JGK-J08) —
tous les autres onglets et toutes les actions d'écriture restent Directeur/Secrétariat.

### Classes passerelles / accélérées (04/08/2026) — option désactivée par défaut

Une classe peut déclarer qu'elle valide **deux niveaux** en une année scolaire (« CI-CP », « 6e-5e »),
pour les parcours d'intégration des élèves venus des écoles coraniques. `Classroom.IsAccelerated`
(faux par défaut) + `Classroom.TargetLevel` (migration `AddAcceleratedClassrooms`, strictement additive).

- **Pas de table de niveaux, et c'est délibéré.** `Classroom.Level` reste le CYCLE en texte libre ; le
  niveau réel vit dans le nom de la classe. `TargetLevel` est donc un **libellé** (« CP », « Cinquième »),
  pas un identifiant — introduire une énumération de niveaux est explicitement interdit (AGENTS.md,
  `Classroom.Level`). La nomenclature par cycle et la reconnaissance du niveau depuis le nom vivent dans
  `ClassroomGradeLevels`, seule source pour la liste déroulante de l'écran **et** pour la délibération.
- **Délibération** : `ClassroomPromotion.ValidatedLevels` — un élève **admis** en classe passerelle valide
  le niveau courant ET le niveau cible ; une classe ordinaire valide un seul niveau, comme avant. Aucune
  décision (ou un redoublement/exclusion) ne valide rien.
- **Documents** : mention « Cursus Accéléré Passerelle » sur le reçu d'inscription, le reçu de caisse, le
  bulletin et le PV de délibération. Les documents d'une classe ordinaire sont inchangés au caractère près.

### Structure d'évaluation modulable / grilles APC (22/08/2026) — option désactivée par défaut

Le Directeur configure lui-même la grille d'évaluation d'un niveau, et le bulletin PDF s'y adapte : plus
aucun tableau de matières codé en dur. Migration `AddHierarchicalEvaluationStructure`, strictement additive
(cinq colonnes nullables ou à défaut neutre sur `subjects`).

- **Deux niveaux, pas plus.** `Subject.ParentSubjectId` : un **domaine** (« Lang & Com. », « Français »)
  porte des **activités** (« P. Alphabétique », « Ressources », « Compétences »). Le bulletin n'imprime
  qu'une colonne de regroupement, dont le `RowSpan` se calcule sur le nombre d'activités — une troisième
  profondeur n'aurait aucune colonne où s'afficher, `SubjectHierarchyGuard` la refuse en 422.
- **`Subject.MaxScore` NULL veut dire « suis le barème du cycle »**, pas « pas de barème ». C'est ce repli
  qui garantit qu'aucune donnée existante ne change : une valeur par défaut fixe à 20 aurait discrètement
  relevé le plafond de tout le primaire, resté à /10. Résolution unique : `GradeCalculator.EffectiveMaxScore`,
  utilisée par la saisie, la correction, l'import Excel, la feuille de notes et le bulletin.
- **Les lignes de barèmes différents sont ramenées au barème du bulletin avant d'être moyennées**
  (`GradeCalculator.Rebase`) : déclarer « Compétences /60 » n'est pas déclarer un poids. Le poids reste le
  coefficient. L'appréciation d'une ligne se décide, elle, sur son **pourcentage** de réussite — une seule
  échelle de mentions pour tous les « Sur ».
- **Le bulletin imprime la grille ENTIÈRE**, cases vides comprises (`EvaluationStructureBuilder`), comme les
  modèles officiels — là où le tableau du secondaire ne liste que les matières notées. Le niveau qui ne
  déclare aucune hiérarchie retombe sur les tableaux d'origine, inchangés.
- **Écran** : `/matieres`, onglet « Structure d'évaluation » — domaines, activités, barème par ligne,
  réorganisation par flèches, entêtes des deux premières colonnes du bulletin.
- **Index unique** : passé à (SchoolId, Level, **ParentSubjectId**, Name, IsDeleted) avec `NULLS NOT DISTINCT`
  (PostgreSQL 15+). Sans cette clause, ajouter une colonne nullable à la clé aurait fait cesser à l'index
  d'interdire deux matières de même nom au même niveau. Les deux moitiés de la règle sont testées contre un
  vrai PostgreSQL (`SubjectStructureEndpointsTests`).

### Présences & Discipline — bilan actionnable et suite des convocations (02/09/2026)

Les deux modules étaient livrés depuis des sprints, **et sans aucun lien entre eux**. Le bilan
d'assiduité comptait les retards et les absences ; convoquer un parent obligeait à ouvrir un autre
écran et à ressaisir ces chiffres de mémoire. Symétriquement, une convocation n'avait ni statut ni
compte rendu : le registre listait des rendez-vous, jamais des entretiens tenus.

- **Convoquer depuis le bilan.** `/reports/attendance` porte une colonne « Action » : sur toute ligne
  présentant au moins un retard ou une absence, « Convoquer » ouvre une convocation dont le **motif
  est pré-rempli** avec les chiffres réellement comptés sur la période affichée, et reste modifiable.
  L'avis PDF s'imprime dans la foulée, sans passer par `/convocations`.
- **Aucun seuil automatique.** Le bilan propose, le Directeur décide (Volume 1 §18.2). L'option d'un
  seuil paramétrable avec génération par lot — sur le modèle de `GenerateDebtorReminderBatches` — a
  été écartée à l'arbitrage du 02/09/2026 ; la porte reste ouverte, rien n'a été construit dans ce sens.
- **Colonne masquée au Secrétariat.** Le rapport lui est ouvert, `ParentSummonsController` ne l'est pas
  (SuperAdmin, Directeur, Surveillant) : l'action n'apparaît qu'à l'intersection des deux rôles.
- **Suite de l'entretien** (Volume 1 §18.3, migration `AddParentSummonsOutcome`) : `Status`,
  `OutcomeNotes`, `ClosedAt`, `ClosedByUserId` sur `ParentSummons`, plus
  `PATCH /parent-summons/{id}/outcome`. Trois invariants, figés par `ParentSummonsIsolationTests` :
  la suite se pose **une seule fois** et depuis `Scheduled` seulement (sinon 422, jamais d'écrasement) ;
  un **compte rendu est exigé** pour « non honorée » et « reportée », facultatif pour « honorée » ;
  les convocations **sans suite passent en tête** du registre, quelle que soit leur date.
- **L'avis PDF n'a pas changé** — et ne doit pas changer : c'est la pièce remise **avant** l'entretien,
  elle ne peut pas porter une suite qui n'existe pas encore au moment de son impression.
- **Migration : `defaultValue` repris à la main.** L'échafaudage EF proposait la chaîne vide pour
  `Status`, qui n'est pas un `ParentSummonsStatus` : toute convocation antérieure se serait relue en
  `""` et aurait fait lever la conversion enum ↔ texte à la première lecture du registre. Corrigé en
  `'Scheduled'` avant application.

### Zone de danger — réinitialisation des données d'essai (22/08/2026)

Le Directeur teste l'application avec des données fictives, puis remet son établissement à neuf depuis
l'onglet **Paramètres → Configuration**, en bas d'écran. `POST /schools/current/reset-data`, migration
`AddSchoolDataReset`.

- **Seule exception du produit à la règle #6** (aucune suppression physique). Elle est bornée : Directeur
  uniquement, sur SON école, après saisie de `PURGER` ou du nom de l'établissement, et journalisée —
  `ResetSchoolDataCommand` est `IAuditableRequest`, et `audit_logs` n'est pas purgé.
- **Pourquoi une fonction PostgreSQL et non un `ExecuteDelete` EF Core.** Le rôle applicatif n'a
  volontairement aucun droit de `DELETE` sur les tables métier — chaque migration lui accorde
  « SELECT, INSERT, UPDATE, jamais DELETE », de sorte que la règle #6 est tenue par la base et pas
  seulement par le code. Lui accorder le `DELETE` aurait affaibli cette garantie pour tout le produit,
  définitivement, au bénéfice d'un seul écran. `reset_school_data` est donc `SECURITY DEFINER` : elle
  ne donne pas un droit, elle donne UNE opération, dont la liste des tables et l'ordre des clés
  étrangères vivent dans la base.
- **Un `SECURITY DEFINER` est exempté de RLS** (droits du propriétaire). La fonction reconstitue donc
  elle-même la barrière : elle refuse d'agir si `p_school_id` diffère de `app.current_school_id`, ou si
  la session n'a aucun tenant. Les deux refus sont testés (`ResetSchoolDataTests`, catégorie
  `MultiTenant`) — c'est la seule opération du produit où l'isolation ne repose pas sur les policies.
- **Conservé** : comptes utilisateurs, fiche et réglages de l'école, années scolaires et trimestres,
  classes, matières, mentions, enseignants, bâtiments/salles, barème des frais et son historique,
  abonnement, journal d'audit. **Effacé** : élèves et tout ce qui pend à eux (inscriptions, échéanciers,
  notes, appréciations de bulletin, appels, discipline, convocations, SMS), paiements, sessions de
  caisse, décaissements, engagements financiers — et les **compteurs de matricules**, sans quoi le
  premier élève recréé porterait `ELEV-2026-0043`.
- **Les fichiers déjà téléversés ne sont pas supprimés** (photos d'élèves sous `wwwroot/uploads/`) : la
  purge est transactionnelle en base, un effacement disque ne l'est pas et laisserait, en cas d'échec,
  une incohérence pire que quelques fichiers orphelins devenus inatteignables.

---

## 3. Points de conformité traités (sprint du 27/07/2026)

1. **`Subscription` implémente `ITenantEntity`.** La table `subscriptions` figurait dans les
   `TenantTables` de la migration `EnableRowLevelSecurity` — la policy RLS était donc en place, mais
   le Global Query Filter EF Core manquait. Les **deux** protections exigées par la règle #2 sont
   désormais actives. Aucune migration : le changement est purement applicatif.
2. **Export financier `.xlsx` câblé à l'IHM.** L'endpoint `GET /finance/reports/revenue/excel`
   existait sans consommateur ; il est servi par le nouvel écran `/rapports/financiers`.
3. **Créneaux d'emploi du temps — création, modification ET suppression.** Un Enseignant ne peut plus
   agir sur le créneau d'un collègue. Le contrôle est porté par `ScheduleOwnershipAuthorizer` (même
   idiome qu'`AttendanceScopeAuthorizer`), partagé par les trois handlers : le `TeacherId` reçu est
   rapproché de sa propre fiche via `Teacher.UserId` (règle #10).
   La modification contrôle **deux** choses — le propriétaire actuel du créneau *et* le propriétaire
   demandé — sinon le verrou de la création se contournait en deux appels (créer pour soi, puis
   réattribuer à un collègue). La suppression est couverte pour la même raison : interdire la
   modification en laissant supprimer ne protégeait rien.
4. **Vue orpheline supprimée.** `Views/Absences/BilletPrint.cshtml` (« Page en construction ») et sa
   route `/billet-print` : les billets d'entrée et de sortie sont des PDF A5 générés côté serveur.

---

## 4. Notifications sortantes — file d'attente et accusés de réception (27/07/2026)

Le canal SMS existait mais envoyait **en ligne**, dans la requête ou l'événement métier. Il passe
désormais par une file. Spécification : `docs/Volume_4_API_Design.md` §20.

- **Mise en file plutôt qu'envoi direct.** `SmsDispatcher` conserve ses quatre gardes (formule,
  commutateur d'école, débit atomique du solde, historisation) mais inscrit le message en `Pending`
  au lieu d'appeler l'agrégateur. `SmsQueueHostedService` le remet hors requête, avec report
  exponentiel plafonné et abandon après `MaxAttempts` — l'abandon **recrédite** le solde.
- **Statuts complets.** `Pending` → `Sent` → `Delivered` ou `Failed`. « Envoyé » (accepté par
  l'agrégateur) et « Livré » (confirmé par accusé de réception) sont volontairement distincts : seul
  le second atteste qu'un parent a été prévenu.
- **Webhook DLR signé** — `POST /api/v1/webhooks/sms/{provider}`, public, HMAC-SHA256 du corps brut.
  Fermé par défaut : sans `Sms__WebhookSecret`, tout est rejeté en 401.
- **Le worker n'a aucun tenant** et ne peut donc rien voir sous RLS. Il passe par trois fonctions
  `SECURITY DEFINER` au périmètre étroit (migration `AddSmsQueue`), **jamais** par le rôle
  propriétaire — même idiome que `SchoolProvisioningStore` (règle #2). `SmsQueueTests` fige cette
  contrainte : un `DbContext` sans tenant voit zéro ligne, la fonction voit la file.
- **Nouveau déclencheur** : mise à disposition d'un bulletin (`SmsTrigger.ReportCard`) — un SMS
  d'avis en plus du PDF envoyé par WhatsApp/e-mail, un SMS ne transportant pas de pièce jointe.
- **Fournisseur WhatsApp réel** (`HttpWhatsAppSender`, API Cloud de Meta) en remplacement du repli
  qui se contentait de journaliser en production. Configuration absente ⇒ pas d'échec au démarrage,
  mais un avertissement explicite — `SmsServiceGuard`, jusqu'ici jamais appelé, l'est désormais.

---

## 5. Session de caisse câblée à l'écran (27/08/2026) — `/caisse` était inutilisable

`RecordPaymentCommandHandler` refuse (422) tout encaissement hors d'une session de caisse ouverte pour
l'utilisateur courant depuis toujours (Volume 1 §15.1) — mais **aucun fichier JS du dépôt n'appelait
jamais** `POST /finance/sessions/open` ni `POST /finance/sessions/{id}/close`, pourtant présents côté
API. Conséquence concrète : personne n'a jamais pu enregistrer un encaissement via l'écran `/caisse`,
pour aucun établissement, depuis la livraison de JGK-F02. Trouvé en poussant la vérification manuelle
de JGK-L03 (résilience réseau) jusqu'à un vrai paiement plutôt que de s'arrêter à l'affichage de l'écran.

- **Nouvelle requête** `GET /finance/sessions/current` (`GetCurrentCashierSessionQuery`) : la session
  ouverte de l'utilisateur courant (fonds initial, total encaissé, nombre de versements, exclut les
  paiements annulés — même agrégat que la clôture), ou `null` s'il n'en a aucune. C'est ce que l'écran
  interroge à l'ouverture pour savoir s'il doit bloquer l'encaissement ou afficher le statut.
- **Écran `/caisse`** : bandeau d'ouverture (déclaration du fonds initial) tant qu'aucune session n'est
  ouverte — la recherche d'élève et le formulaire d'encaissement restent masqués jusque-là ; bandeau de
  statut en direct une fois ouverte ; modale de clôture avec récapitulatif (fonds initial, encaissé,
  solde théorique) puis accès direct au rapport PDF.
- **Second bug corrigé au passage** : `DailyClosingReportPdfGenerator` ne posait pas
  `QuestPDF.Settings.License = LicenseType.Community` dans son constructeur statique, contrairement aux
  25 autres générateurs du projet — le rapport de clôture renvoyait donc systématiquement une erreur
  500 depuis sa livraison, jamais couvert par un test.
- **Vérifié de bout en bout** en conditions réelles (navigateur + PostgreSQL, skill `run`) : ouverture
  → encaissement avec coupure réseau simulée (JGK-L03) → clôture → téléchargement du rapport PDF.
  4 nouveaux tests d'intégration (`GetCurrentCashierSessionQueryTests.cs`).

### Contrôle du comptage physique et écarts de caisse (29/08/2026) — JGK-F09

La limite notée ci-dessus le 27/08/2026 (« la clôture ne confronte pas encore le numéraire compté au
solde théorique ») est comblée : `ActualCashAmount` est désormais un paramètre REQUIS de
`CloseCashierSessionCommand`, jamais une valeur facultative. L'écart se compare aux ESPÈCES attendues
(fonds initial + encaissements EN ESPÈCES uniquement, migration `AddCashierSessionDiscrepancy`), jamais
au total toutes méthodes — un virement ou un mobile money ne transite jamais par le tiroir-caisse
physique. Un écart non nul (manquant ou surplus) exige un motif avant de clôturer (422 sinon, la session
reste `Open`). Le rapport PDF affiche désormais espèces attendues/comptées/écart/motif. Écran `/caisse` :
comptage obligatoire dans la modale de clôture, motif révélé seulement après un premier refus serveur —
jamais anticipé côté client, qui ne connaît pas les espèces attendues. Vérifié en conditions réelles
(navigateur + PostgreSQL) : montant incorrect → 422 → motif → clôture → rapport à jour. Tests :
`CloseCashierSessionCommandValidatorTests.cs` (5/5), `CashierSessionClosingTests.cs` (6/6, dont la preuve
qu'un paiement non-espèces n'entre jamais dans l'écart).

### Inscription = calcul seul, encaissement = Caisse (30/08/2026)

L'écran `/inscriptions` mélangeait deux métiers : il **calculait** les frais dus **et** encaissait le
versement du jour (cases « ce frais est réglé », mode de règlement, création d'un `Payment` dans la
transaction). Contraire au principe déjà acté par la refonte du reçu du 25/08/2026 (`docs/design-
references/README.md` §1 : « le secrétariat n'encaisse aucun fonds »). Séparation appliquée :

- **`POST /api/v1/enrollments`** n'accepte plus `collectedFees` / `paymentMethod` (retirés de
  `CreateEnrollmentCommand`, du validateur et d'`openapi.yaml`). `CreateEnrollmentCommandHandler` fige
  le dû annuel, laisse `AmountPaid = 0` et ne crée **jamais** de `Payment` — la dépendance
  `ICurrentUserService` (n'y servait qu'à l'auteur du versement) a été retirée du constructeur.
- **Écran `/inscriptions`** : panneau renommé « Frais », barème en **lecture seule** + un simulateur
  de mensualités **purement indicatif** (`simMonths` / `simSubtotal()` dans `enrollments.js`, aucun
  appel réseau). Plus de mode de règlement, plus de « Encaissé ce jour ».
- **Caisse (`/caisse`) inchangée** : elle voit désormais l'intégralité du solde à encaisser et prend
  le premier versement comme les suivants (`RecordPaymentCommand`). Le solde/échéancier ne dépendaient
  déjà pas du `Payment` d'inscription (`InstallmentScheduleCalculator`).
- `EnrollmentReceiptDto` garde `CollectedLines` / `TotalCollected` / `PaymentMethod` (front + Finance)
  mais ils valent toujours vide / 0 / null pour une inscription ; `GetEnrollmentReceiptQuery` les
  renseigne encore à la relecture d'un reçu historique. Tests :
  `EnrollmentTests.A_New_Enrollment_Freezes_The_Debt_But_Records_No_Payment`,
  `EnrollmentsEndpointsTests.An_Enrollment_Never_Records_A_Payment_And_Leaves_The_Whole_Balance_Due`.
