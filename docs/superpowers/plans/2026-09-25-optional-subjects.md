# Dispense d'une matière obligatoire — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Statut : en attente de validation.** Aucune tâche ci-dessous n'est commencée. Ce plan **remplace** le plan de 4 976
lignes de la première version (options + dispenses sur `Subject.IsOptional`), rendu caduc par l'Évolution N°6.

**Goal :** Un élève peut être dispensé d'une matière **obligatoire** de sa classe, avec un **motif obligatoire**
lisible seulement par le Directeur et le Secrétariat. La matière sort de ses moyennes et de la saisie des notes ; son
bulletin la marque « Dispensé(e) » (coefficient barré, hors totaux). Sans dispense, tout est strictement comme avant.

**Architecture :** une table tenant `student_subject_exemptions` (élève, année, matière, motif). `SubjectFollowScope`
(Évolution N°6, `main`) reste la **seule porte** : `ExcludedSubjectsAsync` ajoute les dispensés, `RestrictedStudentsAsync`
les retire de la liste de classe, `EnsureFollowsAsync` les nomme, et une nouvelle `ExemptSubjectsAsync` alimente le
bulletin. Deux routes `GET/PUT /api/v1/class-subjects/students/{id}/exemptions` sous
`[Authorize(Roles = StaffRoles)]` (= `"Directeur,Secretariat"`). Le motif ne sort que de ce GET.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql (RLS, Global Query Filter), MediatR + FluentValidation, QuestPDF,
Alpine.js, xUnit + FluentAssertions (Testcontainers Postgres), `node --test`.

**Spec :** `docs/superpowers/specs/2026-09-25-optional-subjects-design.md` (décisions D1 à D11).

## Global Constraints

- PostgreSQL uniquement ; migrations **nouvelles**, jamais une migration appliquée modifiée. — `AGENTS.md`.
- Table tenant : `SchoolId`, **Global Query Filter + RLS** (les deux), `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE`),
  entrée dans `TenantTables`, `reset_school_data` et `delete_school_year` (migration à part). Modèle :
  `20260924213235_AddSubjectCoefficientOverrides` + `…ToPurges`, et `20260925100215_AddClassSubjectsAndOptions`.
- Suppression **logique** (`SoftDelete(actor)`), règle #6. CQRS MediatR, **aucune logique métier** dans le contrôleur ni
  l'entité (règles #7, #8). Erreurs normalisées : `ValidationException` → 422, `KeyNotFoundException` → 404.
