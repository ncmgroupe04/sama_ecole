# Dispense d'une matière obligatoire — Spécification (v2, recentrée sur `main`)

**Date :** 25/09/2026
**Statut :** À valider. **Remplace** `2026-09-25-optional-subjects-design.md` (v1, conservée sur la branche
`backup/optional-subjects-v1`), devenue en partie redondante avec l'Évolution N°6 livrée sur `main`.
**Périmètre :** permettre au Directeur ou au Secrétariat de **dispenser un élève d'une matière obligatoire** pour
l'année active (EPS pour raison médicale, par exemple), avec un **motif**. La matière sort de ses moyennes et de sa
saisie de notes, et le **bulletin** la marque « Dispensé(e) ». Les **options** (LV2, option scientifique) ne sont
plus l'objet de cette branche : elles relèvent du modèle de `main`.

## 1. Contexte et arbitrage

Pendant la rédaction de la v1, `main` a reçu l'**Évolution N°6** (`679f5b9`, PR #38/#39) : programme d'une classe
(`class_subjects`, groupe d'options), choix d'options d'un élève par année (`student_subject_enrollments`) et
`SubjectFollowScope`, « seul point d'entrée des lecteurs » qui décide quelles matières un élève suit (résumé de notes,
bulletins, délibération, fiche élève, grille de saisie, import, feuilles PDF/Excel, garde de `CreateGrade`).

Arbitrage du propriétaire (25/09/2026) : **un seul modèle d'options — celui de `main`**. Cette branche ne garde que
ce que `main` n'a pas : la dispense d'une matière **obligatoire**, avec motif, et son rendu sur le bulletin.
Disparaissent de la v1 : `Subject.IsOptional/OptionGroup`, la table `enrollment_subject_exemptions`, la moitié
« options » du `PUT …/options`, `OptionSubjectIds` à l'inscription, le bloc « Langues & options » et la case
« optionnelle » de l'écran Matières, `EnrollmentOptionsPlanner`. Les correctifs demandés à la relecture finale
(règle « ligne active », deux moitiés indépendantes, activité sous une option) **disparaissent avec elles**.

## 2. Décisions

| # | Question | Décision |
|---|---|---|
| 1 | Modèle d'options | Celui de `main` (Évolution N°6). Rien à faire ici. |
| 2 | Représentation d'une dispense | Une ligne `(élève, matière, année scolaire)` avec un **motif obligatoire** (≤ 200 caractères). Table `student_subject_exemptions`, calquée sur `student_subject_enrollments` : rattachée à l'**élève et à l'année**, pas à l'inscription. Aucune ligne = aucune dispense. |
| 3 | Matières dispensables | Les matières **obligatoires et autonomes** de la classe de l'élève (classe de référence : `StudentYearClassroom`, celle des coefficients) : si la classe a un programme, ses `class_subjects` **actifs sans groupe d'options** ; sinon, les matières du **niveau** de la classe (comparé sans casse ni espaces de bord). « Autonome » = ni activité d'un domaine, ni domaine. |
| 4 | Effet sur le calcul et la saisie | Identique à une option non suivie : la matière est **exclue** par `SubjectFollowScope` (résumé, bulletins, délibération, fiche élève, grille, import, fiches PDF/Excel, `CreateGrade`). Les lecteurs de `main` en héritent **sans nouveau paramètre de constructeur**. |
| 5 | Notes déjà saisies | **Conservées** (règle #6), masquées. |
| 6 | Bulletin PDF | La matière dispensée **reste** sur le bulletin, à sa place : « Dispensé(e) » dans la zone des notes, coefficient **barré**, ni « Moy x coef » ni points, hors totaux (§4.4). Écart à la règle #12, **validé par le propriétaire**, consigné dans `docs/design-references/README.md`. |
| 7 | Confidentialité du motif | Le motif peut être médical. **Lecture et écriture réservées au Directeur et au Secrétariat** (les deux routes) ; jamais dans un DTO de bulletin, un document, un journal ni une erreur. |
| 8 | Portée temporelle | L'**année active** (comme les options de `main`). Une nouvelle année démarre sans dispense. |
| 9 | Rôles et module | Écriture et lecture : `Directeur, Secretariat` ; module `Pedagogy` requis (contrôleur `ClassSubjectsController`). |

## 3. Modèle de données

`StudentSubjectExemption` — table tenant `student_subject_exemptions` : `Id`, `SchoolId`, `StudentId`, `SubjectId`,
`SchoolYearId`, `Reason` (varchar(200), **non nul**), champs d'audit et de suppression logique.

- Global Query Filter **et** policy RLS (`TenantTables`), `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE`).
- FK composites tenant-safe `(SchoolId, StudentId)`, `(SchoolId, SubjectId)`, `(SchoolId, SchoolYearId)` en RESTRICT.
- Unicité partielle `(SchoolId, StudentId, SubjectId, SchoolYearId) WHERE NOT IsDeleted`.
- Ajoutée à `reset_school_data` et `delete_school_year` (migration de purges, technique des migrations précédentes).
- Pas de `xmin` : l'écriture est un remplacement idempotent de l'ensemble des dispenses de l'élève pour l'année.
- Migration **nouvelle, générée sur la base de `main`**, accompagnée de ses scripts SQL idempotents
  `docs/migrations/<id>.sql` et `<id>.rollback.sql` (convention de `746672a`).

