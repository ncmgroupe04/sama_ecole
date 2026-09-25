# Matières optionnelles & dispenses — Spécification

**Date :** 25/09/2026
**Statut :** Validé, amendé le 25/09/2026 (volet dispense d'une matière obligatoire) ; plan d'implémentation :
`docs/superpowers/plans/2026-09-25-optional-subjects.md`.
**Périmètre :** Permettre qu'un élève ne suive qu'une partie des matières de sa classe, de deux façons :
1. **Option non suivie** (LV2 au choix, option scientifique) — une matière **optionnelle** que l'élève ne suit pas ;
2. **Dispense** — l'élève est exempté d'une matière **obligatoire** (ex. EPS pour raison médicale), avec un motif.

Dans les deux cas la matière ne compte plus dans ses moyennes, elle n'apparaît pas à la saisie des notes, et le
total des coefficients s'adapte. Le bulletin la traite selon le type (§4.4). Branche `feature/optional-subjects`,
issue de `main`.

## 1. Contexte

Une `Subject` est rattachée à un **niveau** en texte libre (`Subject.Level`), pas à une classe : il
n'existe aucune liste « matières de la 4ème A ». La structure du bulletin se compose par niveau
(`EvaluationStructureBuilder`), et la moyenne générale ne somme que les matières **ayant une note**
(`GradeCalculator.WeightedGeneralAverage`). Le bulletin secondaire n'imprime que les matières ayant une note ;
la grille APC, elle, imprime toutes les lignes du niveau. Aujourd'hui, un élève qui ne suit pas l'Arabe apparaît
dans la feuille de saisie de cette matière, sa note éventuelle entre dans sa moyenne, et (grille APC) sa ligne est
imprimée vide.

Il n'existe aucun filtre d'inscription par élève. La note est rattachée à l'élève et à la matière
(`Grade`), l'inscription (`Enrollment`) est rattachée à l'élève, à la classe et à l'année scolaire.

Modèle suivi : les coefficients par série (`docs/Volume_1_Cahier_des_Charges.md` §8.7, 24/09/2026) —
même règle « sans donnée, le calcul est celui d'avant », même portée annuelle via l'inscription.

## 2. Décisions actées

| # | Question | Décision |
|---|---|---|
| 1 | Exclusivité entre options | **Groupe d'options** : `Subject.OptionGroup` (texte libre, ex. « LV2 »). Au plus une matière choisie par groupe ; une option sans groupe est cumulable et se coche librement. |
| 2 | Élève sans choix enregistré | **Il suit toutes les options** (comportement actuel). Aucun bulletin ne change au déploiement ni à l'activation d'une option. |
| 3 | Représentation en base | **Dispenses** (`enrollment_subject_exemptions`), pas des choix : « aucune ligne » = « suit tout ». |
| 4 | Portée du choix | Sur l'**inscription** (donc par année scolaire). Une réinscription repart sans dispense. |
| 5 | Notes déjà saisies d'une matière dont l'élève est dispensé | **Conservées** (règle #6), masquées du bulletin et des moyennes. |
| 6 | Matières éligibles | Matière **autonome** uniquement : ni domaine parent, ni activité APC. Optionnelle ou obligatoire (décision 8). |
| 7 | Rôles | Lecture : ceux qui lisent l'inscription. Écriture : **Directeur + Secrétariat**. Module `Pedagogy` requis. |
| 8 | Dispense d'une matière **obligatoire** | Même table et même calcul que l'option non suivie ; la ligne porte un **motif** (`Reason`), **obligatoire** pour une matière obligatoire (422 sinon), facultatif pour une option. Le type est **déduit de `Subject.IsOptional`** : pas de colonne « type ». |
| 9 | Bulletin PDF | **Selon le type** : option non suivie → ligne **absente** ; dispense d'une matière obligatoire → ligne **« Dispensé(e) »**, coefficient barré, hors totaux (§4.4). |
| 10 | Règle n°12 (bulletin = `docs/design-references/`) | La ligne « Dispensé(e) » est un **écart assumé** et validé par le propriétaire (25/09/2026) : il est consigné dans `docs/design-references/README.md`. Aucun autre élément du bulletin ne change. |

## 3. Modèle de données

### 3.1 `Subject`

- `IsOptional` (bool, défaut `false`) — toutes les matières existantes restent obligatoires.
- `OptionGroup` (texte, nullable, longueur bornée) — significatif seulement si `IsOptional`. Comparé sans
  tenir compte de la casse ni des espaces de bord, comme `Subject.Level`.
- Validation Create/Update : `IsOptional` refusé (422) sur une matière qui a un parent ou des enfants ;
  `OptionGroup` renseigné sans `IsOptional` refusé.

### 3.2 `EnrollmentSubjectExemption` (nouvelle table tenant `enrollment_subject_exemptions`)

`Id`, `SchoolId`, `EnrollmentId`, `SubjectId`, `Reason` (texte, nullable, longueur bornée à 200), champs
d'audit et de suppression logique (`IsDeleted`, `DeletedAt`, `DeletedBy`).

`Reason` : obligatoire (non vide) pour la dispense d'une matière **obligatoire**, imposé par la commande (§5.2) ;
pour une option non suivie il reste vide (la ligne dit seulement « ne suit pas »).

- Global Query Filter **et** policy RLS : la table est ajoutée à `TenantTables` (AGENTS.md règle #2).
- Le rôle applicatif `sama_ecole_app` reçoit ses droits sur la nouvelle table dans la migration.
- Unicité (`SchoolId`, `EnrollmentId`, `SubjectId`) hors lignes supprimées, tenue par la base.
- Une ligne est **active** quand la matière est encore `IsOptional` **ou** quand la ligne porte un `Reason` :
  repasser une matière en « obligatoire » rend ses lignes d'option, sans motif, inertes (la matière revient à
  tous, sans purge).
- Ajoutée à `reset_school_data` et aux purges d'année, comme `subject_coefficient_overrides`.
- Pas de `xmin` : le choix d'options n'est pas une donnée sensible au sens de la règle #5 ; l'écriture est
  un remplacement idempotent (§5.2).

### 3.3 Migration

Une migration `AddOptionalSubjects` : deux colonnes sur `subjects` (avec défauts), une table, sa policy
RLS, ses droits. Aucune migration existante n'est modifiée.

## 4. Calcul

### 4.1 Résolveur unique

Classe **statique** `SubjectExemptions` :

- `ForStudentAsync` renvoie un `StudentExemptions` (les deux sortes de dispenses, dont `Mandatory` — les matières
  obligatoires dispensées, avec nom et coefficient — et `HiddenIds` — les options non suivies) pour l'inscription
  **active** (non annulée) de l'année ;
- `StudentsExemptFromAsync` renvoie les élèves dispensés d'une matière.

Un élève sans inscription active pour l'année, ou sans dispense, obtient l'ensemble vide.

### 4.2 Lecteurs qui appliquent le filtre

| Lecteur | Effet |
|---|---|
| `GetGradeSummaryQueryHandler` | Lignes de notes des matières dispensées écartées avant calcul. Le total des coefficients s'adapte (22 au lieu de 25). Bulletins PDF, bulletins de classe et délibération en héritent. |
| `GetStudentDetailQueryHandler` (`BuildTermReportsAsync`) | Même filtre, par année, pour que la fiche ne contredise jamais le bulletin. |
| `EvaluationStructureBuilder` | Nouveau paramètre « matières dispensées » : une option non suivie **disparaît** du tableau, même sans note ; une matière obligatoire dispensée **reste** avec un indicateur « dispensée » que le PDF rend (§4.4). |
| `GetClassGrades`, `GetGradeSheetPdf`, `GetGradeSheetExcel` | L'élève dispensé de la matière n'apparaît pas dans la liste de saisie. |
| `ImportGradeSheet` | Une ligne d'élève dispensé est **rejetée** (erreur de ligne, message dédié) — jamais écrite. |
| `CreateGradeCommandHandler` | Refus (422, erreur sur le champ) si l'élève est dispensé : filet de sécurité serveur. |

Pour que le PDF sache **quelle ligne marquer**, le résumé de notes expose les matières obligatoires dispensées :
`GradeSummaryDto.ExemptSubjects` est une liste de `ExemptSubjectDto(SubjectId, SubjectName, Coefficient)` (le
coefficient **effectif**, pour l'imprimer barré ; le **motif n'y figure jamais**), vide par défaut — un client ou
un test existant n'est pas affecté. Ces matières n'entrent ni dans `Subjects` ni dans les totaux. Câblage :
`GetGradeSummaryQueryHandler` → `ReportCardDataService` → `ReportCardDto.ExemptSubjects` →
`ReportCardDocument.GradeRows()`.

L'année de référence est celle de la période (`Term.SchoolYearId`), comme pour les surcharges de coefficient.

### 4.3 Règles

- **Invariant :** sans dispense, le résultat de chaque lecteur est strictement celui d'avant.
- Le classement continue de comparer les moyennes générales ; deux élèves aux options différentes sont
  comparés sur la moyenne, non sur le total de points (pratique actuelle inchangée).
- Primaire/Maternelle : mécanisme neutre (les options ne s'y appliquent pas en pratique), mais non bloqué
  par le code — le niveau étant un texte libre, c'est la validation §3.1 qui protège les grilles APC.

### 4.4 Rendu du bulletin PDF (décisions 9 et 10)

- **Option non suivie** : aucune ligne ; c'est déjà l'effet d'une matière sans note.
- Primaire : pas de colonne coefficient, « Dispensé(e) » couvre Devoir, Comp et Moy ; grille APC :
  `EvaluationLineDto.IsExempt`, « Dispensé(e) » couvre Notes et Sur ; la ligne garde le rang alphabétique qu'elle
  aurait eu si elle avait été notée.
- **Dispense d'une matière obligatoire** : la ligne garde son rang et son libellé ; la zone des notes porte
  « Dispensé(e) » ; le coefficient s'affiche **barré** ; ni « Moy x coef » ni points ; les totaux de
  coefficients et de points l'ignorent.
- Aucune autre modification de la mise en page (règle n°12) ; l'écart est consigné dans
  `docs/design-references/README.md`. Le bulletin bilingue arabe suit la même règle.

## 5. API

### 5.1 Matières

Les DTO de création/modification de matière gagnent `IsOptional` et `OptionGroup`. Les lectures les
renvoient. Aucune nouvelle route.

### 5.2 Options d'une inscription

- `GET /api/v1/enrollments/{id}/options` → matières optionnelles du niveau de la classe **courante de l'élève**
  (`Student.ClassroomId`), groupées, avec l'état de chaque matière (suivie / dispensée) et `hasExplicitChoice`
  (vrai si **au moins une option** a une dispense), plus `mandatorySubjects` (matières obligatoires du niveau,
  avec `isExempt`, `reason`, `gradeCount`), ainsi que le nombre de notes que masquerait chaque dispense.
- `PUT /api/v1/enrollments/{id}/options` avec `SetEnrollmentOptionsCommand(EnrollmentId, SubjectIds, Exemptions)`
  où `Exemptions` est une liste `(SubjectId, Reason)`. `SubjectIds` et `Exemptions` sont **indépendants** : `null` =
  ne pas toucher à cette moitié, une liste (même vide) la remplace :
  - les matières choisies appartiennent au niveau de la classe et sont optionnelles, sinon 422 ;
  - au plus une par `OptionGroup`, sinon 422 ;
  - chaque matière de `Exemptions` appartient au niveau de la classe, est **obligatoire** et autonome, et
    porte un `Reason` non vide, sinon 422 ;
  - les dispenses deviennent **l'ensemble des options du niveau non choisies** (sans motif) **plus** les
    matières de `Exemptions` (avec motif) ; l'ancien ensemble est remplacé dans une transaction (retrait par
    suppression logique, ajout des manquantes, mise à jour d'un motif modifié) ;
  - idempotent ; `PUT` refusé (422) si l'inscription est annulée ou n'est pas celle de l'année active ;
    inscription d'une autre école → 404 (filtre tenant).
- `POST` d'inscription (`CreateEnrollmentCommand`) accepte `optionSubjectIds` facultatif : les dispenses
  s'écrivent dans la **même transaction** que l'inscription (comme le matricule, règle #3). Omis, aucune
  dispense n'est créée.

Format d'erreur normalisé (`docs/Volume_4_API_Design.md` §0.4). Le `schoolId` vient du JWT (règle #10).

## 6. Écrans

1. **Matières (réglages)** : case « Matière optionnelle / au choix » et champ « Groupe d'options » avec
   suggestions des groupes existants, visible seulement si la case est cochée. Pastille « Option · LV2 »
   dans la liste.
2. **Inscription / réinscription** : bloc « Langues & options » si le niveau de la classe compte des
   options — un choix unique par groupe, une case par option sans groupe.
3. **Fiche élève › onglet « Options & dispenses »** : le bloc ci-dessus **plus** une section « Dispenses »
   (matières obligatoires du niveau : case « Dispensé(e) » et champ « Motif » obligatoire quand la case est
   cochée) — la dispense se saisit ici et non à l'inscription, car elle survient après coup et exige un motif.
   Modifiable par Secrétariat et Directeur. Mention
   « Options non renseignées : l'élève suit toutes les options » tant que `hasExplicitChoice` est faux.
   Alerte à l'enregistrement : « N notes seront masquées du bulletin (conservées) ».
4. **Saisie des notes** : aucun changement d'écran (décision du propriétaire du 25/09/2026 : hors périmètre, le
   rejet 422 à la saisie suffit).
5. **Aide** : fiche d'aide « Matières optionnelles » dans `help.js`.

## 7. Tests

Obligatoires (Finance, Notes, isolation tenant — AGENTS.md) :

- **Invariant** : sans dispense, sommaire de notes et bulletin identiques à avant (test de non-régression).
- Élève dispensé d'une matière notée : absente du sommaire, total des coefficients 22 au lieu de 25.
- `EvaluationStructureBuilder` : ligne dispensée absente, autres lignes inchangées.
- Résolution des groupes : un seul choix par groupe accepté, deux → 422 ; option sans groupe cumulable.
- Dispense d'une matière obligatoire : sans motif → 422 ; matière optionnelle placée dans `Exemptions` → 422 ;
  motif modifié → ligne mise à jour, sans doublon ; isolement par inscription.
- Bulletin : option non suivie → aucune ligne ; matière obligatoire dispensée → ligne « Dispensé(e) »,
  coefficient barré, absente des totaux (test du document PDF, sans capture d'écran).
- `SetEnrollmentOptionsCommand` : idempotence, remplacement, matière hors niveau ou non optionnelle refusée.
- Saisie refusée sur matière dispensée ; import : ligne ignorée ; feuilles de notes sans l'élève dispensé.
- Isolation : la nouvelle table est protégée par RLS (`--filter Category=MultiTenant`).
- Tests JS pour le bloc d'options (formulaire d'inscription, fiche élève, réglages des matières).

## 8. Documentation

Cahier des charges §8.8 (règle fonctionnelle), Volume 3 (table), Volume 4 (routes),
`ACTIVE_CONTEXT.md`, fiche d'aide `help.js`, et `docs/design-references/README.md` (une ligne : la mention
« Dispensé(e) » est l'unique écart validé à la référence du bulletin).

## 9. Hors périmètre (YAGNI)

- Liste de classe « élèves aux options à renseigner » et affectation en masse.
- Choix imposé par groupe (« LV2 obligatoire ») : un groupe sans choix reste permis.
- Dispenses au niveau de l'élève sur plusieurs années.
- Matière facultative « à points bonus » (seuls les points au-dessus de 10/20 comptent) : règle de calcul à part.
- Dispense par période (trimestre/semestre) : la portée est l'inscription, donc l'année.
- Emploi du temps et présences par option.

## 10. Points de vigilance

1. **Nouvelle matière dans un groupe existant** : un élève déjà placé (dispensé des autres) voit la
   nouvelle matière jusqu'à ce que le secrétariat mette son choix à jour — conséquence assumée du modèle
   par dispenses.
2. **Activation d'une option** : marquer « Espagnol » optionnelle ne change rien tant qu'aucun choix n'est
   enregistré ; c'est voulu, mais le secrétariat doit ensuite renseigner les choix.
3. **Six lecteurs** de la liste d'élèves et de matières (§4.2) : un lecteur oublié contredirait les autres.
   Le plan prévoit un test par lecteur.
4. **Changement de classe** : `UpdateStudentCommand` change `Student.ClassroomId` sans toucher l'inscription. Les
   dispenses restent rattachées à l'inscription ; les options proposées sont celles du niveau de la classe
   courante ; les dispenses d'un autre niveau sont retirées logiquement au prochain enregistrement de la moitié
   concernée.
5. **Écart à la règle n°12** : la ligne « Dispensé(e) » n'existe pas dans la référence graphique. Elle est
   validée par le propriétaire mais reste le seul point où le bulletin s'écarte de `docs/design-references/` ;
   tout autre ajout de mention serait à arbitrer séparément.
6. **Absence de verrou `xmin`** sur les dispenses (l'écriture est un remplacement idempotent d'un ensemble) :
   deux écrans ouverts en même temps, le dernier enregistrement gagne. À reconsidérer si un motif de dispense
   devenait une pièce à valeur probante.
7. **Choix sans dispense** : un choix qui ne dispense de rien est indiscernable de « aucun choix » :
   `hasExplicitChoice` reste faux, sans effet sur le calcul.
8. **Livret de compétences** : `GetSkillsBookletPdf` ignore le drapeau `IsExempt` : il imprime la ligne d'une
   matière dispensée comme avant.
9. **`PUT` en remplacement** : `PUT /enrollments/{id}/options` remplace chaque moitié qu'il reçoit : tout client
   doit renvoyer l'état complet de la moitié qu'il modifie — comme `PUT /subjects/{id}`.
