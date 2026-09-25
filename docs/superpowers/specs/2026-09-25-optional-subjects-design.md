# Matières optionnelles & dispenses — Spécification

**Date :** 25/09/2026
**Statut :** Validé en brainstorming le 25/09/2026, en attente de relecture puis de plan d'implémentation.
**Périmètre :** Permettre qu'un élève ne suive qu'une partie des matières de sa classe (LV2 au choix,
option scientifique) : la matière non suivie n'apparaît ni à la saisie des notes, ni sur le bulletin, ni
dans les moyennes, et le total des coefficients s'adapte. Branche `feature/optional-subjects`, issue de `main`.

## 1. Contexte

Une `Subject` est rattachée à un **niveau** en texte libre (`Subject.Level`), pas à une classe : il
n'existe aucune liste « matières de la 4ème A ». La structure du bulletin se compose par niveau
(`EvaluationStructureBuilder`), et la moyenne générale ne somme que les matières **ayant une note**
(`GradeCalculator.WeightedGeneralAverage`). Aujourd'hui, un élève qui ne suit pas l'Arabe voit donc
sa ligne « Arabe » (vide) sur le bulletin, et apparaît dans la feuille de saisie de cette matière.

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
| 6 | Matières éligibles | Matière **autonome** uniquement : ni domaine parent, ni activité APC. |
| 7 | Rôles | Lecture : ceux qui lisent l'inscription. Écriture : **Directeur + Secrétariat**. Module `Pedagogy` requis. |

## 3. Modèle de données

### 3.1 `Subject`

- `IsOptional` (bool, défaut `false`) — toutes les matières existantes restent obligatoires.
- `OptionGroup` (texte, nullable, longueur bornée) — significatif seulement si `IsOptional`. Comparé sans
  tenir compte de la casse ni des espaces de bord, comme `Subject.Level`.
- Validation Create/Update : `IsOptional` refusé (422) sur une matière qui a un parent ou des enfants ;
  `OptionGroup` renseigné sans `IsOptional` refusé.

### 3.2 `EnrollmentSubjectExemption` (nouvelle table tenant `enrollment_subject_exemptions`)

`Id`, `SchoolId`, `EnrollmentId`, `SubjectId`, champs d'audit et de suppression logique
(`IsDeleted`, `DeletedAt`, `DeletedBy`).

- Global Query Filter **et** policy RLS : la table est ajoutée à `TenantTables` (AGENTS.md règle #2).
- Le rôle applicatif `sama_ecole_app` reçoit ses droits sur la nouvelle table dans la migration.
- Unicité (`SchoolId`, `EnrollmentId`, `SubjectId`) hors lignes supprimées, tenue par la base.
- Ajoutée à `reset_school_data` et aux purges d'année, comme `subject_coefficient_overrides`.
- Pas de `xmin` : le choix d'options n'est pas une donnée sensible au sens de la règle #5 ; l'écriture est
  un remplacement idempotent (§5.2).

### 3.3 Migration

Une migration `AddOptionalSubjects` : deux colonnes sur `subjects` (avec défauts), une table, sa policy
RLS, ses droits. Aucune migration existante n'est modifiée.

## 4. Calcul

### 4.1 Résolveur unique

`SubjectExemptionLoader` (calqué sur `CoefficientOverrideLoader`) :

- `LoadAsync(studentId, schoolYearId)` → ensemble des `SubjectId` dont l'inscription **active** (non
  annulée) de l'année dispense l'élève ;
- `LoadForClassAsync(classroomId, schoolYearId)` → `élève → ensemble`, en une requête (bulletins de classe,
  feuilles de notes) pour éviter le N+1.

Un élève sans inscription active pour l'année, ou sans dispense, obtient l'ensemble vide.

### 4.2 Lecteurs qui appliquent le filtre

| Lecteur | Effet |
|---|---|
| `GetGradeSummaryQueryHandler` | Lignes de notes des matières dispensées écartées avant calcul. Le total des coefficients s'adapte (22 au lieu de 25). Bulletins PDF, bulletins de classe et délibération en héritent. |
| `GetStudentDetailQueryHandler` (`BuildTermReportsAsync`) | Même filtre, par année, pour que la fiche ne contredise jamais le bulletin. |
| `EvaluationStructureBuilder` | Nouveau paramètre « matières dispensées » : la ligne disparaît du tableau même sans note. |
| `GetClassGrades`, `GetGradeSheetPdf`, `GetGradeSheetExcel` | L'élève dispensé de la matière n'apparaît pas dans la liste de saisie. |
| `ImportGradeSheet` | Une ligne d'élève dispensé est signalée comme ignorée, jamais écrite. |
| `CreateGradeCommandHandler` | Refus (422, erreur sur le champ) si l'élève est dispensé : filet de sécurité serveur. |

