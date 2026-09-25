# Dispense d'une matière obligatoire — Spécification

**Date :** 25/09/2026 (réécriture du même jour ; remplace la spécification « Matières optionnelles & dispenses »)
**Statut :** **À valider par le propriétaire** avant toute modification de code. Plan d'implémentation :
`docs/superpowers/plans/2026-09-25-optional-subjects.md`.
**Branche :** `feature/optional-subjects` (nom conservé), à rebâtir sur `origin/main` — voir plan, Tâche 0.

## 0. Pourquoi une réécriture

La première version couvrait deux besoins : (1) une **option non suivie** (LV2, option scientifique), (2) la
**dispense d'une matière obligatoire**. Entre-temps, l'**Évolution N°6** (`docs/Volume_1_Cahier_des_Charges.md` §8.8,
déjà dans `main`) a livré le besoin (1) avec un autre modèle :

| Besoin (1) — options | Version initiale (cette branche) | Évolution N°6 (`main`) — **retenue** |
|---|---|---|
| Où vit l'option | `Subject.IsOptional` + `Subject.OptionGroup` | `ClassSubject.OptionGroup` (programme **de la classe**) |
| Choix d'un élève | table de **dispenses** sur l'inscription | `StudentSubjectEnrollment` (choix **positif**, par année) |
| Point d'entrée des lecteurs | `SubjectExemptions` (classe statique) | **`SubjectFollowScope`** (service scopé, mémoïsé) |
| API | `/enrollments/{id}/options`, `optionSubjectIds` | `/class-subjects/...`, `subjectOptionIds` |

Les deux modèles ne peuvent pas coexister (deux vérités pour « qui suit quelle matière »). **Cette spécification
abandonne tout le volet options** de la première version et ne garde que le besoin (2), rebâti sur
`SubjectFollowScope`. Ce qui disparaît : `Subject.IsOptional`, `Subject.OptionGroup`, la table
`enrollment_subject_exemptions`, `SubjectExemptions`, `OptionSelectionRules` (partie options),
`EnrollmentOptionsPlanner`, `SetEnrollmentOptionsCommand`, `GetEnrollmentOptionsQuery`, le bloc « Langues & options »
et les migrations `AddOptionalSubjects` / `AddOptionalSubjectsToPurges` (jamais appliquées en production).

## 1. Périmètre

Un élève peut être **dispensé d'une matière obligatoire de sa classe** (exemple : EPS pour raison médicale) pour
l'année scolaire, avec un **motif obligatoire**. Conséquences :

1. la matière **sort de ses moyennes** ; le total des coefficients de son bulletin s'adapte ;
2. il **n'apparaît plus** dans la feuille de saisie des notes de cette matière (écran, fiche PDF, modèle Excel) ;
   toute note tentée pour lui est refusée (saisie unitaire, import) ;
3. son **bulletin** garde la ligne de la matière, marquée **« Dispensé(e) »**, coefficient barré, hors totaux ;
4. le **motif** est une donnée sensible : lisible et modifiable **uniquement** par le Directeur et le Secrétariat
   (`[Authorize(Roles = "Directeur,Secretariat")]`), jamais imprimé, jamais renvoyé par un autre endpoint.

**Invariant (hérité de l'Évolution N°6) :** sans dispense enregistrée, le résultat de chaque lecteur — moyennes,
fiche élève, feuilles de notes, bulletin — est strictement celui d'avant.

**Hors périmètre :** option non suivie (Évolution N°6), dispense d'une **option** (l'élève ne la choisit simplement
pas), dispense par période, affectation en masse, dispense qui court sur plusieurs années, ligne « N élève(s)
dispensé(s) » à la saisie (retirée le 25/09/2026), tout changement d'écran de saisie.

## 2. Contexte : ce que `main` fournit déjà