## 4. Calcul

### 4.1 Requêtes et règles pures (`SamaEcole.Application.Exemptions`)

Classes **statiques et sans état** : `ExemptionQueries` (`ForStudentAsync`, `StudentsAsync`, `DispensableAsync`) et
`ExemptionRules` (`ValidateReason`, `Validate`, `LevelMatches`). Le motif est obligatoire, nettoyé (trim) et borné.

### 4.2 Intégration dans `SubjectFollowScope`

Quatre modifications de `main`, aucune ne change une signature publique existante :

| Membre | Modification |
|---|---|
| `ExcludedSubjectsAsync(student, année)` | Ajoute les matières dispensées aux matières exclues par les règles de programme (mémoïsé). Tous les lecteurs de `main` filtrent déjà dessus. |
| `RestrictedStudentsAsync(classe, matière, année)` | Retranche les élèves dispensés : `null` (toute la classe) reste `null` tant qu'aucun élève n'est dispensé, sinon l'ensemble des élèves autorisés. La grille, l'import et les fiches PDF/Excel en héritent. |
| `EnsureFollowsAsync(…)` | Message dédié « Cet élève est dispensé de cette matière » (422 sur le champ) au lieu du message générique. |
| `ExemptionsAsync(student, année)` *(nouveau)* | Les matières dispensées (nom, coefficient), mémoïsé — pour le bulletin. |

`ImportGradeSheetCommandHandler` : le message de rejet mentionne la dispense. `GetGradeSummaryQueryHandler` expose
`GradeSummaryDto.ExemptSubjects` (`ExemptSubjectDto(SubjectId, SubjectName, Coefficient effectif)` ; **jamais le
motif**). `EvaluationStructureBuilder` reçoit l'ensemble des matières dispensées : la ligne reste dans la grille APC,
marquée `EvaluationLineDto.IsExempt`. `ReportCardDataService` (14 constructions dans les tests) appelle la requête
statique : **aucun paramètre de constructeur ajouté**.

### 4.3 Règles

- **Invariant :** sans dispense, calcul et bulletin sont strictement ceux de `main`.
- Le rang par matière lit les résumés des camarades : un camarade dispensé sort du classement de cette matière.
- Primaire/Maternelle : mécanisme neutre ; le coefficient rapporté est 1.

### 4.4 Rendu du bulletin PDF (écart validé à la règle #12)

- Secondaire : la ligne garde son rang (tri par nom, comme les lignes notées) ; « Dispensé(e) » sur Devoir/Comp/Moy ;
  coefficient barré ; « Moy x », T.H et appréciation vides ; rang « — » ; hors totaux.
- Primaire (pas de coefficient) : « Dispensé(e) » sur Devoir/Comp/Moy ; T.H vide ; rang « — ».
- Grille APC : « Dispensé(e) » sur Notes et Sur ; appréciation vide.
- Le bulletin bilingue arabe suit la même règle. Aucune autre modification de la mise en page.

## 5. API (`/api/v1/class-subjects/students/{studentId}/exemptions`)

Dans `ClassSubjectsController` (module `Pedagogy`, `[Authorize(Roles = StaffRoles)]` sur **les deux routes**) :