L'année de référence est celle de la période (`Term.SchoolYearId`), comme pour les surcharges de coefficient.

### 4.3 Règles

- **Invariant :** sans dispense, le résultat de chaque lecteur est strictement celui d'avant.
- Le classement continue de comparer les moyennes générales ; deux élèves aux options différentes sont
  comparés sur la moyenne, non sur le total de points (pratique actuelle inchangée).
- Primaire/Maternelle : mécanisme neutre (les options ne s'y appliquent pas en pratique), mais non bloqué
  par le code — le niveau étant un texte libre, c'est la validation §3.1 qui protège les grilles APC.

## 5. API

### 5.1 Matières

Les DTO de création/modification de matière gagnent `IsOptional` et `OptionGroup`. Les lectures les
renvoient. Aucune nouvelle route.

### 5.2 Options d'une inscription

- `GET /api/v1/enrollments/{id}/options` → matières optionnelles du niveau de la classe, groupées, avec
  l'état de chaque matière (suivie / dispensée) et `hasExplicitChoice`, plus le nombre de notes que
  masquerait chaque dispense.
- `PUT /api/v1/enrollments/{id}/options` avec `SetEnrollmentOptionsCommand(EnrollmentId, SubjectIds)` :
  - les matières choisies appartiennent au niveau de la classe et sont optionnelles, sinon 422 ;
  - au plus une par `OptionGroup`, sinon 422 ;
  - les dispenses deviennent **l'ensemble des options du niveau non choisies** ; l'ancien ensemble est
    remplacé dans une transaction (retrait par suppression logique, ajout des manquantes) ;
  - idempotent ; inscription annulée → 422 ; inscription d'une autre école → 404 (filtre tenant).
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
3. **Fiche élève › onglet Options** : même bloc, modifiable par Secrétariat et Directeur. Mention
   « Options non renseignées : l'élève suit toutes les options » tant que `hasExplicitChoice` est faux.
   Alerte à l'enregistrement : « N notes seront masquées du bulletin (conservées) ».
4. **Saisie des notes** : aucun changement, hors une ligne « N élève(s) dispensé(s) de cette matière ».
5. **Aide** : fiche d'aide « Matières optionnelles » dans `help.js`.

## 7. Tests

Obligatoires (Finance, Notes, isolation tenant — AGENTS.md) :

- **Invariant** : sans dispense, sommaire de notes et bulletin identiques à avant (test de non-régression).
- Élève dispensé d'une matière notée : absente du sommaire, total des coefficients 22 au lieu de 25.
- `EvaluationStructureBuilder` : ligne dispensée absente, autres lignes inchangées.
- Résolution des groupes : un seul choix par groupe accepté, deux → 422 ; option sans groupe cumulable.
- `SetEnrollmentOptionsCommand` : idempotence, remplacement, matière hors niveau ou non optionnelle refusée.
- Saisie refusée sur matière dispensée ; import : ligne ignorée ; feuilles de notes sans l'élève dispensé.
- Isolation : la nouvelle table est protégée par RLS (`--filter Category=MultiTenant`).
- Tests JS pour le bloc d'options (formulaire d'inscription, fiche élève, réglages des matières).

## 8. Documentation

Cahier des charges §8.8 (règle fonctionnelle), Volume 3 (table), Volume 4 (routes),
`ACTIVE_CONTEXT.md`, fiche d'aide `help.js`.

## 9. Hors périmètre (YAGNI)

- Liste de classe « élèves aux options à renseigner » et affectation en masse.
- Choix imposé par groupe (« LV2 obligatoire ») : un groupe sans choix reste permis.
- Dispenses au niveau de l'élève sur plusieurs années.
- Emploi du temps et présences par option.

## 10. Points de vigilance

1. **Nouvelle matière dans un groupe existant** : un élève déjà placé (dispensé des autres) voit la
   nouvelle matière jusqu'à ce que le secrétariat mette son choix à jour — conséquence assumée du modèle
   par dispenses.
2. **Activation d'une option** : marquer « Espagnol » optionnelle ne change rien tant qu'aucun choix n'est
   enregistré ; c'est voulu, mais le secrétariat doit ensuite renseigner les choix.
3. **Six lecteurs** de la liste d'élèves et de matières (§4.2) : un lecteur oublié contredirait les autres.
   Le plan prévoit un test par lecteur.
4. **Changement de classe en cours d'année** : les dispenses suivent l'inscription ; un élève changé de
   classe conserve celles de l'inscription active. Le comportement exact avec une nouvelle inscription est
   à confirmer au plan (`CreateEnrollmentCommand`, `ClassroomPromotion`).