- `SubjectFollowScope` (`src/SamaEcole.Application/ClassSubjects/SubjectFollowScope.cs`) est **le seul point
  d'entrée** « qui suit quelle matière, pour quelle année ». Ses lecteurs actuels : `GetGradeSummaryQueryHandler`,
  `GetStudentDetailQueryHandler`, `GetClassGradesQueryHandler`, `GetGradeSheetPdfQueryHandler`,
  `GetGradeSheetExcelQueryHandler`, `ImportGradeSheetCommandHandler`, `CreateGradeCommandHandler`. Trois méthodes :
  `ExcludedSubjectsAsync(élève, année)` (matières que l'élève ne suit pas), `RestrictedStudentsAsync(classe, matière,
  année)` (élèves autorisés, `null` = toute la classe), `EnsureFollowsAsync(...)` (garde d'écriture, 422).
- `SubjectFollowRules` (pur) : une matière n'est pas suivie si elle est désactivée ou d'un groupe d'options non
  choisi.
- `StudentYearClassroom.ResolveAsync(élève, année)` : la classe de référence de l'élève pour une année.
- `ClassSubjectsController` (`/api/v1/class-subjects`) : `StaffRoles = Directeur,Secretariat` ; année **active**
  résolue serveur ; module `Pedagogy` requis.
- Une classe **sans programme** (aucune ligne `class_subjects` : classes « Général / Collège », anciennes classes)
  garde le comportement d'avant : toute matière notée est suivie.

## 3. Décisions

### 3.1 Reprises de la première version (validées le 25/09/2026)

| # | Décision |
|---|---|
| D1 | Une dispense s'appuie sur un **motif obligatoire** (texte non vide après `Trim`, 200 caractères au plus). Le type « dispense » ne se déduit plus d'un drapeau : **toute ligne de la nouvelle table est une dispense**. |
| D2 | Notes déjà saisies d'une matière dispensée : **conservées** (règle #6), masquées des moyennes et du bulletin. La saisie affiche « N notes seront masquées » avant enregistrement. |
| D3 | Bulletin : ligne **« Dispensé(e) »**, coefficient **barré**, ni « Moy x coef » ni points, exclue des totaux. **Écart assumé à la règle #12**, seul écart, consigné dans `docs/design-references/README.md`. |
| D4 | Saisie des notes : **aucun changement d'écran** (le 422 serveur et l'absence de l'élève dans la grille suffisent). |
| D5 | Moyennes : le total des coefficients s'adapte (22 au lieu de 25) ; le classement compare les moyennes générales, comme aujourd'hui. |

### 3.2 Nouvelles décisions pour cette réécriture

| # | Décision | Statut |
|---|---|---|
| D6 | **Une seule porte : `SubjectFollowScope`.** La dispense n'ajoute ni classe statique ni second résolveur : `ExcludedSubjectsAsync` renvoie désormais « options non choisies **+** matières dispensées » ; `RestrictedStudentsAsync` retire les dispensés ; `EnsureFollowsAsync` nomme la cause. Les six lecteurs actuels en héritent **sans modification de constructeur**. Seul ajout de lecture : `ExemptSubjectsAsync` (le bulletin doit savoir **quelle ligne marquer**). | Proposé |
| D7 | **Clé de la dispense : (élève, année scolaire, matière)** — et non l'inscription. Même clé que `StudentSubjectEnrollment` et `StudentYearClassroom` ; une dispense survit à un changement de classe en cours d'année ; pas de dépendance à `Enrollment`. | **À valider** |
| D8 | **Matières éligibles** : la classe de référence de l'élève (année) a un programme → matières **actives, sans groupe d'options** de ce programme ; sinon → matières **autonomes** (ni domaine parent ni activité APC) dont le niveau est celui de la classe (comparaison sans casse ni espaces, comme `Subject.Level` ailleurs). Sans ce repli, une classe « Collège » sans programme — le cas typique de l'EPS — ne pourrait rien dispenser. | **À valider** |
| D9 | **Accès au motif** : lecture **et** écriture par `[Authorize(Roles = "Directeur,Secretariat")]` (constante `StaffRoles` du contrôleur). Le motif n'est porté que par `GET/PUT …/exemptions`. Il est **absent** de `GetStudentDetail` (lisible par tout rôle authentifié), de `GradeSummaryDto`, de `ReportCardDto`, des PDF, des exports et du journal d'audit. L'écran n'appelle le GET qu'aux rôles autorisés. | Proposé (consigne du 25/09) |
| D10 | **Année active seulement** pour l'écriture (année résolue serveur, règle #10). Les années passées se lisent (bulletins) mais ne se modifient pas. | Proposé |
| D11 | **Pas de verrou `xmin`** : l'écriture est un remplacement idempotent de l'ensemble des dispenses de l'élève pour l'année ; deux écrans ouverts, le dernier enregistrement gagne (vigilance §10.5). | À valider |

## 4. Modèle de données

### 4.1 Table `student_subject_exemptions` (tenant)

`Id`, `SchoolId`, `StudentId`, `SchoolYearId`, `SubjectId`, `Reason` (`varchar(200)`, **NOT NULL**), audit et
suppression logique (`IsDeleted`, `DeletedAt`, `DeletedBy`) — même gabarit que `student_subject_enrollments`.

- Clés étrangères composites `(SchoolId, StudentId)`, `(SchoolId, SchoolYearId)`, `(SchoolId, SubjectId)`, `ON DELETE
  RESTRICT`.
- Unicité `(SchoolId, StudentId, SchoolYearId, SubjectId)` **hors lignes supprimées** (index partiel `WHERE NOT
  "IsDeleted"`), tenue par la base ; entrée dans `UniqueConstraintCatalog`.
- **Global Query Filter et policy RLS** (règle #2) : la table est ajoutée à `TenantTables` ; le rôle `sama_ecole_app`
  reçoit `SELECT, INSERT, UPDATE` (jamais `DELETE`).
- Ajoutée à `reset_school_data` et aux purges d'année, comme `student_subject_enrollments`.

### 4.2 Migration

Une migration **nouvelle** `AddStudentSubjectExemptions` (table, index, RLS, droits), et une seconde
`AddStudentSubjectExemptionsToPurges` (fonctions de purge), sur le modèle de
`20260924213235_AddSubjectCoefficientOverrides` / `…ToPurges`. Scripts SQL idempotents + rollback dans
`docs/migrations/`. Aucune migration existante n'est modifiée. Aucune colonne ajoutée à `subjects`.

## 5. Calcul — extension de `SubjectFollowScope`

| Méthode | Avant (`main`) | Après |
|---|---|---|
| `ExcludedSubjectsAsync(élève, année)` | options non choisies ∪ matières désactivées | **∪ matières dispensées** (une seule lecture SQL de plus, mémoïsée dans le scope) |
| `ExemptSubjectsAsync(élève, année)` (**nouveau**) | — | matières dispensées (`SubjectId`) — **sans motif** |
| `RestrictedStudentsAsync(classe, matière, année)` | `null` = toute la classe ; sinon les élèves du groupe d'options | quand la matière a des dispensés : **élèves de la classe moins les dispensés** (jamais `null` dans ce cas) |
| `EnsureFollowsAsync(...)` | 422 « option non choisie ou désactivée » | 422 dédié « **élève dispensé de cette matière** » — message **sans motif** |

`SubjectFollowRules.ExcludedSubjects` gagne une surcharge pure prenant l'ensemble des matières dispensées (les tests
existants de `SubjectFollowRulesTests` restent verts). Aucun filtre `SchoolId` à la main (règle #2).

### 5.1 Lecteurs

| Lecteur | Effet |
|---|---|
| `GetGradeSummaryQueryHandler` | Les notes des matières dispensées sont écartées (déjà le cas pour `excluded`) ; **en plus**, `GradeSummaryDto.ExemptSubjects` liste les matières dispensées : `ExemptSubjectDto(SubjectId, SubjectName, Coefficient)` — coefficient **effectif** (surcharges de l'Évolution N°4 appliquées), **jamais le motif**. Liste vide par défaut ; membre facultatif **en dernier** du DTO. Bulletins de classe, PDF et délibération en héritent. |
| `GetStudentDetailQueryHandler` | Même filtre que le bulletin, par année : la fiche ne contredit jamais le bulletin. **Aucun motif** dans la réponse. |
| `GetClassGrades`, `GetGradeSheetPdf`, `GetGradeSheetExcel` | Élève dispensé absent de la liste (via `RestrictedStudentsAsync`, sans changer les handlers). |
| `ImportGradeSheet` | Ligne d'un élève dispensé : **erreur de ligne** si elle contient une note (message adapté), sans effet si elle est vide — déjà le comportement N°6, seul le texte s'élargit. |
| `CreateGradeCommandHandler` | 422 (via `EnsureFollowsAsync`). |
| `EvaluationStructureBuilder` (grille APC) | Reçoit les matières dispensées : la ligne **reste** avec `IsExempt` (rendue « Dispensé(e) »). |
| Livret de compétences | **Inchangé** (vigilance §10.4). |

### 5.2 Éligibilité et validation d'écriture (règles pures)

`SubjectExemptionRules` (pur, testé seul) :

- la matière est éligible (D8), sinon 422 « cette matière ne peut pas être dispensée » ;
- `Reason` : `Trim`, non vide, ≤ 200, sinon 422 sur le champ `exemptions[i].reason` ;
- pas de doublon de matière dans la liste, sinon 422 ;
- l'élève a une classe de référence pour l'année, sinon 422 « rattachez l'élève à une classe ».

## 6. Rendu du bulletin PDF (D3)

Trois tableaux dans `ReportCardDocument` (secondaire `ComposeGradesTable`, primaire `ComposeGradesTablePrimaire`,
grille APC `ComposeGradesTableApc`) :

- **Secondaire** : la ligne garde son rang alphabétique et son libellé ; la zone des notes porte « Dispensé(e) » ;
  coefficient **barré** ; ni « Moy x coef » ni points ; totaux de coefficients et de points inchangés (sans elle).
- **Primaire** : pas de colonne coefficient ; « Dispensé(e) » couvre Devoir, Comp et Moy.
- **Grille APC** : `EvaluationLineDto.IsExempt` ; « Dispensé(e) » couvre Notes et Sur.
- Câblage : `GetGradeSummaryQueryHandler` → `ReportCardDataService` → `ReportCardDto.ExemptSubjects` →
  `ReportCardDocument.GradeRows()` (`internal`, testable sans lire le texte PDF).
- Aucune autre modification de mise en page (règle #12). Le **motif n'est jamais imprimé**.

## 7. API

Dans `ClassSubjectsController` (`/api/v1/class-subjects`), à côté des options d'un élève ; module `Pedagogy` requis ;
**les deux routes** portent `[Authorize(Roles = StaffRoles)]` = `Directeur,Secretariat` :

| Route | Rôle | Effet |
|---|---|---|
| `GET /api/v1/class-subjects/students/{studentId}/exemptions` | Directeur, Secrétariat | Année active. Renvoie les matières **éligibles** de la classe de l'élève : `subjectId`, `subjectName`, `coefficient` (effectif), `isExempt`, `reason` (`null` si non dispensé), `gradeCount` (notes que la dispense masquerait ou masque déjà). 404 si l'élève n'existe pas dans l'école. |
| `PUT /api/v1/class-subjects/students/{studentId}/exemptions` | Directeur, Secrétariat | Corps `{ "exemptions": [ { "subjectId", "reason" } ] }` = **liste complète** des dispenses de l'année active (comme les options d'un élève). Remplacement idempotent dans **une transaction** : suppression logique des retirées, ajout des nouvelles, mise à jour d'un motif modifié. Réponse : les `subjectId` dispensés. 422 (§5.2), 404. `IAuditableRequest` (journal d'audit : action, acteur, IP — **pas** le corps). |

Format d'erreur normalisé (`docs/Volume_4_API_Design.md` §0.4). `schoolId` du JWT (règle #10). Un rôle non autorisé
reçoit **403** — jamais le motif, jamais une liste partielle.

## 8. Écrans

1. **Fiche élève › section « Dispenses »**, sous « Matières optionnelles » (Évolution N°6) : une ligne par matière
   éligible, case « Dispensé(e) », champ « Motif » **obligatoire** quand la case est cochée, alerte à
   l'enregistrement « N notes seront masquées du bulletin (conservées) ». **Section rendue et appelée seulement pour
   Directeur et Secrétariat** ; pour les autres rôles la fiche ne montre qu'un badge « Dispensé(e) » **sans motif**
   sur les matières concernées (issu du bulletin, pas du GET).
2. **Inscription** : **aucun** changement (la dispense survient après coup et exige un motif).
3. **Saisie des notes** : aucun changement (D4).
4. **Aide** : fiche « Dispense d'une matière » dans `help.js`, qui précise qui voit le motif.

## 9. Tests

Obligatoires (Notes, isolation tenant — `AGENTS.md`) ; ciblés, la suite complète reste lancée par le propriétaire :

- **Invariant** : sans dispense, `GetGradeSummary`, fiche élève, feuilles de notes et bulletin identiques à avant
  (non-régression) ; `SubjectFollowRulesTests` existants inchangés.
- Règles pures : matière éligible / inéligible (avec et sans programme, option d'un groupe, matière désactivée,
  activité APC) ; motif vide, blanc, 201 caractères ; doublon.
- Calcul : élève dispensé d'une matière notée → absente du sommaire, total des coefficients 22 au lieu de 25,
  `ExemptSubjects` renseigné avec le coefficient **effectif** (surcharge de classe) et **sans motif**.
- Lecteurs : élève dispensé absent de la grille, de la fiche PDF, du modèle Excel ; note saisie → 422 ; import →
  erreur de ligne ; fiche élève cohérente avec le bulletin.
- Bulletin : `GradeRows()` porte la ligne « Dispensé(e) » (secondaire, primaire, APC), coefficient barré, totaux
  sans elle ; **contrôle visuel** obligatoire (QuestPDF n'expose pas le texte).
- API : `PUT` idempotent (même corps deux fois = un seul jeu de lignes) ; motif modifié → ligne mise à jour, pas de
  doublon ; matière inéligible → 422 ; élève sans classe → 422 ; année active seulement ; élève d'une autre école →
  404 ; **403 pour Enseignant, Finance, Surveillant et tout rôle ≠ Directeur/Secrétariat, sur GET et sur PUT**.
- **Confidentialité du motif** : `GET /students/{id}`, `GradeSummary`, `ReportCardDto` sérialisé et journal d'audit ne
  contiennent jamais le motif saisi (chaîne sentinelle).
- **Isolation** : la table est protégée par RLS (`--filter Category=MultiTenant`) ; lecture inter-tenant vide,
  `DELETE` refusé pour `sama_ecole_app`.
- Tests JS (`node --test`) : logique de la section « Dispenses » (motif obligatoire, alerte, rôles).

## 10. Points de vigilance

1. **Ordre avec l'Évolution N°7** : sa PR (`claude/senegal-series-subjects-coefficients-ndqwkq`, dont
   `GetReportCardPdfQuery`, PV du conseil) n'est pas encore dans `main` ; le câblage bulletin de cette évolution
   touche les mêmes fichiers. Rebâtir la branche après la fusion de N°7 évite un conflit.
2. **Programme de classe modifié après coup** : si une matière dispensée devient une option ou est désactivée, la
   dispense reste en base mais n'a plus d'effet visible (la matière est déjà exclue) ; elle réapparaît si la
   matière redevient obligatoire.
3. **Nouvelle matière** ajoutée à la classe en cours d'année : suivie par tous, y compris les dispensés d'une autre
   matière — une dispense vise une matière, jamais « le sport en général ».
4. **Livret de compétences** (`GetSkillsBookletPdf`) : n'applique pas `SubjectFollowScope` aujourd'hui ; il continue
   d'imprimer la ligne d'une matière dispensée. À traiter séparément si le livret doit suivre.
5. **Pas de `xmin`** (D11) : dernier enregistrement gagnant sur un motif ; à reconsidérer si le motif devenait une
   pièce à valeur probante.
6. **Motif = donnée de santé possible** : à documenter au Volume 7 (accès restreint, jamais journalisé, jamais
   exporté) ; toute future extraction (export « santé », rapport de classe) doit l'exclure explicitement.
7. **Écart à la règle #12** : « Dispensé(e) » est l'unique mention hors référence graphique ; tout autre ajout est à
   arbitrer séparément.
8. **Lecteurs de `GetGradeSummary` ajoutés par l'Évolution N°7** (PV du conseil, statistiques de délibération) : non
   audités ici ; ils héritent de l'exclusion par construction, mais le plan (Tâche 3) vérifie qu'aucun ne compte
   une matière dispensée comme une note nulle une fois N°7 fusionnée.