- `schoolId` toujours issu du JWT / `ITenantProvider` ; l'année d'écriture est l'année **active** résolue serveur
  (`CoefficientRules.ActiveSchoolYearIdAsync`), jamais un paramètre client (règle #10).
- **Invariant :** sans dispense, chaque lecteur et le bulletin sont strictement ceux d'avant. **Les constructeurs des
  handlers existants ne changent pas** (`SubjectFollowScope` est déjà injecté partout) ; les DTO existants ne gagnent
  que des membres **facultatifs, en dernier**.
- **Règle #12** : « Dispensé(e) » est l'unique écart au bulletin de référence, validé et consigné (Tâche 8).
- **Le motif est une donnée sensible** : il n'existe ni dans un DTO de bulletin, ni dans `GetStudentDetail`, ni dans un
  PDF, ni dans un message d'erreur, ni dans le journal d'audit. Un test à chaîne sentinelle le garantit (Tâche 6).
- Le propriétaire lance lui-même la suite complète `dotnet test` (consigne du 17/09/2026) : ce plan n'exécute que des
  tests **ciblés** (`--filter`). `dotnet build` et `node --test` restent libres. Tests d'intégration : Docker requis.
- Base de dev : conteneur `sama-ecole-postgres`, base `sama_ecole_dev`. Après une migration : `dotnet ef database update`
  **avant** de relancer l'app (sinon « Une erreur inattendue » partout).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
  Staging **explicite** (jamais `git add .`). Worktree : `C:\Users\NCM\Documents\antigravity\sama_ecole-optional-subjects`.
- Fichiers de la première version : ils vivent dans le tag de sauvegarde (Tâche 0) ; on les **consulte**
  (`git show backup/optional-subjects-pre-n6:<chemin>`) mais on ne les cherry-pick pas — leur base (`cf4a2f6`) précède
  l'Évolution N°6.

## À valider avant la Tâche 0

| # | Question | Recommandation |
|---|---|---|
| V1 | Clé de la dispense (D7) : (élève, année, matière) plutôt que l'inscription | Oui |
| V2 | Éligibilité (D8) : programme de la classe sinon matières autonomes du niveau | Oui — sans repli, une classe « Collège » sans programme ne peut rien dispenser |
| V3 | Branche : garder le nom `feature/optional-subjects` en la **rebâtissant sur `origin/main`** (backup, `reset --hard`, `push --force-with-lease`) | Oui — origin porte l'ancienne histoire, aucune PR ouverte ; sinon nouvelle branche `feature/subject-exemptions` et `feature/optional-subjects` archivée |
| V4 | Ordre avec la PR de l'Évolution N°7 (`GetReportCardPdfQuery` modifié des deux côtés) | Attendre la fusion de N°7 avant la Tâche 4 — ou accepter un rebase |
| V5 | Absence de `xmin` sur la table (D11) | Oui |
| V6 | Migrations `AddOptionalSubjects*` de la première version : appliquées sur ta base de dev ? | À vérifier (Tâche 0.3) ; si oui, on les défait en dev avant de repartir |

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Domain/Entities/StudentSubjectExemption.cs` (créer) | Entité (`ITenantEntity`, `AuditableEntity`). |
| `src/SamaEcole.Persistence/Configurations/StudentSubjectExemptionConfiguration.cs`, `ApplicationDbContext.cs`, `Errors/UniqueConstraintCatalog.cs`, `Migrations/*`, `docs/migrations/*` | Table, index unique partiel, RLS, droits, purges. |
| `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs` | `DbSet<StudentSubjectExemption>`. |
| `src/SamaEcole.Application/ClassSubjects/SubjectExemptionRules.cs` (créer) | Règles pures : éligibilité, validation du corps. |
| `src/SamaEcole.Application/ClassSubjects/SubjectFollowRules.cs`, `SubjectFollowScope.cs` (modifier) | Surcharge pure ; `ExemptSubjectsAsync` ; exclusion, restriction, garde. |
| `src/SamaEcole.Application/ClassSubjects/Commands/SetStudentSubjectExemptionsCommand.cs`, `Queries/GetStudentSubjectExemptionsQuery.cs` (créer) | Écriture / lecture. |
| `src/SamaEcole.Application/Grades/Queries/GetGradeSummary/*` (modifier) | `ExemptSubjectDto`, `GradeSummaryDto.ExemptSubjects`. |
| `src/SamaEcole.Application/ReportCards/*` (modifier) | `ReportCardDto.ExemptSubjects`, `EvaluationStructureBuilder`, `EvaluationLineDto.IsExempt`. |
| `src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs` (modifier) | Ligne « Dispensé(e) », `GradeRows()`. |
| `src/SamaEcole.Web/Controllers/ClassSubjectsController.cs` (modifier) | Deux routes, `StaffRoles`. |
| `src/SamaEcole.Web/wwwroot/js/subject-exemptions.js` (créer), `students.js`, `help.js`, `Views/Students/Index.cshtml` (modifier) | Section « Dispenses ». |
| `tests/SamaEcole.{UnitTests,IntegrationTests}/ClassSubjects/*`, `tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs`, `src/SamaEcole.Web/tests/js/*` | Tests. |

---

### Task 0: Rebâtir la branche sur `main`

Prérequis : **V1 à V6 validées**. Aucune modification de code avant.

- [ ] **0.1** Vérifier le worktree propre : `git -C ../sama_ecole-optional-subjects status -sb`.
- [ ] **0.2** Sauvegarde : `git tag backup/optional-subjects-pre-n6 151bee6` (local ; `origin/feature/optional-subjects`
  garde l'ancienne histoire tant qu'on n'a pas poussé de force).
- [ ] **0.3** Base de dev : `docker exec sama-ecole-postgres psql -U sama_ecole -d sama_ecole_dev -c "select \"MigrationId\"
  from \"__EFMigrationsHistory\" where \"MigrationId\" like '%OptionalSubjects%';"`. Si `AddOptionalSubjects` et/ou
  `AddOptionalSubjectsToPurges` sont listées : les défaire **avant** le `reset` (tant que leurs fichiers existent) :
  `dotnet ef database update <migration précédente> -p src/SamaEcole.Persistence -s src/SamaEcole.Web`.
- [ ] **0.4** Mettre de côté les deux documents (spec + plan) hors du dépôt, `git fetch origin`, puis
  `git reset --hard origin/main` sur `feature/optional-subjects`, puis les recopier et les commiter :
  `docs(optional-subjects): réécriture — dispense d'une matière obligatoire sur SubjectFollowScope`.
- [ ] **0.5** `dotnet build` vert sur l'arbre neuf. Aucun `push` tant que le propriétaire n'a pas donné son accord pour le
  `--force-with-lease` (l'origine porte 17 commits qui disparaîtraient de la branche).

### Task 1: Données et isolation

**Files :** entité, configuration, `ApplicationDbContext`, `IApplicationDbContext`, `UniqueConstraintCatalog`, migrations
(2), `docs/migrations/*.sql` + `.rollback.sql`, `AuthApiFactory.cs`, `ClassSubjectsIsolationTests.cs` (étendre) ou
`StudentSubjectExemptionsIsolationTests.cs` (créer).

- [ ] **1.1 Test d'abord** (`Category=MultiTenant`, calqué sur `ClassSubjectsIsolationTests`) : une ligne de l'école A est
  invisible depuis B ; un `INSERT` avec le `SchoolId` de B depuis A est refusé par la RLS ; `DELETE` refusé pour
  `sama_ecole_app` ; second `INSERT` du même (élève, année, matière) → violation d'unicité ; après suppression logique,
  le doublon est permis. Échec attendu (table absente).
- [ ] **1.2** Entité `StudentSubjectExemption` (`SchoolId`, `StudentId`, `SchoolYearId`, `SubjectId`, `Reason` non nul) sur
  le gabarit de `StudentSubjectEnrollment`. Configuration : `student_subject_exemptions`, `Reason` `HasMaxLength(200)`
  requis, index unique partiel `UX_student_subject_exemptions_choice` `WHERE "IsDeleted" = false`, FK composites,
  Global Query Filter comme les tables voisines.
- [ ] **1.3** `dotnet ef migrations add AddStudentSubjectExemptions -p src/SamaEcole.Persistence -s src/SamaEcole.Web`, puis
  compléter `Up` (RLS + `TenantTables`, droits) sur le modèle de `20260925100215_AddClassSubjectsAndOptions`. Seconde
  migration `AddStudentSubjectExemptionsToPurges` : `reset_school_data` + `delete_school_year`.
- [ ] **1.4** Ajouter `student_subject_exemptions` à `UniqueConstraintCatalog` (message métier « cet élève est déjà dispensé
  de cette matière ») et à la purge de `AuthApiFactory` (`DELETE FROM student_subject_exemptions;`, avant
  `student_subject_enrollments`).
- [ ] **1.5** Scripts SQL idempotents + rollback dans `docs/migrations/` (pratique du commit `746672a`).
- [ ] **1.6** `dotnet ef database update` sur la base de dev. Tests 1.1 verts.
  Commit : `feat(exemptions): table des dispenses de matière obligatoire (RLS, purges)`.

### Task 2: Règles pures et `SubjectFollowScope`

**Files :** `SubjectExemptionRules.cs` (créer), `SubjectFollowRules.cs`, `SubjectFollowScope.cs` (modifier), tests unitaires
et d'intégration `ClassSubjects/`.

- [ ] **2.1 Tests purs d'abord** (`SubjectExemptionRulesTests`) : éligibilité (programme présent → actives sans groupe ;
  option d'un groupe, matière désactivée, matière hors programme → refusées ; **pas de programme** → matières autonomes du
  niveau, niveau comparé sans casse ni espaces, activité APC refusée) ; motif vide / blanc / 201 caractères → erreur sur
  `exemptions[i].reason` ; doublon → erreur. Et `SubjectFollowRulesTests` : nouvelle surcharge
  `ExcludedSubjects(classSubjects, chosen, exempt)` — les tests existants **inchangés**.
- [ ] **2.2** Implémenter les règles pures (aucun accès base).
- [ ] **2.3 Tests d'intégration** (`SubjectFollowScope` sur Postgres) : élève sans dispense → mêmes résultats qu'avant
  (**invariant**, y compris classe sans programme) ; élève dispensé → `ExcludedSubjectsAsync` contient la matière,
  `ExemptSubjectsAsync` aussi, sans motif ; `RestrictedStudentsAsync` renvoie « classe moins dispensés » (et `null`
  quand personne n'est dispensé) ; une ligne supprimée logiquement n'agit plus ; l'année A ne contamine pas l'année B ;
  `EnsureFollowsAsync` lève un 422 dont le message dit « dispensé(e) » et ne contient **pas** le motif (sentinelle).
- [ ] **2.4** Implémenter dans `SubjectFollowScope` : lecture des dispenses (une requête par (élève, année), **mémoïsée**
  comme `_excluded`) ; union dans `ResolveExcludedAsync` ; `ExemptSubjectsAsync` ; `RestrictedStudentsAsync` (charger
  `Students.Where(ClassroomId == classroomId)` seulement s'il existe des dispensés pour cette matière et cette année) ;
  message de `EnsureFollowsAsync`. **Constructeur inchangé.**
- [ ] **2.5** Tests 2.1 et 2.3 verts. Commit : `feat(exemptions): règles pures et résolution dans SubjectFollowScope`.

### Task 3: Moyennes et fiche élève

**Files :** `GetGradeSummaryQuery.cs` + handler, `GetStudentDetailQuery.cs`, tests `ClassSubjects/`.

- [ ] **3.1 Tests d'abord** : (a) **non-régression** sans dispense (sommaire identique, `ExemptSubjects` vide) ;
  (b) matière notée dispensée → absente de `Subjects`, total des coefficients 22 au lieu de 25, moyenne générale recalculée ;
  (c) `ExemptSubjects` = `(SubjectId, SubjectName, Coefficient effectif)` — coefficient de **classe** surchargé pris en
  compte (Évolution N°4) — et **aucun** membre de motif dans le DTO (test par réflexion sur `ExemptSubjectDto`) ;
  (d) primaire : coefficient neutralisé à 1 comme aujourd'hui ; (e) fiche élève (`BuildTermReportsAsync`) cohérente
  avec le bulletin, par année.
- [ ] **3.2** `ExemptSubjectDto(SubjectId, SubjectName, Coefficient)` ; `GradeSummaryDto.ExemptSubjects`
  (`IReadOnlyList<ExemptSubjectDto>? = null` → vide, **dernier** paramètre). `GetGradeSummaryQueryHandler` : après
  l'exclusion existante, lit `ExemptSubjectsAsync`, joint `Subjects` pour nom + coefficient, applique
  `overrides.Effective(...)` (secondaire) ; aucune modification du calcul des totaux.
- [ ] **3.3** `GetStudentDetail` : rien à changer pour l'exclusion (déjà via `ExcludedSubjectsAsync`) ; confirmer par test
  que la réponse ne contient aucun motif.
- [ ] **3.4** Vérifier les lecteurs de `GetGradeSummary` ajoutés par l'Évolution N°7 (PV du conseil, statistiques de
  délibération) **si N°7 est fusionnée** : aucun ne doit traiter une matière dispensée comme une note nulle.
- [ ] **3.5** Commit : `feat(exemptions): moyennes et fiche élève ignorent les matières dispensées`.

### Task 4: Bulletin PDF — la ligne « Dispensé(e) »

**Files :** `GetReportCardPdfQuery.cs` (`ReportCardDto`, `ReportCardDataService`), `EvaluationStructureBuilder.cs`,
`ReportCardDocument.cs`, tests `ReportCards/`. **Dépend de V4** (fichiers modifiés par l'Évolution N°7).

- [ ] **4.1 Tests d'abord** (`ReportCardDocument.GradeRows()`, `internal`) : secondaire → ligne « Dispensé(e) » au rang
  alphabétique, coefficient barré, pas de points ni « Moy x coef », totaux sans elle ; primaire → « Dispensé(e) » couvre
  Devoir/Comp/Moy ; grille APC → `IsExempt`, « Dispensé(e) » couvre Notes et Sur ; **sans dispense** → lignes identiques
  à avant ; le PDF se génère (comptage de pages, `GenerateImages`).
- [ ] **4.2** `ReportCardDto.ExemptSubjects` (facultatif, dernier) alimenté par `ReportCardDataService` depuis
  `summary.ExemptSubjects` ; `EvaluationStructureBuilder.BuildAsync` reçoit les matières dispensées (paramètre
  facultatif) et pose `EvaluationLineDto.IsExempt` ; `ReportCardDocument` rend la ligne dans les **trois** tableaux.
  Motif : nulle part. Bulletin arabe (`subjectNamesAr`) : la ligne reprend le libellé arabe de la matière si le
  gabarit le porte.
- [ ] **4.3 Contrôle visuel obligatoire** : générer un bulletin d'un élève dispensé (secondaire, primaire, APC) et
  regarder les images ; consigner ce qui a été vu dans le message de commit ou le compte rendu.
- [ ] **4.4** Commit : `feat(exemptions): ligne « Dispensé(e) » sur le bulletin`.

### Task 5: Feuilles de notes, import et saisie

**Files :** aucun handler à recompiler (déjà via `SubjectFollowScope`) ; tests `Grades/` ou `ClassSubjects/`. Seuls les
textes d'erreur de `ImportGradeSheetCommandHandler` sont élargis.

- [ ] **5.1 Tests** : élève dispensé absent de `GetClassGrades`, de la fiche PDF de saisie, du modèle Excel ; note via
  `CreateGrade` → 422 (« dispensé(e) », pas de motif) ; import : ligne avec note → erreur de ligne, ligne vide → sans
  effet ; les autres élèves de la classe ne sont pas affectés ; **non-régression** sans dispense.
- [ ] **5.2** Élargir le message d'import (« option non choisie, matière désactivée **ou élève dispensé** »).
- [ ] **5.3** Commit : `feat(exemptions): feuilles de notes, import et saisie excluent les élèves dispensés`.

### Task 6: API et confidentialité du motif

**Files :** `SetStudentSubjectExemptionsCommand.cs`, `GetStudentSubjectExemptionsQuery.cs`, `ClassSubjectsController.cs`,
`openapi.yaml` (Tâche 8), tests d'intégration et fonctionnels.

- [ ] **6.1 Tests d'abord** : `PUT` — idempotent (deux fois = un jeu de lignes) ; motif modifié → mise à jour sans
  doublon ; retrait → suppression logique ; matière inéligible, motif invalide, doublon, élève sans classe → 422 ;
  élève d'une autre école → 404 ; écrit sur l'année **active** uniquement. `GET` — matières éligibles avec `isExempt`,
  `reason`, `gradeCount` ; matière non dispensée → `reason` nul. **Rôles** (tests fonctionnels HTTP) : Directeur et
  Secrétariat → 200 ; Enseignant, Finance, Surveillant (et tout autre rôle) → **403 sur GET et sur PUT**.
  **Sentinelle** : un motif « SENTINELLE-MOTIF-42 » ne se retrouve ni dans `GET /students/{id}`, ni dans un
  `GradeSummaryDto` / `ReportCardDto` sérialisés, ni dans le corps d'une erreur 422, ni dans la ligne du journal
  d'audit de la commande.
- [ ] **6.2** `SetStudentSubjectExemptionsCommand(StudentId, Exemptions)` : `IAuditableRequest`, validateur FluentValidation
  (forme), règles pures de la Tâche 2 (métier), handler dans une transaction (année active, classe via
  `StudentYearClassroom`, éligibles chargés une fois, diff soft-delete / ajout / mise à jour). Réponse : `subjectId`
  dispensés. `GetStudentSubjectExemptionsQuery(StudentId)` : lecture seule, `AsNoTracking`, `gradeCount` par matière.
- [ ] **6.3** Contrôleur : deux routes sous `/api/v1/class-subjects/students/{studentId}/exemptions`, **chacune** avec
  `[Authorize(Roles = StaffRoles)]` (`StaffRoles` = `"Directeur,Secretariat"`), `ProducesResponseType` 200/403/404/422,
  `command with { StudentId = studentId }` comme `SetStudentOptions`. Aucune logique dans le contrôleur.
- [ ] **6.4** Commit : `feat(exemptions): API des dispenses et accès restreint au motif`.

### Task 7: Front — section « Dispenses » de la fiche élève

**Files :** `subject-exemptions.js` (créer : logique pure), `students.js`, `Views/Students/Index.cshtml`, tests
`src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs` (créer).

- [ ] **7.1 Tests JS d'abord** : motif obligatoire quand la case est cochée (bouton désactivé sinon — garde côté client,
  cf. « Erreur HTTP 400 brut » de la mémoire projet) ; message d'alerte « N notes seront masquées du bulletin
  (conservées) » selon `gradeCount` ; corps `PUT` = liste **complète** ; section **absente** et GET **non appelé** pour
  un rôle hors Directeur/Secrétariat.
- [ ] **7.2** Logique pure dans `subject-exemptions.js` ; section « Dispenses » dans la fiche élève sous « Matières
  optionnelles », composants partagés du projet (mémoire *shared-tab-nav*, *modal-dialog-design-conventions* si une
  confirmation modale est utilisée). Pour les autres rôles : badge « Dispensé(e) » **sans motif**, issu du bulletin.
- [ ] **7.3** `node --test` vert ; vérification visuelle (`npm run build:css`, app lancée) en tant que Directeur puis en
  tant qu'Enseignant.
- [ ] **7.4** Commit : `feat(exemptions): section Dispenses de la fiche élève`.

### Task 8: Documentation, aide, contexte actif

- [ ] **8.1** `docs/Volume_1_Cahier_des_Charges.md` **§8.9** « Dispense d'une matière obligatoire » (le §8.8 est pris par
  l'Évolution N°6 — l'ancien texte de cette branche portait le même numéro : il n'est pas repris).
- [ ] **8.2** `docs/Volume_3_DDS.md` (table `student_subject_exemptions`), `docs/Volume_4_API_Design.md` (deux routes,
  rôles), `docs/Volume_7_Security.md` (motif = donnée sensible : rôles, jamais journalisé ni exporté),
  `openapi.yaml` (deux routes, `ExemptSubjectDto`, `GradeSummaryDto.exemptSubjects`, `403` documenté).
- [ ] **8.3** `docs/design-references/README.md` : une ligne — « Dispensé(e) » est l'unique écart validé au bulletin.
- [ ] **8.4** `help.js` : fiche « Dispense d'une matière » (qui la saisit, qui voit le motif, ce que devient le bulletin).
- [ ] **8.5** `ACTIVE_CONTEXT.md` : section de l'évolution, invariants, points de vigilance de la spécification §10.
- [ ] **8.6** Commit : `docs(exemptions): cahier des charges §8.9, API, DDS, sécurité, aide et contexte actif`.

## Vérification finale (avant de proposer la fusion)

- [ ] `dotnet build` (0 erreur, sans nouvel avertissement), `node --test` (tous verts).
- [ ] Tests ciblés : `--filter "FullyQualifiedName~ClassSubjects|FullyQualifiedName~ReportCards|Category=MultiTenant"`.
- [ ] Suite complète : **lancée par le propriétaire** (build Release puis `--no-build`, ~32 min).
- [ ] `git branch --contains` / `git log origin/main..HEAD` : la branche ne contient que ce qui est prévu ; aucun reste de
  la première version (`Subject.IsOptional`, `enrollment_subject_exemptions`, `SubjectExemptions`).
- [ ] Push : `git push --force-with-lease origin feature/optional-subjects` **uniquement après accord explicite** (V3).

## Self-review (spec ↔ plan)

| Spécification | Tâche |
|---|---|
| D1 motif obligatoire, ≤ 200 | 2 (règles), 6 (422), 7 (garde client) |
| D2 notes conservées, alerte « N masquées » | 3 (masquage), 6 (`gradeCount`), 7 (alerte) |
| D3 ligne « Dispensé(e) », écart règle #12 | 4, 8.3 |
| D4 saisie inchangée | 5 (aucun écran) |
| D5 moyennes et total des coefficients | 3 |
| D6 une seule porte `SubjectFollowScope` | 2, 3, 5 |
| D7 clé (élève, année, matière) | 1, 2 |
| D8 éligibilité avec/sans programme | 2 |
| D9 accès au motif Directeur + Secrétariat | 6 (403, sentinelle), 7 (rôles), 8.2 |
| D10 année active seulement | 6 |
| D11 pas de `xmin` | 1 |
| §4 modèle, RLS, purges | 1 |
| §5 lecteurs, invariant | 2, 3, 5 |
| §6 rendu PDF | 4 |
| §7 API | 6 |
| §8 écrans | 7 |
| §9 tests, confidentialité, isolation | 1, 2, 3, 4, 5, 6, 7 |
| §10 vigilances | 3.4, 4 (V4), 8.5 |