- `GET` → `{ studentId, schoolYearId, subjects: [{ subjectId, name, isExempt, reason, gradeCount }] }` : les matières
  dispensables de la classe de l'élève, avec l'éventuelle dispense et son motif, et le nombre de notes de l'année
  qu'une dispense masquerait. 404 si l'élève est inconnu (ou d'une autre école).
- `PUT` avec `{ exemptions: [{ subjectId, reason }] }` : **remplace** l'ensemble des dispenses de l'élève pour l'année
  active (liste vide = plus aucune dispense), en une transaction (retrait par suppression logique, mise à jour d'un
  motif modifié, ajout des nouvelles). 422 : aucune année active, élève sans classe, matière non dispensable, motif
  vide ou trop long, doublon. 409 : collision sur l'index unique. 204 sinon.

Format d'erreur normalisé (`docs/Volume_4_API_Design.md` §0.4) ; `schoolId` lu du JWT (règle #10).

## 6. Écrans

Fiche élève : une section **« Dispenses »**, sous « Matières optionnelles » de `main`, visible et modifiable par ceux
qui gèrent l'élève (`canManageStudent`) : une case par matière dispensable et un champ **Motif** obligatoire quand la
case est cochée ; le bouton « Enregistrer » reste désactivé tant qu'un motif manque ; une alerte annonce le nombre de
notes qui seront masquées (conservées). Masquée si la classe n'a aucune matière dispensable. Fiche d'aide dédiée.
Aucun changement des écrans Inscriptions, Matières et Saisie des notes.

## 7. Tests

- Règles pures : motif (vide, 200/201 caractères), doublon, matière non dispensable.
- Base (SQL brut, rôle applicatif) : RLS, `WITH CHECK`, unicité partielle et recréation après suppression logique,
  purge d'année et réinitialisation, borne du motif.
- Requêtes : matières dispensables (classe avec programme, classe sans programme, domaine/activité exclus, option
  exclue) ; dispenses d'un élève par année ; élèves dispensés d'une matière ; isolation de l'école.
- `SubjectFollowScope` : exclusion, `RestrictedStudentsAsync` (`null` préservé, sous-ensemble), `EnsureFollowsAsync`.
- Calcul : sans dispense = comme avant ; matière dispensée absente du sommaire, total des coefficients adapté,
  `ExemptSubjects` (coefficient effectif, pas de motif) ; fiche élève cohérente avec le bulletin ; grille APC marquée.
- Saisie : grille sans l'élève dispensé, import refusé avec message dédié, `CreateGrade` 422.
- API : lecture et écriture (remplacement, motif modifié, liste vide), 404 inter-école, **403 pour Finance et
  Enseignant sur les deux routes**, 422 dont motif manquant.
- Bulletin : `GradeRows()` (ordre, nature des lignes), tenue sur une page A5 des trois tableaux, câblage données →
  `ReportCardDto`, absence de motif sur tout DTO de bulletin.
- Front : logique pure des dispenses, section de la fiche (deux états, motif obligatoire, payload).

## 8. Documentation

Cahier des charges (nouvelle sous-section à la suite du §8.8 de `main`), Volume 3 (table), Volume 4 (routes),
`openapi.yaml`, Volume 7 (matrice et confidentialité du motif), `docs/design-references/README.md` (l'écart validé),
fiche d'aide `help.js`, `ACTIVE_CONTEXT.md`.

## 9. Hors périmètre

- Tout ce qui concerne le **choix des options** : modèle de `main`.
- Dispense d'une **option** (l'élève ne la choisit simplement pas) et dispense **par période** (la portée est l'année).
- Matière facultative « à points bonus » ; livret de compétences (il ignore le marquage « Dispensé(e) »).
- Mise à jour ou suppression d'une **note existante** sur une matière dispensée : non bloquée (invisible dans les
  moyennes et les bulletins, joignable seulement par l'API) ; consigné au Volume 4.
- Affectation en masse des dispenses.

## 10. Points de vigilance

1. **Écart à la règle #12** : « Dispensé(e) » est le seul écart au bulletin de référence ; tout autre ajout serait à
   arbitrer séparément.
2. **Le motif est une donnée sensible** : jamais imprimé ni journalisé ; lu et écrit par le Directeur et le
   Secrétariat seulement.
3. **Programme modifié après une dispense** : si une matière dispensée est désactivée ou devient optionnelle dans le
   programme de la classe, la dispense reste enregistrée mais n'est plus proposée ; elle continue d'exclure la
   matière tant qu'elle existe. À nettoyer depuis la fiche.
4. **Changement de classe en cours d'année** : les matières dispensables sont celles de la classe de référence de
   l'élève ; une dispense sur une matière absente de la nouvelle classe reste, inerte (la matière n'y est pas suivie).
5. **Livret de compétences** : ignore le marquage.
6. **Deux sources d'exclusion** dans `SubjectFollowScope` (programme/options et dispenses) : tout nouveau lecteur doit
   passer par lui, jamais recalculer.
